using Aurora.Runtime.Ecs;

namespace Aurora.Runtime.Events;

/// <summary>Uma ação de um evento. Campos usados dependem de <see cref="Type"/> (ver EventSystem).</summary>
public sealed class EventAction
{
    /// <summary>SetVariable | SetSwitch | Teleport | Destroy | Wait | ShowMessage.</summary>
    public string Type = "";

    /// <summary>Nome da variável/switch, ou da entidade alvo (null = a própria entidade do evento).</summary>
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
    /// <summary>SceneStart | PlayerTouch | SwitchOn.</summary>
    public string Trigger = "PlayerTouch";

    /// <summary>Switch observado quando Trigger = SwitchOn.</summary>
    public string? Switch;

    /// <summary>Distância ao jogador que dispara PlayerTouch (pixels do mundo).</summary>
    public float Radius = 20f;

    /// <summary>True = dispara uma única vez.</summary>
    public bool Once = true;

    public List<EventAction> Actions = [];

    // Estado de execução (não serializado).
    internal bool Fired;
    internal bool Running;
    internal int ActionIndex;
    internal float WaitTimer;
    internal bool WaitingDialogue;
}
