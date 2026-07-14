using System.Numerics;
using Aurora.Runtime.Events;
using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.UI;

/// <summary>Elemento de tela (HUD/menu) em coordenadas de pixel de tela — não segue a câmera.</summary>
public abstract class UiElement
{
    public string Name = "";
    public float X;
    public float Y;

    /// <summary>Left (padrão: X é a borda esquerda, pixel absoluto — bom pra HUD grudado no
    /// canto) | Center (X é deslocamento a partir do centro horizontal da tela — bom pra
    /// menu, funciona igual em qualquer resolução) | Right (X é deslocamento a partir da
    /// borda direita). Sem isso, coordenada fixa só fica correta numa resolução específica —
    /// telas de Android reais são bem mais largas que a referência 1280x720 usada ao autorar.</summary>
    public string AnchorX = "Left";

    /// <summary>Top (padrão) | Center | Bottom — mesma ideia do AnchorX, eixo vertical.</summary>
    public string AnchorY = "Top";
}

/// <summary>Texto com suporte a tokens: {Nome} (variável do GameState), {Item:Nome} (quantidade
/// no inventário), {Quest:Nome} (estágio da quest) — resolvidos a cada frame no Draw.</summary>
public sealed class UiText : UiElement
{
    public string Text = "";
    public string Color = "#FFFFFFFF";
    public float Scale = 1f;
}

/// <summary>Ícone/imagem estática (textura resolvida no Load).</summary>
public sealed class UiImage : UiElement
{
    public string? TexturePath;
    internal Texture2D? Texture;
    public float Width;
    public float Height;
    public string Color = "#FFFFFFFF";
}

/// <summary>Barra de progresso (vida, mana, XP…) lendo uma variável do GameState (0..Max).</summary>
public sealed class UiBar : UiElement
{
    public float Width = 100f;
    public float Height = 12f;
    public string Variable = "";
    public float Max = 100f;
    public string FillColor = "#40C040FF";
    public string BackColor = "#303030FF";
}

/// <summary>Retângulo sólido — fundo de janela/painel.</summary>
public sealed class UiPanel : UiElement
{
    public float Width = 100f;
    public float Height = 100f;
    public string Color = "#000000AA";
}

/// <summary>Botão clicável (mouse no Windows, toque no Android via InputManager.SetPointer).
/// OnClick usa o mesmo vocabulário de ações do EventTrigger, rodado por
/// <see cref="Aurora.Runtime.Events.EventSystem.RunActions"/> ao clicar/tocar.</summary>
public sealed class UiButton : UiElement
{
    public float Width = 120f;
    public float Height = 32f;
    public string Text = "";
    public string Color = "#3A3860FF";
    public string HoverColor = "#4A4880FF";
    public string PressedColor = "#2A2850FF";
    public string TextColor = "#FFFFFFFF";
    public List<EventAction> OnClick = [];

    // Estado de runtime (não serializado) — atualizado por UIManager.Update.
    internal bool Hovered;
    internal bool Pressed;
    internal int? OwnerTouchId;

    /// <summary>True só no frame do clique/toque — pra jogo reagir sem precisar do vocabulário
    /// genérico de EventAction (ex: chamar um método específico de um script). Lido em código:
    /// <c>if (UI.Find&lt;UiButton&gt;("hud", "BotaoAtk")?.Clicked == true) ...</c></summary>
    public bool Clicked;
}

/// <summary>Joystick virtual (toque multi-dedo no Android; clique-e-arraste no desktop) — base
/// fixa em (X,Y)/Anchor, direção lida em <see cref="Value"/> a cada frame. Convive com
/// UiButton/outro UiJoystick tocado por outro dedo ao mesmo tempo (UIManager.Update dá dono
/// por id de toque). Não dispara OnClick — é estado contínuo, não um clique único.</summary>
public sealed class UiJoystick : UiElement
{
    public float Radius = 70f;
    public string BaseColor = "#FFFFFF2E";
    public string KnobColor = "#FFFFFF66";

    /// <summary>Direção normalizada * intensidade (0..1) — leia a cada frame no script do
    /// player. Vetor zero quando ninguém está tocando o joystick.</summary>
    public Vector2 Value;

    // Estado de runtime (não serializado) — atualizado por UIManager.Update.
    internal int? OwnerTouchId;
    internal Vector2 KnobOffset;
}
