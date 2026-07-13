namespace Aurora.Runtime.UI;

/// <summary>Uma tela de UI carregada (HUD, menu, inventário) — lista de elementos + visibilidade.</summary>
public sealed class UiScreen(string id)
{
    public string Id { get; } = id;
    public List<UiElement> Elements { get; } = [];
    public bool Visible { get; set; }
}
