using System.Text.Json;
using Aurora.Runtime.Ecs;

namespace Aurora.Runtime.Events;

/// <summary>Uma ação de um evento. Campos usados dependem de <see cref="Type"/> (ver EventSystem).</summary>
public sealed class EventAction
{
    /// <summary>SetVariable | SetSwitch | Teleport | Destroy | Spawn | Wait | ShowMessage |
    /// AddItem | RemoveItem | SetQuestStage | AdvanceQuest | ...</summary>
    public string Type = "";

    /// <summary>Nome da variável/switch/item/quest, da entidade alvo (null = a própria entidade
    /// do evento), ou o caminho do prefab em Spawn.</summary>
    public string? Name;

    /// <summary>SetVariable: "Set" (padrão) ou "Add".</summary>
    public string? Op;

    /// <summary>
    /// Alcance da ação em pixels, medido a partir de quem disparou o evento. 0 (padrão) = a cena
    /// inteira. Só faz diferença quando o alvo é um grupo (<c>#etiqueta</c>): é o que separa uma
    /// bomba de um botão que mata todo inimigo do mapa.
    ///
    /// <para>Precisa de uma origem: num gatilho sem entidade de origem não há de onde medir, e o
    /// alcance é ignorado com aviso no console.</para>
    /// </summary>
    public float Radius;

    public float Value;
    public bool On = true;
    public float X;
    public float Y;
    public float Seconds;
    public string? Text;

    /// <summary>ShowMessage: caminho de uma textura (relativa a Assets) desenhada ao lado do
    /// texto — o retrato do personagem falando. Vazio/null = sem retrato.</summary>
    public string? Portrait;

    /// <summary>ShowMessage: true (padrão) trava o controle do jogador enquanto a caixa está na
    /// tela — o normal pra diálogo. Desligue pra texto informativo (tutorial, aviso) que não deve
    /// impedir o jogador de continuar andando. ShowChoice sempre trava — não dá pra navegar
    /// opções e andar ao mesmo tempo.</summary>
    public bool BlocksPlayer = true;

    /// <summary>
    /// Probabilidade de a ação acontecer, de 0 a 1. 1 (padrão) sempre roda. É o que expressa
    /// "30% de chance de largar a poção" sem precisar de variável nem script — e vale pra
    /// qualquer ação, não só Spawn.
    ///
    /// <para>O sorteio é por ação, não pela sequência: duas ações a 50% podem sair as duas,
    /// nenhuma, ou uma só. Pra escolher UMA entre várias, encadeie por switch.</para>
    /// </summary>
    public float Chance = 1f;

    /// <summary>ChangeScene: nome da entidade-marcador na cena de destino onde o jogador aparece.
    /// Vazio = cada entidade fica onde o arquivo da cena diz. Sem isto, uma porta que volta
    /// sempre joga o jogador no mesmo canto do mapa.</summary>
    public string? SpawnPoint;

    /// <summary>Opções de ShowChoice.</summary>
    public List<EventOption> Options = [];

    /// <summary>Lê uma lista de ações do mesmo formato usado por EventTrigger.Actions e
    /// UiButton.OnClick — centraliza o parsing pra não duplicar entre SceneSerializer e UIManager.</summary>
    public static List<EventAction> ParseList(JsonElement arrayElement)
    {
        var list = new List<EventAction>();
        foreach (var element in arrayElement.EnumerateArray())
        {
            var action = new EventAction
            {
                Type = element.TryGetProperty("Action", out var t) ? t.GetString() ?? "" : "",
                Name = element.TryGetProperty("Name", out var name) ? name.GetString() : null,
                Op = element.TryGetProperty("Op", out var op) ? op.GetString() : null,
                Value = element.TryGetProperty("Value", out var v) ? v.GetSingle() : 0f,
                Radius = element.TryGetProperty("Radius", out var r) ? r.GetSingle() : 0f,
                On = element.TryGetProperty("On", out var on) ? on.GetBoolean() : true,
                X = element.TryGetProperty("X", out var x) ? x.GetSingle() : 0f,
                Y = element.TryGetProperty("Y", out var y) ? y.GetSingle() : 0f,
                Seconds = element.TryGetProperty("Seconds", out var s) ? s.GetSingle() : 0f,
                Text = element.TryGetProperty("Text", out var txt) ? txt.GetString() : null,
                Portrait = element.TryGetProperty("Portrait", out var portrait) ? portrait.GetString() : null,
                BlocksPlayer = element.TryGetProperty("BlocksPlayer", out var bp) ? bp.GetBoolean() : true,
                Chance = element.TryGetProperty("Chance", out var chance) ? chance.GetSingle() : 1f,
                SpawnPoint = element.TryGetProperty("SpawnPoint", out var sp) ? sp.GetString() : null,
            };

            if (element.TryGetProperty("Options", out var options))
            {
                foreach (var optionElement in options.EnumerateArray())
                {
                    action.Options.Add(new EventOption
                    {
                        Text = optionElement.TryGetProperty("Text", out var ot) ? ot.GetString() ?? "" : "",
                        Switch = optionElement.TryGetProperty("Switch", out var sw) ? sw.GetString() : null,
                    });
                }
            }

            list.Add(action);
        }
        return list;
    }

