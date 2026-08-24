using System.Numerics;
using Aurora.Runtime.Audio;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Input;
using Aurora.Runtime.Saves;
using Aurora.Runtime.UI;
using Silk.NET.Input;

namespace Aurora.Runtime.Events;

/// <summary>
/// Interpreta os <see cref="EventTrigger"/> do mundo a cada frame: verifica gatilhos
/// e executa as ações em sequência (Wait suspende até o tempo passar).
/// </summary>
public sealed class EventSystem
{
    private readonly World _world;
    private readonly GameState _state;
    private readonly List<(Entity Entity, Transform Transform, EventTrigger Trigger)> _buffer = [];

    /// <summary>Entidade considerada "o jogador" para gatilhos PlayerTouch.</summary>
    public string PlayerEntityName { get; set; } = "Player";

    /// <summary>
    /// Quando presente, ShowMessage/ShowChoice abrem a caixa de diálogo e a sequência
    /// de ações pausa até o jogador dispensar (modelo RPG Maker).
    /// </summary>
    public DialogueSystem? Dialogue { get; set; }

    /// <summary>Quando presente, KeyPress detecta teclas pressionadas.</summary>
    public InputManager? Input { get; set; }

    /// <summary>Quando presente, PlaySound/PlayMusic/StopMusic reproduzem áudio.</summary>
    public AudioManager? Audio { get; set; }

    /// <summary>Quando presente, a ação Save grava o estado em disco.</summary>
    public SaveManager? Save { get; set; }

    /// <summary>Quando presente, ações AddItem/RemoveItem e gatilho HasItem operam aqui.</summary>
    public InventoryManager? Inventory { get; set; }

    /// <summary>Quando presente, a ação UseItem acha a ficha do item e roda o efeito dela.</summary>
    public Database.ItemDatabase? Items { get; set; }

    /// <summary>Quando presente, ações SetQuestStage/AdvanceQuest e gatilho QuestStageAtLeast operam aqui.</summary>
    public QuestManager? Quests { get; set; }

    /// <summary>Quando presente, ações ShowUI/HideUI/ToggleUI mostram/escondem telas já carregadas
    /// via <see cref="UIManager.Load"/> (tipicamente em OnLoad).</summary>
    public UIManager? UI { get; set; }

    /// <summary>ShowMessage entrega o texto aqui — a camada de UI do jogo decide como exibir.</summary>
    public event Action<string>? MessageShown;

    /// <summary>Disparado pela ação ChangeScene, com o caminho da cena e o nome do marcador onde
    /// o jogador deve aparecer (null = cada entidade fica onde o arquivo da cena manda). O
    /// SceneManager assina e executa a transição.</summary>
    public event Action<string, string?>? SceneChangeRequested;

    /// <summary>Disparado pela ação Quit. O Game assina e chama Exit() (fecha a janela/app).</summary>
    public event Action? QuitRequested;

    private bool _sceneStartFired;

    /// <summary>Reseta o estado de disparo — chamado ao carregar uma nova cena.</summary>
    public void Reset() => _sceneStartFired = false;

    public EventSystem(World world, GameState state)
    {
        _world = world;
        _state = state;
        _world.EntityDied += RunDeathTrigger;
    }

    /// <summary>
    /// Roda as ações de um gatilho "Death" no instante da morte, com a entidade ainda de pé —
    /// é a única janela em que dá pra ler a posição dela pra largar o loot no lugar certo.
    ///
    /// <para>A sequência sai inteira de uma vez: <c>Wait</c> e <c>ShowChoice</c> não suspendem
    /// aqui, porque um segundo depois a entidade dona do evento não existe mais e não haveria
    /// quem retomasse. Pra cutscene depois da morte, ligue um switch aqui e use SwitchOn numa
    /// entidade que continua viva.</para>
    /// </summary>
    private void RunDeathTrigger(Entity entity)
    {
        if (entity.Get<EventTrigger>() is not { Trigger: "Death" } trigger)
            return;

        if (trigger.Once && trigger.Fired)
            return;

        trigger.Fired = true;
        foreach (var action in trigger.Actions)
            ExecuteWithChance(entity, action);
    }

