using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aurora.Runtime.Assets;
using Aurora.Runtime.Events;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Input;

namespace Aurora.Runtime.UI;

/// <summary>
/// Telas de HUD/menu: arquivos .json no mesmo formato de cena (<c>{"Scene":..,"Objects":[...]}</c>)
/// mas com componentes UiText/UiImage/UiBar/UiPanel/UiButton em coordenadas de pixel de tela —
/// não seguem a câmera, persistem entre trocas de cena (LoadScene não mexe aqui). UiButton reage
/// a clique/toque via <see cref="Update"/> (chamado pelo Game a cada frame). Editável no mesmo
/// Aurora Editor: hierarquia/inspector genéricos já funcionam pra qualquer componente desconhecido
/// (UiButton usa UiButtonViewModel pra editar a lista OnClick).
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

        using var doc = JsonDocument.Parse(assets.LoadText(path));
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
            "UiButton" => new UiButton
            {
                Width = GetF("Width", 120f),
                Height = GetF("Height", 32f),
                Text = GetS("Text"),
                Color = GetS("Color", "#3A3860FF"),
                HoverColor = GetS("HoverColor", "#4A4880FF"),
                PressedColor = GetS("PressedColor", "#2A2850FF"),
                TextColor = GetS("TextColor", "#FFFFFFFF"),
                OnClick = json.TryGetProperty("OnClick", out var onClick) ? EventAction.ParseList(onClick) : [],
            },
            "UiJoystick" => new UiJoystick
            {
                Radius = GetF("Radius", 70f),
                BaseColor = GetS("BaseColor", "#FFFFFF2E"),
                KnobColor = GetS("KnobColor", "#FFFFFF66"),
            },
            _ => null,
        };

        if (element is null)
            return null;

        element.Name = name;
        element.X = GetF("X");
        element.Y = GetF("Y");
        element.AnchorX = GetS("AnchorX", "Left");
        element.AnchorY = GetS("AnchorY", "Top");
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

    /// <summary>Acha um elemento pelo nome numa tela carregada — pra ler UiJoystick.Value ou
    /// UiButton.Clicked em código, sem precisar de EventAction pra lógica específica do jogo.</summary>
    public T? Find<T>(string screenId, string elementName) where T : UiElement
        => _screens.TryGetValue(screenId, out var screen)
            ? screen.Elements.OfType<T>().FirstOrDefault(e => e.Name == elementName)
            : null;

    /// <summary>Resolve X/Y+Anchor pra posição de pixel de tela de verdade. Center/Right/Bottom
    /// tornam a coordenada independente de resolução (ver UiElement.AnchorX/AnchorY) — sem
    /// isso, coordenada fixa só bate numa tela do tamanho exato usado ao autorar.</summary>
    private static Vector2 ResolvePosition(UiElement element, Vector2 size, float screenWidth, float screenHeight)
        => new(
            ResolveAxis(element.AnchorX, element.X, screenWidth, size.X),
            ResolveAxis(element.AnchorY, element.Y, screenHeight, size.Y));

    private static float ResolveAxis(string anchor, float coordinate, float screenSize, float elementSize)
        => anchor switch
        {
            "Center" => screenSize / 2f + coordinate - elementSize / 2f,
            "Right" or "Bottom" => screenSize - coordinate - elementSize,
            _ => coordinate, // "Left"/"Top"
        };

    private static Vector2 JoystickCenter(UiJoystick stick, float screenWidth, float screenHeight)
    {
        var size = new Vector2(stick.Radius * 2f, stick.Radius * 2f);
        return ResolvePosition(stick, size, screenWidth, screenHeight) + new Vector2(stick.Radius, stick.Radius);
    }

    private static Vector2? FindTouch(IReadOnlyList<(int Id, Vector2 Position)> touches, int id)
    {
        foreach (var (touchId, position) in touches)
            if (touchId == id)
                return position;
        return null;
    }

    /// <summary>Atualiza hover/clique/arrasto dos UiButton e UiJoystick das telas visíveis —
    /// chamado automaticamente pelo Game a cada frame, antes do passe de render. Multi-toque de
    /// verdade (InputManager.ActiveTouches): cada toque "pertence" a um elemento só, do frame em
    /// que nasce até soltar — dá pra segurar um UiJoystick com um dedo e apertar um UiButton com
    /// outro ao mesmo tempo. screenWidth/Height iguais aos passados pro Draw — senão o hit-test
    /// erra a posição mostrada na tela pra AnchorX/Y diferente de Left/Top.</summary>
    public void Update(InputManager input, EventSystem? events, float screenWidth, float screenHeight)
    {
        // Hover é feedback de mouse parado sem clicar (desktop) — separado do sistema de posse
        // por toque abaixo, que só existe enquanto há contato de verdade (mouse pressionado ou
        // dedo na tela).
        var mousePos = input.MousePosition;
        foreach (var screen in _screens.Values)
        {
            if (!screen.Visible)
                continue;
            foreach (var button in screen.Elements.OfType<UiButton>())
            {
                var position = ResolvePosition(button, new Vector2(button.Width, button.Height), screenWidth, screenHeight);
                button.Hovered = mousePos.X >= position.X && mousePos.X <= position.X + button.Width
                              && mousePos.Y >= position.Y && mousePos.Y <= position.Y + button.Height;
            }
        }

        var touches = input.ActiveTouches;
        var activeIds = new HashSet<int>();
        foreach (var (id, _) in touches)
            activeIds.Add(id);

        // Solta quem perdeu o toque (dedo levantado/mouse solto).
        foreach (var screen in _screens.Values)
        {
            foreach (var element in screen.Elements)
            {
                if (element is UiButton { OwnerTouchId: { } bid } button && !activeIds.Contains(bid))
                {
                    button.OwnerTouchId = null;
                    button.Pressed = false;
                }
                else if (element is UiJoystick { OwnerTouchId: { } sid } stick && !activeIds.Contains(sid))
                {
                    stick.OwnerTouchId = null;
                    stick.Value = Vector2.Zero;
                    stick.KnobOffset = Vector2.Zero;
                }
            }
        }

        // Toques que já são donos de um UiJoystick continuam arrastando ele.
        var claimedIds = new HashSet<int>();
        foreach (var screen in _screens.Values)
        {
            foreach (var stick in screen.Elements.OfType<UiJoystick>())
            {
                if (stick.OwnerTouchId is not { } id)
                    continue;
                claimedIds.Add(id);
                if (FindTouch(touches, id) is not { } pos)
                    continue;

                var center = JoystickCenter(stick, screenWidth, screenHeight);
                var delta = pos - center;
                float dist = delta.Length();
                float clamped = MathF.Min(dist, stick.Radius);
                stick.KnobOffset = dist > 0.001f ? delta / dist * clamped : Vector2.Zero;
                stick.Value = dist > 0.001f ? delta / dist * (clamped / stick.Radius) : Vector2.Zero;
            }

            foreach (var button in screen.Elements.OfType<UiButton>())
            {
                if (button.OwnerTouchId is { } id)
                    claimedIds.Add(id);
            }
        }

        // Reseta o "clique de um frame só" antes de reivindicar toque novo (senão Clicked
        // nunca voltaria a false depois do primeiro toque).
        foreach (var screen in _screens.Values)
            foreach (var button in screen.Elements.OfType<UiButton>())
                button.Clicked = false;

        // Toques sem dono tentam reivindicar um UiButton ou UiJoystick livre (telas visíveis
        // por cima ganham prioridade — mesma ordem em que foram carregadas/mostradas).
        foreach (var (id, pos) in touches)
        {
            if (claimedIds.Contains(id))
                continue;

            bool claimed = false;
            foreach (var screen in _screens.Values)
            {
                if (!screen.Visible || claimed)
                    continue;

                foreach (var element in screen.Elements)
                {
                    if (element is UiButton { OwnerTouchId: null } button)
                    {
                        var position = ResolvePosition(button, new Vector2(button.Width, button.Height), screenWidth, screenHeight);
                        bool inside = pos.X >= position.X && pos.X <= position.X + button.Width
                                   && pos.Y >= position.Y && pos.Y <= position.Y + button.Height;
                        if (!inside)
                            continue;

                        button.OwnerTouchId = id;
                        button.Pressed = true;
                        button.Clicked = true;
                        events?.RunActions(button.OnClick);
                        claimed = true;
                        break;
                    }

                    if (element is UiJoystick { OwnerTouchId: null } stick)
                    {
                        var center = JoystickCenter(stick, screenWidth, screenHeight);
                        if (Vector2.Distance(pos, center) > stick.Radius * 1.6f)
                            continue;

                        stick.OwnerTouchId = id;
                        var delta = pos - center;
                        float dist = delta.Length();
                        float clamped = MathF.Min(dist, stick.Radius);
                        stick.KnobOffset = dist > 0.001f ? delta / dist * clamped : Vector2.Zero;
                        stick.Value = dist > 0.001f ? delta / dist * (clamped / stick.Radius) : Vector2.Zero;
                        claimed = true;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>Desenha todas as telas visíveis (chame no passe de UI, igual Dialogue.Draw).
    /// screenWidth/Height resolvem AnchorX/Y != Left/Top (ver ResolvePosition).</summary>
    public void Draw(SpriteBatch batch, Font? font, GameState state, InventoryManager? inventory, QuestManager? quests,
        float screenWidth, float screenHeight)
    {
        foreach (var screen in _screens.Values)
        {
            if (!screen.Visible)
                continue;

            foreach (var element in screen.Elements)
            {
                switch (element)
                {
                    case UiPanel panel:
                    {
                        var position = ResolvePosition(panel, new Vector2(panel.Width, panel.Height), screenWidth, screenHeight);
                        batch.DrawRect(position, new Vector2(panel.Width, panel.Height), Color.FromHex(panel.Color));
                        break;
                    }

                    case UiImage { Texture: { } texture } image:
                    {
                        var position = ResolvePosition(image, new Vector2(image.Width, image.Height), screenWidth, screenHeight);
                        batch.Draw(texture, position, new Vector2(image.Width, image.Height),
                            Vector2.Zero, 0f, Color.FromHex(image.Color));
                        break;
                    }

                    case UiBar bar:
                    {
                        var position = ResolvePosition(bar, new Vector2(bar.Width, bar.Height), screenWidth, screenHeight);
                        float value = state.GetVariable(bar.Variable);
                        float ratio = bar.Max > 0f ? Math.Clamp(value / bar.Max, 0f, 1f) : 0f;
                        batch.DrawRect(position, new Vector2(bar.Width, bar.Height), Color.FromHex(bar.BackColor));
                        if (ratio > 0f)
                            batch.DrawRect(position, new Vector2(bar.Width * ratio, bar.Height), Color.FromHex(bar.FillColor));
                        break;
                    }

                    case UiText text when font is not null:
                    {
                        string resolved = Interpolate(text.Text, state, inventory, quests);
                        var size = font.MeasureText(resolved, text.Scale);
                        var position = ResolvePosition(text, size, screenWidth, screenHeight);
                        font.Draw(batch, resolved, position, Color.FromHex(text.Color), text.Scale);
                        break;
                    }

                    case UiButton button:
                    {
                        var position = ResolvePosition(button, new Vector2(button.Width, button.Height), screenWidth, screenHeight);
                        string bg = button.Pressed ? button.PressedColor
                            : button.Hovered ? button.HoverColor
                            : button.Color;
                        batch.DrawRect(position, new Vector2(button.Width, button.Height), Color.FromHex(bg));

                        if (font is not null && button.Text.Length > 0)
                        {
                            var textSize = font.MeasureText(button.Text);
                            var textPos = position + new Vector2(
                                (button.Width - textSize.X) / 2f,
                                (button.Height - textSize.Y) / 2f);
                            font.Draw(batch, button.Text, textPos, Color.FromHex(button.TextColor));
                        }
                        break;
                    }

                    case UiJoystick stick:
                    {
                        var center = JoystickCenter(stick, screenWidth, screenHeight);
                        batch.DrawGlow(center, stick.Radius, Color.FromHex(stick.BaseColor));
                        batch.DrawGlow(center + stick.KnobOffset, stick.Radius * 0.45f, Color.FromHex(stick.KnobColor));
                        break;
                    }
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