    /// <summary>Escreve uma lista de ações no mesmo formato lido por <see cref="ParseList"/>.</summary>
    public static void WriteList(Utf8JsonWriter json, string propertyName, List<EventAction> actions)
    {
        json.WriteStartArray(propertyName);
        foreach (var action in actions)
        {
            json.WriteStartObject();
            json.WriteString("Action", action.Type);
            if (action.Name is not null) json.WriteString("Name", action.Name);
            if (action.Op is not null) json.WriteString("Op", action.Op);
            if (action.Value != 0f) json.WriteNumber("Value", action.Value);
            if (action.Radius != 0f) json.WriteNumber("Radius", action.Radius);
            if (!action.On) json.WriteBoolean("On", false);
            if (action.X != 0f) json.WriteNumber("X", action.X);
            if (action.Y != 0f) json.WriteNumber("Y", action.Y);
            if (action.Seconds != 0f) json.WriteNumber("Seconds", action.Seconds);
            if (action.Text is not null) json.WriteString("Text", action.Text);
            if (action.Portrait is not null) json.WriteString("Portrait", action.Portrait);
            if (!action.BlocksPlayer) json.WriteBoolean("BlocksPlayer", false);
            if (action.Chance != 1f) json.WriteNumber("Chance", action.Chance);
            if (!string.IsNullOrEmpty(action.SpawnPoint)) json.WriteString("SpawnPoint", action.SpawnPoint);

            if (action.Options.Count > 0)
            {
                json.WriteStartArray("Options");
                foreach (var option in action.Options)
                {
                    json.WriteStartObject();
                    json.WriteString("Text", option.Text);
                    if (option.Switch is not null)
                        json.WriteString("Switch", option.Switch);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
            }

            json.WriteEndObject();
        }
        json.WriteEndArray();
    }
}

/// <summary>Uma opção de ShowChoice: escolhida, liga o switch (encadeia outros eventos).</summary>
public sealed class EventOption
{
    public string Text = "";
    public string? Switch;
}

/// <summary>
/// Evento visual estilo RPG Maker: um gatilho e uma lista de ações executadas em
/// sequência (Wait pausa a sequência). Interpretado pelo EventSystem a cada frame.
/// </summary>
public sealed class EventTrigger : IComponent
{
    /// <summary>SceneStart | PlayerTouch | Touch | Death | SwitchOn | KeyPress | Timer |
    /// VariableCompare | HasItem | QuestStageAtLeast.</summary>
    public string Trigger = "PlayerTouch";

    /// <summary>Switch observado quando Trigger = SwitchOn.</summary>
    public string? Switch;

    /// <summary>Distância ao jogador que dispara PlayerTouch (pixels do mundo).</summary>
    public float Radius = 20f;

    /// <summary>
    /// Prefixo de nome que dispara o gatilho Touch (sem diferenciar maiúsculas). Vazio = qualquer
    /// entidade com Collider.
    ///
    /// <para>Touch usa a FORMA do collider, não a distância entre centros como PlayerTouch — é o
    /// que faz placa de pressão e zona de gatilho funcionarem com o tamanho que você desenhou, e
    /// o que permite reagir a algo que não seja o jogador (uma caixa empurrada, um projétil).</para>
    /// </summary>
    public string TargetPrefix = "";

    /// <summary>Tecla para KeyPress, ex: "Space", "E", "Enter". Nomes do enum Silk.NET.Input.Key.</summary>
    public string Key = "E";

    /// <summary>Intervalo em segundos entre disparos para Timer.</summary>
    public float Interval = 5f;

    /// <summary>Nome da variável (VariableCompare), item (HasItem) ou quest (QuestStageAtLeast) comparado.</summary>
    public string? Variable;

    /// <summary>Operador de comparação: ==, !=, &gt;=, &lt;=, &gt;, &lt;</summary>
    public string CompareOp = ">=";

    /// <summary>Valor de comparação (quantidade de item / número do estágio / valor da variável).</summary>
    public float CompareValue;

    /// <summary>True = dispara uma única vez.</summary>
    public bool Once = true;

    public List<EventAction> Actions = [];

    // Estado de execução (não serializado).
    internal bool Fired;
    internal bool Running;
    internal int ActionIndex;
    internal float WaitTimer;
    internal bool WaitingDialogue;

    /// <summary>Sequência pausada esperando a ação MoveTo chegar ao destino (ver
    /// EventSystem.Advance). Guarda o id da entidade em vez de uma referência: entidade pode
    /// morrer no meio do trajeto, e Entity é só um handle — checar IsAlive é o jeito de saber.</summary>
    internal bool WaitingMove;
    internal int WaitingMoveEntityId;
    internal float _timer; // acumulador para Timer
}