    public void Update(float deltaTime)
    {
        // Snapshot: ações podem destruir entidades no meio da varredura.
        _buffer.Clear();
        foreach (var entry in _world.Query<Transform, EventTrigger>())
            _buffer.Add(entry);

        Vector2? playerPosition = _world.TryFind(PlayerEntityName, out var player)
            ? player.Get<Transform>()?.Position
            : null;

        foreach (var (entity, transform, trigger) in _buffer)
        {
            // Timer acumula sempre, mesmo enquanto a sequência está rodando
            if (trigger.Trigger == "Timer")
                trigger._timer += deltaTime;

            if (trigger.Running)
            {
                Advance(entity, trigger, deltaTime);
                continue;
            }

            if (trigger.Once && trigger.Fired)
                continue;

            if (ShouldFire(entity, trigger, transform, playerPosition))
            {
                if (trigger.Trigger == "Timer")
                    trigger._timer = 0f;
                trigger.Fired = true;
                trigger.Running = true;
                trigger.ActionIndex = 0;
                trigger.WaitTimer = 0f;
                Advance(entity, trigger, deltaTime);
            }
        }

        _sceneStartFired = true;
    }

    private bool ShouldFire(Entity entity, EventTrigger trigger, Transform transform, Vector2? playerPosition)
        => trigger.Trigger switch
        {
            "Touch"            => TouchedThisFrame(entity, trigger),
            "SceneStart"       => !_sceneStartFired,
            "PlayerTouch"      => playerPosition is { } p
                                  && Vector2.Distance(p, transform.Position) <= trigger.Radius,
            // Death não é avaliado aqui: chega por World.EntityDied, no instante da morte. Se
            // dependesse desta varredura, a entidade já teria sido destruída pelo Health e o
            // evento nunca veria a posição onde largar o loot.
            "Death"            => false,
            _ => ShouldFireRest(trigger),
        };

