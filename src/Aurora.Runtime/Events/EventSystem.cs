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

    /// <summary>ShowMessage entrega o texto aqui — a camada de UI do jogo decide como exibir.</summary>
    public event Action<string>? MessageShown;

    /// <summary>Disparado pela ação ChangeScene. O SceneManager assina e executa a transição.</summary>
    public event Action<string>? SceneChangeRequested;

    private bool _sceneStartFired;

    /// <summary>Reseta o estado de disparo — chamado ao carregar uma nova cena.</summary>
    public void Reset() => _sceneStartFired = false;

    public EventSystem(World world, GameState state)
    {
        _world = world;
        _state = state;
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

            if (ShouldFire(trigger, transform, playerPosition))
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

    private bool ShouldFire(EventTrigger trigger, Transform transform, Vector2? playerPosition)
        => trigger.Trigger switch
        {
            "SceneStart"       => !_sceneStartFired,
            "PlayerTouch"      => playerPosition is { } p
                                  && Vector2.Distance(p, transform.Position) <= trigger.Radius,
            "SwitchOn"         => trigger.Switch is not null && _state.GetSwitch(trigger.Switch),
            "KeyPress"         => Input?.WasKeyPressed(ParseKey(trigger.Key)) ?? false,
            "Timer"            => trigger._timer >= trigger.Interval,
            "VariableCompare"  => trigger.Variable is not null
                                  && Compare(_state.GetVariable(trigger.Variable),
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

            // Uma ação com referência inválida (arquivo, entidade) não deve derrubar o jogo
            // inteiro - loga e segue pra próxima ação/gatilho.
            try
            {
                Execute(self, action);
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

    private void Execute(Entity self, EventAction action)
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
                var target = ResolveTarget(self, action.Name);
                var transform = target?.Get<Transform>();
                if (transform is not null)
                    transform.Position = new Vector2(action.X, action.Y);
                break;
            }

            case "Destroy":
                ResolveTarget(self, action.Name)?.Destroy();
                break;

            case "ShowMessage" when action.Text is not null:
                // Name = nome do falante (opcional).
                Dialogue?.ShowMessage(action.Text, action.Name);
                MessageShown?.Invoke(action.Text);
                break;

            case "Save":
                Save?.Save((int)action.Value);
                break;

            case "ChangeScene" when action.Name is not null:
                SceneChangeRequested?.Invoke(action.Name);
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

    private Entity? ResolveTarget(Entity self, string? name)
    {
        if (name is null || name.Equals("Self", StringComparison.OrdinalIgnoreCase))
            return self;

        return _world.TryFind(name, out var entity) ? entity : null;
    }
}
