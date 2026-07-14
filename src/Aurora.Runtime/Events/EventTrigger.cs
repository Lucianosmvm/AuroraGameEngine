using System.Text.Json;
using Aurora.Runtime.Ecs;

namespace Aurora.Runtime.Events;

/// <summary>Uma ação de um evento. Campos usados dependem de <see cref="Type"/> (ver EventSystem).</summary>
public sealed class EventAction
{
    /// <summary>SetVariable | SetSwitch | Teleport | Destroy | Wait | ShowMessage | AddItem |
    /// RemoveItem | SetQuestStage | AdvanceQuest | ...</summary>
    public string Type = "";

    /// <summary>Nome da variável/switch/item/quest, ou da entidade alvo (null = a própria entidade do evento).</summary>
    public string? Name;

    /// <summary>SetVariable: "Set" (padrão) ou "Add".</summary>
    public string? Op;

    public float Value;
    public bool On = true;
    public float X;
    public float Y;
    public float Seconds;
    public string? Text;

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
                On = element.TryGetProperty("On", out var on) ? on.GetBoolean() : true,
                X = element.TryGetProperty("X", out var x) ? x.GetSingle() : 0f,
                Y = element.TryGetProperty("Y", out var y) ? y.GetSingle() : 0f,
                Seconds = element.TryGetProperty("Seconds", out var s) ? s.GetSingle() : 0f,
                Text = element.TryGetProperty("Text", out var txt) ? txt.GetString() : null,
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
            if (!action.On) json.WriteBoolean("On", false);
            if (action.X != 0f) json.WriteNumber("X", action.X);
            if (action.Y != 0f) json.WriteNumber("Y", action.Y);
            if (action.Seconds != 0f) json.WriteNumber("Seconds", action.Seconds);
            if (action.Text is not null) json.WriteString("Text", action.Text);

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
    /// <summary>SceneStart | PlayerTouch | SwitchOn | KeyPress | Timer | VariableCompare |
    /// HasItem | QuestStageAtLeast.</summary>
    public string Trigger = "PlayerTouch";

    /// <summary>Switch observado quando Trigger = SwitchOn.</summary>
    public string? Switch;

    /// <summary>Distância ao jogador que dispara PlayerTouch (pixels do mundo).</summary>
    public float Radius = 20f;

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
    internal float _timer; // acumulador para Timer
}