    /// <summary>Alguém encostou nesta entidade neste frame, pela forma dos colliders.</summary>
    private bool TouchedThisFrame(Entity self, EventTrigger trigger)
    {
        foreach (var (a, b) in _world.OverlapsThisFrame)
        {
            int otherId = a == self.Id ? b : b == self.Id ? a : 0;
            if (otherId == 0 || !_world.IsAlive(otherId))
                continue;

            if (trigger.TargetPrefix.Length == 0
                || _world.GetName(otherId).StartsWith(trigger.TargetPrefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private bool ShouldFireRest(EventTrigger trigger)
        => trigger.Trigger switch
        {
            "SwitchOn"         => trigger.Switch is not null && _state.GetSwitch(trigger.Switch),
            "KeyPress"         => Input?.WasKeyPressed(ParseKey(trigger.Key)) ?? false,
            "Timer"            => trigger._timer >= trigger.Interval,
            "VariableCompare"  => trigger.Variable is not null
                                  && Compare(_state.GetVariable(trigger.Variable),
                                             trigger.CompareOp, trigger.CompareValue),
            "HasItem"          => trigger.Variable is not null
                                  && Compare(Inventory?.GetCount(trigger.Variable) ?? 0,
                                             trigger.CompareOp, trigger.CompareValue),
            "QuestStageAtLeast" => trigger.Variable is not null
                                  && Compare(Quests?.GetStage(trigger.Variable) ?? 0,
                                             trigger.CompareOp, trigger.CompareValue),
            _ => false,
        };

    private static Key ParseKey(string name)
        => Enum.TryParse<Key>(name, ignoreCase: true, out var k) ? k : Key.Unknown;

    private static bool Compare(float actual, string op, float value) => op switch
    {
        ">=" => actual >= value,
        "<=" => actual <= value,
        ">"  => actual > value,
        "<"  => actual < value,
        "!=" => MathF.Abs(actual - value) > 1e-6f,
        _    => MathF.Abs(actual - value) < 1e-6f,   // "==" default
    };

    /// <summary>Executa ações imediatamente, sem entidade "dona" — usado por UiButton.OnClick.
    /// Wait é ignorado (clique é síncrono) e "Self"/null não resolve a nenhuma entidade;
    /// ações que miram entidade (Teleport, Destroy, PlayAnimation…) precisam de Name explícito.</summary>
    public void RunActions(IEnumerable<EventAction> actions)
    {
        // Lista materializada e cursor por índice (em vez de foreach) porque If/Else/EndIf
        // desviam o fluxo — o botão de HUD tem que condicionar igual a um evento de cena.
        var list = actions as List<EventAction> ?? [.. actions];

        for (int i = 0; i < list.Count; )
        {
            var action = list[i];

            if (action.Type == "Wait")
            {
                i++;
                continue;
            }

            if (IsBranch(action.Type))
            {
                i = ResolveBranch(list, i);
                continue;
            }

            i++;

            try
            {
                ExecuteWithChance(null, action);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[EventSystem] Falha na ação de UiButton '{action.Type}': {ex.Message}");
            }
        }
    }

    private void Advance(Entity self, EventTrigger trigger, float deltaTime)
    {
        if (trigger.WaitTimer > 0f)
        {
            trigger.WaitTimer -= deltaTime;
            if (trigger.WaitTimer > 0f)
                return;
        }

        if (trigger.WaitingDialogue)
        {
            if (Dialogue?.IsActive == true)
                return;
            trigger.WaitingDialogue = false;
        }

        while (trigger.ActionIndex < trigger.Actions.Count)
        {
            var action = trigger.Actions[trigger.ActionIndex];
            trigger.ActionIndex++;

            if (action.Type == "Wait")
            {
                trigger.WaitTimer = action.Seconds;
                if (trigger.WaitTimer > 0f)
                    return; // Retoma no próximo frame, após o tempo passar.
                continue;
            }

            // Antes do sorteio de Chance de propósito: um If que "não saiu" deixaria o Else e o
            // EndIf órfãos e a sequência executaria os dois lados.
            if (IsBranch(action.Type))
            {
                trigger.ActionIndex = ResolveBranch(trigger.Actions, trigger.ActionIndex - 1);
                continue;
            }

            // Uma ação com referência inválida (arquivo, entidade) não deve derrubar o jogo
            // inteiro - loga e segue pra próxima ação/gatilho.
            try
            {
                ExecuteWithChance(self, action);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[EventSystem] Falha na ação '{action.Type}': {ex.Message}");
            }

            // Diálogo aberto: pausa a sequência até o jogador dispensar.
            if (action.Type is "ShowMessage" or "ShowChoice" && Dialogue?.IsActive == true)
            {
                trigger.WaitingDialogue = true;
                return;
            }
        }

        trigger.Running = false;
    }

    /// <summary>Ação de controle de fluxo: não "faz" nada, só desvia o cursor da sequência.</summary>
    internal static bool IsBranch(string type) => type is "If" or "Else" or "EndIf";

    /// <summary>
    /// Onde a sequência continua depois de encontrar um If/Else/EndIf.
    ///
    /// <para>Marcadores numa lista plana, e não uma árvore de ações aninhadas, porque é assim que
    /// o editor já edita eventos (uma lista que se arrasta) e assim que o JSON já é lido — uma
    /// árvore obrigaria a refazer os dois pra ganhar a mesma coisa. Aninhar funciona por contagem
    /// de profundidade: um If dentro de outro acha o próprio Else.</para>
    /// </summary>
    private int ResolveBranch(List<EventAction> actions, int index)
    {
        var action = actions[index];

        return action.Type switch
        {
            // Condição verdadeira entra no corpo; falsa pula pro Else (se houver) ou pro fim.
            "If" => EvaluateCondition(action) ? index + 1 : FindMatch(actions, index, stopAtElse: true),

            // Só se chega num Else caindo do corpo de um If verdadeiro — então o corpo do Else
            // tem que ser pulado inteiro.
            "Else" => FindMatch(actions, index, stopAtElse: false),

            _ => index + 1,   // EndIf: marcador, segue em frente
        };
    }

    /// <summary>Índice logo após o Else (quando stopAtElse) ou o EndIf que fecha este bloco.
    /// Bloco sem EndIf termina a sequência, em vez de estourar o índice.</summary>
    private static int FindMatch(List<EventAction> actions, int index, bool stopAtElse)
    {
        int depth = 0;

        for (int i = index + 1; i < actions.Count; i++)
        {
            switch (actions[i].Type)
            {
                case "If":
                    depth++;
                    break;

                case "Else" when depth == 0 && stopAtElse:
                    return i + 1;

                case "EndIf" when depth == 0:
                    return i + 1;

                case "EndIf":
                    depth--;
                    break;
            }
        }

        return actions.Count;
    }

    /// <summary>
    /// Testa a condição de um If. <c>Text</c> escolhe o que comparar — "Switch", "Item", "Quest"
    /// ou "Variable" (padrão); <c>Name</c> é o nome consultado, <c>Op</c> o operador e
    /// <c>Value</c> o valor. Reaproveita os campos que a ação já tinha, sem inventar formato novo.
    /// </summary>
    /// <summary>
    /// Avalia uma condição no formato da ação If, de fora da sequência de eventos. É por aqui que
    /// as tabelas de spawn testam "zumbi só à noite" — a condição é a mesma coisa nos dois
    /// lugares, então não existem duas linguagens de condição no projeto.
    /// </summary>
    public bool TestCondition(EventAction condition) => EvaluateCondition(condition);

    private bool EvaluateCondition(EventAction action)
    {
        if (action.Name is null)
            return false;

        return (action.Text ?? "Variable") switch
        {
            "Switch" => _state.GetSwitch(action.Name) == action.On,
            "Item"   => Compare(Inventory?.GetCount(action.Name) ?? 0, action.Op ?? ">=", action.Value),
            "Quest"  => Compare(Quests?.GetStage(action.Name) ?? 0, action.Op ?? ">=", action.Value),
            _        => Compare(_state.GetVariable(action.Name), action.Op ?? ">=", action.Value),
        };
    }

    /// <summary>Sorteia o <see cref="EventAction.Chance"/> e executa se passar.</summary>
    private bool ExecuteWithChance(Entity? self, EventAction action)
    {
        if (action.Chance < 1f && _random.NextDouble() >= action.Chance)
            return false;

        Execute(self, action);
        return true;
    }

    private readonly Random _random = new();

    private void Execute(Entity? self, EventAction action)
    {
        switch (action.Type)
        {
            case "SetVariable" when action.Name is not null:
                if (string.Equals(action.Op, "Add", StringComparison.OrdinalIgnoreCase))
                    _state.AddVariable(action.Name, action.Value);
                else
                    _state.SetVariable(action.Name, action.Value);
                break;

            case "SetSwitch" when action.Name is not null:
                _state.SetSwitch(action.Name, action.On);
                break;

            case "Teleport":
            {
                // Leva os filhos junto (ver World.TeleportWithChildren): escrever Position direto
                // moveria só o alvo, e o que estivesse preso nele ficaria pra trás pra sempre —
                // o vínculo pai/filho preserva o encaixe, não o recalcula.
                if (ResolveTarget(self, action.Name) is { } target && target.Get<Transform>() is not null)
                    _world.TeleportWithChildren(target, new Vector2(action.X, action.Y));
                break;
            }

            case "Destroy":
                ResolveTarget(self, action.Name)?.Destroy();
                break;

            case "SetWeather" when action.Name is not null:
            {
                // Name = tipo, Value = intensidade, Text = entidade dona do Weather (vazio = a
                // primeira da cena, que é o caso normal: uma entidade de clima por mapa).
                var weatherEntity = string.IsNullOrEmpty(action.Text)
                    ? _world.Query<Weather>().Select(e => (Entity?)e.Entity).FirstOrDefault()
                    : ResolveTarget(self, action.Text);

                if (weatherEntity?.Get<Weather>() is { } weather)
                    weather.Set(action.Name, action.Value);
                else
                    Console.Error.WriteLine("[EventSystem] SetWeather: nenhuma entidade com Weather na cena.");

                break;
            }

            case "Spawn" when action.Name is not null:
            {
                // X/Y são deslocamento a partir de quem disparou o evento: assim o MESMO arquivo
                // de prefab serve pra quantos pontos de spawn a cena tiver, sem editar o prefab
                // nem duplicá-lo. Num gatilho sem entidade de origem a origem é (0,0), então
                // X/Y viram posição absoluta — os dois usos saem da mesma conta.
                var origin = self?.Get<Transform>()?.Position ?? Vector2.Zero;
                _world.Spawn(action.Name, origin + new Vector2(action.X, action.Y));
                break;
            }

            case "Damage":
                if (ResolveTarget(self, action.Name) is { } damageTarget)
                    _world.Damage(damageTarget, action.Value, self);
                break;

            case "Heal":
                if (ResolveTarget(self, action.Name) is { } healTarget)
                    _world.Heal(healTarget, action.Value);
                break;

            case "ShowMessage" when action.Text is not null:
                // Name = nome do falante (opcional).
                Dialogue?.ShowMessage(action.Text, action.Name);
                MessageShown?.Invoke(action.Text);
                break;

            case "Save":
                Save?.Save((int)action.Value);
                break;

            case "AddItem" when action.Name is not null:
                Inventory?.Add(action.Name, (int)action.Value);
                break;

            case "UseItem" when action.Name is not null:
            {
                // Text = quem usa (vazio = o jogador). O efeito do item é uma lista de ações
                // comum, então ele roda com essa entidade como "self" — é o que faz
                // { "Action": "Heal", "Value": 50 } curar quem tomou a poção, sem nomear ninguém.
                if (Items?.Get(action.Name) is not { } definition)
                {
                    Console.Error.WriteLine($"[EventSystem] UseItem: item '{action.Name}' não está no banco.");
                    break;
                }

                if (Inventory is not null && !Inventory.Has(action.Name))
                    break;

                string userName = string.IsNullOrEmpty(action.Text) ? PlayerEntityName : action.Text;
                Entity? user = _world.TryFind(userName, out var found) ? found : self;

                foreach (var step in definition.Effect)
                    ExecuteWithChance(user, step);

                if (definition.Consumable)
                    Inventory?.Remove(action.Name, 1);

                break;
            }

            case "RemoveItem" when action.Name is not null:
                Inventory?.Remove(action.Name, (int)action.Value);
                break;

            case "SetQuestStage" when action.Name is not null:
                Quests?.SetStage(action.Name, (int)action.Value);
                break;

            case "AdvanceQuest" when action.Name is not null:
                Quests?.Advance(action.Name, action.Value == 0f ? 1 : (int)action.Value);
                break;

            case "ShowUI" when action.Name is not null:
                UI?.Show(action.Name);
                break;

            case "HideUI" when action.Name is not null:
                UI?.Hide(action.Name);
                break;

            case "ToggleUI" when action.Name is not null:
                UI?.Toggle(action.Name);
                break;

            case "ChangeScene" when action.Name is not null:
                SceneChangeRequested?.Invoke(action.Name,
                    string.IsNullOrEmpty(action.SpawnPoint) ? null : action.SpawnPoint);
                break;

            // Congela World.Update (behaviors, colisão, partículas, vida) sem descarregar a
            // cena — pra menu de pausa/inventário/configurações abrir por cima do jogo parado.
            // UI continua respondendo a clique normalmente enquanto pausado.
            case "SetPause":
                _world.Paused = action.On;
                break;

            case "Quit":
                QuitRequested?.Invoke();
                break;

            case "PlaySound" when action.Name is not null:
                Audio?.Play(action.Name, action.Value > 0f ? action.Value : 1f);
                break;

            case "PlayMusic" when action.Name is not null:
                Audio?.PlayMusic(action.Name, action.On, action.Value > 0f ? action.Value : 1f);
                break;

            case "StopMusic":
                Audio?.StopMusic();
                break;

            case "PlayAnimation" when action.Text is not null:
                ResolveTarget(self, action.Name)?.Get<Animator>()?.Play(action.Text, restart: true);
                break;

            case "StopAnimation":
                ResolveTarget(self, action.Name)?.Get<Animator>()?.Stop();
                break;

            case "SetActive":
            {
                // Liga/desliga efeitos sem remover o componente da cena (chuva, tocha, etc).
                // Name = entidade alvo (null/"Self" = a própria), On = liga/desliga.
                if (ResolveTarget(self, action.Name) is { } activeTarget)
                {
                    var particles = activeTarget.Get<ParticleEmitter>();
                    if (particles is not null)
                        particles.Emitting = action.On;

                    var light = activeTarget.Get<Light2D>();
                    if (light is not null)
                        light.Enabled = action.On;

                    var tint = activeTarget.Get<GlobalTint>();
                    if (tint is not null)
                        tint.Enabled = action.On;
                }
                break;
            }

            case "ShowChoice" when Dialogue is not null && action.Options.Count > 0:
                Dialogue.ShowChoice(action.Text ?? "",
                    action.Options.Select(o => o.Text).ToList(),
                    index =>
                    {
                        var option = action.Options[index];
                        if (option.Switch is not null)
                            _state.SetSwitch(option.Switch, true);
                        // Name = variável que recebe o índice escolhido (opcional).
                        if (action.Name is not null)
                            _state.SetVariable(action.Name, index);
                    });
                break;
        }
    }

    private Entity? ResolveTarget(Entity? self, string? name)
    {
        if (name is null || name.Equals("Self", StringComparison.OrdinalIgnoreCase))
            return self;

        return _world.TryFind(name, out var entity) ? entity : null;
    }
}
