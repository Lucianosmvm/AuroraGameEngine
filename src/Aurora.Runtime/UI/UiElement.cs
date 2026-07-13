using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.UI;

/// <summary>Elemento de tela (HUD/menu) em coordenadas de pixel de tela — não segue a câmera.</summary>
public abstract class UiElement
{
    public string Name = "";
    public float X;
    public float Y;
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
