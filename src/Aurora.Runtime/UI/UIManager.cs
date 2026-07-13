using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aurora.Runtime.Assets;
using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.UI;

/// <summary>
/// Telas de HUD/menu: arquivos .json no mesmo formato de cena (<c>{"Scene":..,"Objects":[...]}</c>)
/// mas com componentes UiText/UiImage/UiBar/UiPanel em coordenadas de pixel de tela — não seguem
/// a câmera, persistem entre trocas de cena (LoadScene não mexe aqui). Editável no mesmo Aurora
/// Editor: hierarquia/inspector genéricos já funcionam pra qualquer componente desconhecido.
/// </summary>
public sealed class UIManager
{
    private readonly Dictionary<string, UiScreen> _screens = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex TokenPattern = new(@"\{([^}]+)\}", RegexOptions.Compiled);

    /// <summary>Carrega (ou recarrega) uma tela a partir do arquivo. Fica visível por padrão.</summary>
    public UiScreen Load(string path, AssetManager assets)
    {
        string id = Path.GetFileNameWithoutExtension(path);
        var screen = new UiScreen(id);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.TryGetProperty("Objects", out var objects))
        {
            foreach (var obj in objects.EnumerateArray())
            {
                string name = obj.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                if (!obj.TryGetProperty("Components", out var components))
                    continue;

                foreach (var comp in components.EnumerateArray())
                {
                    var element = ParseElement(comp, name, assets);
                    if (element is not null)
                        screen.Elements.Add(element);
                }
            }
        }

        screen.Visible = true;
        _screens[id] = screen;
        return screen;
    }

    private static UiElement? ParseElement(JsonElement json, string name, AssetManager assets)
    {
        string type = json.TryGetProperty("Type", out var t) ? t.GetString() ?? "" : "";

        float GetF(string field, float fallback = 0f) => json.TryGetProperty(field, out var p) ? p.GetSingle() : fallback;
        string GetS(string field, string fallback = "") => json.TryGetProperty(field, out var p) ? p.GetString() ?? fallback : fallback;

        UiElement? element = type switch
        {
            "UiText" => new UiText { Text = GetS("Text"), Color = GetS("Color", "#FFFFFFFF"), Scale = GetF("Scale", 1f) },
            "UiImage" => BuildImage(GetS("Texture"), GetF("Width"), GetF("Height"), GetS("Color", "#FFFFFFFF"), assets),
            "UiBar" => new UiBar
            {
                Width = GetF("Width", 100f),
                Height = GetF("Height", 12f),
                Variable = GetS("Variable"),
                Max = GetF("Max", 100f),
                FillColor = GetS("FillColor", "#40C040FF"),
                BackColor = GetS("BackColor", "#303030FF"),
            },
            "UiPanel" => new UiPanel
            {
                Width = GetF("Width", 100f),
                Height = GetF("Height", 100f),
                Color = GetS("Color", "#000000AA"),
            },
            _ => null,
        };

        if (element is null)
            return null;

        element.Name = name;
        element.X = GetF("X");
        element.Y = GetF("Y");
        return element;
    }

    private static UiImage BuildImage(string texturePath, float width, float height, string color, AssetManager assets)
    {
        var image = new UiImage { TexturePath = texturePath, Width = width, Height = height, Color = color };
        if (!string.IsNullOrEmpty(texturePath))
        {
            image.Texture = assets.LoadTexture(texturePath);
            if (width <= 0f) image.Width = image.Texture.Width;
            if (height <= 0f) image.Height = image.Texture.Height;
        }
        return image;
    }

    public bool Show(string id)
    {
        if (!_screens.TryGetValue(id, out var screen)) return false;
        screen.Visible = true;
        return true;
    }

    public bool Hide(string id)
    {
        if (!_screens.TryGetValue(id, out var screen)) return false;
        screen.Visible = false;
        return true;
    }

    public bool Toggle(string id)
    {
        if (!_screens.TryGetValue(id, out var screen)) return false;
        screen.Visible = !screen.Visible;
        return screen.Visible;
    }

    public bool IsVisible(string id) => _screens.TryGetValue(id, out var screen) && screen.Visible;

    /// <summary>Desenha todas as telas visíveis (chame no passe de UI, igual Dialogue.Draw).</summary>
    public void Draw(SpriteBatch batch, Font? font, GameState state, InventoryManager? inventory, QuestManager? quests)
    {
        foreach (var screen in _screens.Values)
        {
            if (!screen.Visible)
                continue;

            foreach (var element in screen.Elements)
            {
                var position = new Vector2(element.X, element.Y);
                switch (element)
                {
                    case UiPanel panel:
                        batch.DrawRect(position, new Vector2(panel.Width, panel.Height), Color.FromHex(panel.Color));
                        break;

                    case UiImage { Texture: { } texture } image:
                        batch.Draw(texture, position, new Vector2(image.Width, image.Height),
                            Vector2.Zero, 0f, Color.FromHex(image.Color));
                        break;

                    case UiBar bar:
                    {
                        float value = state.GetVariable(bar.Variable);
                        float ratio = bar.Max > 0f ? Math.Clamp(value / bar.Max, 0f, 1f) : 0f;
                        batch.DrawRect(position, new Vector2(bar.Width, bar.Height), Color.FromHex(bar.BackColor));
                        if (ratio > 0f)
                            batch.DrawRect(position, new Vector2(bar.Width * ratio, bar.Height), Color.FromHex(bar.FillColor));
                        break;
                    }

                    case UiText text when font is not null:
                        font.Draw(batch, Interpolate(text.Text, state, inventory, quests), position,
                            Color.FromHex(text.Color), text.Scale);
                        break;
                }
            }
        }
    }

    private static string Interpolate(string template, GameState state, InventoryManager? inventory, QuestManager? quests)
        => TokenPattern.Replace(template, match =>
        {
            string token = match.Groups[1].Value;
            if (token.StartsWith("Item:", StringComparison.OrdinalIgnoreCase))
                return (inventory?.GetCount(token[5..]) ?? 0).ToString();
            if (token.StartsWith("Quest:", StringComparison.OrdinalIgnoreCase))
                return (quests?.GetStage(token[6..]) ?? 0).ToString();
            return state.GetVariable(token).ToString(System.Globalization.CultureInfo.InvariantCulture);
        });
}
