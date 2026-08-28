using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aurora.Runtime.Assets;
using Aurora.Runtime.Events;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Input;
using Silk.NET.Input;

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

    /// <summary>
    /// Catálogo de itens, pros tokens <c>{ItemName:id}</c>, <c>{ItemDesc:id}</c> e
    /// <c>{ItemPrice:id}</c> do UiText. Null = os tokens caem no próprio id.
    ///
    /// <para>Propriedade e não parâmetro de <c>Draw</c> de propósito: a assinatura do Draw está
    /// escrita no Game.cs de todo jogo já gerado por este editor, e mexer nela quebraria todos
    /// eles pra ganhar nada.</para>
    /// </summary>
    public Database.ItemDatabase? Items { get; set; }

    /// <summary>Textos de interface, pro token <c>{Term:chave}</c> do UiText. Null = o token
    /// devolve a própria chave.</summary>
    public Database.TermDatabase? Terms { get; set; }

    /// <summary>
    /// Estado do jogo, pro espelhamento de <see cref="UiTextInput.Variable"/>. Null = campo com
    /// Variable não sincroniza (segue funcionando pelo <see cref="UiTextInput.Text"/>).
    ///
    /// <para>Propriedade em vez de parâmetro do Update pelo mesmo motivo de <see cref="Items"/>:
    /// a assinatura do Update está escrita no Game.cs de todo jogo já gerado.</para>
    /// </summary>
    public GameState? State { get; set; }

    /// <summary>
    /// Registra uma tela montada em código, em vez de lida de arquivo. Substitui a de mesmo id,
    /// igual ao <see cref="Load"/>.
    ///
    /// <para>Existe porque <see cref="Load"/> exige um <see cref="AssetManager"/>, e esse exige
    /// contexto de OpenGL: sem este caminho não há como montar uma tela fora de uma janela — nem
    /// num teste, nem num HUD gerado em código.</para>
    /// </summary>
    public UiScreen Add(UiScreen screen)
    {
        _screens[screen.Id] = screen;
        return screen;
    }

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
            "UiText" => new UiText
            {
                Text = GetS("Text"),
                Color = GetS("Color", "#FFFFFFFF"),
                Scale = GetF("Scale", 1f),
                MaxWidth = GetF("MaxWidth", 0f),
            },
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
            "UiButton" => BuildButton(new UiButton
            {
                Text = GetS("Text"),
                Color = GetS("Color", "#3A3860FF"),
                HoverColor = GetS("HoverColor", "#4A4880FF"),
                PressedColor = GetS("PressedColor", "#2A2850FF"),
                TextColor = GetS("TextColor", "#FFFFFFFF"),
                TexturePath = GetS("Texture"),
                HoverTexturePath = GetS("HoverTexture"),
                PressedTexturePath = GetS("PressedTexture"),
                OnClick = json.TryGetProperty("OnClick", out var onClick) ? EventAction.ParseList(onClick) : [],
            }, GetF("Width"), GetF("Height"), assets),
            "UiTextInput" => new UiTextInput
            {
                Width = GetF("Width", 200f),
                Height = GetF("Height", 32f),
                Text = GetS("Text"),
                Variable = GetS("Variable"),
                Placeholder = GetS("Placeholder"),
                MaxLength = (int)GetF("MaxLength", 32f),
                Allowed = GetS("Allowed"),
                Color = GetS("Color", "#20203CFF"),
                FocusColor = GetS("FocusColor", "#2A2A55FF"),
                TextColor = GetS("TextColor", "#FFFFFFFF"),
                PlaceholderColor = GetS("PlaceholderColor", "#8888A8FF"),
                CaretColor = GetS("CaretColor", "#FFFFFFFF"),
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

    /// <summary>Resolve as imagens do botão e o tamanho: Width/Height ausentes (ou 0) herdam o
    /// tamanho da imagem — mesma regra do UiImage — e caem nos 120x32 de sempre quando o botão
    /// não tem imagem nenhuma.</summary>
    private static UiButton BuildButton(UiButton button, float width, float height, AssetManager assets)
    {
        button.Texture = LoadOptional(button.TexturePath, assets);
        button.HoverTexture = LoadOptional(button.HoverTexturePath, assets);
        button.PressedTexture = LoadOptional(button.PressedTexturePath, assets);

        button.Width = width > 0f ? width : button.Texture?.Width ?? 120f;
        button.Height = height > 0f ? height : button.Texture?.Height ?? 32f;
        return button;
    }

    private static Texture2D? LoadOptional(string? path, AssetManager assets)
        => string.IsNullOrEmpty(path) ? null : assets.LoadTexture(path);

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

    // A regra em si mora em UiAnchor: o editor compila aquele arquivo por link pra desenhar o
    // preview exatamente igual. Ver o comentário de lá antes de mexer.
    private static float ResolveAxis(string anchor, float coordinate, float screenSize, float elementSize)
        => UiAnchor.Resolve(anchor, coordinate, screenSize, elementSize);

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
        {
            foreach (var button in screen.Elements.OfType<UiButton>())
                button.Clicked = false;

            foreach (var field in screen.Elements.OfType<UiTextInput>())
                field.Submitted = false;
        }

        // Toques sem dono tentam reivindicar um UiButton ou UiJoystick livre (telas visíveis
        // por cima ganham prioridade — mesma ordem em que foram carregadas/mostradas).
        foreach (var (id, pos) in touches)
        {
            if (claimedIds.Contains(id))
                continue;

            // Todo toque sem dono redefine o foco: cair num campo foca ele, cair em qualquer
            // outro lugar (botao, cenario, tela vazia) tira o cursor de todos.
            UpdateFocus(pos, screenWidth, screenHeight);

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

        UpdateTyping(input);
        SyncTextVariables();
    }

    /// <summary>
    /// Espelha campo de texto e variável do GameState, nos dois sentidos.
    ///
    /// <para>Quem mudou desde o último frame ganha. O campo tem prioridade porque a comparação
    /// dele vem primeiro: se o jogador digitou E um evento escreveu na variável no mesmo frame,
    /// prevalece o que a pessoa está digitando — sumir letra debaixo do cursor é pior que perder
    /// uma escrita de evento.</para>
    ///
    /// <para>O sentido variável→campo é o que faz um save carregado reaparecer no campo, e o que
    /// preenche o valor inicial quando a tela abre.</para>
    /// </summary>
    private void SyncTextVariables()
    {
        if (State is not { } state)
            return;

        foreach (var screen in _screens.Values)
        {
            foreach (var field in screen.Elements.OfType<UiTextInput>())
            {
                if (field.Variable.Length == 0)
                    continue;

                // Primeiro encontro entre os dois: a variável ganha se já existir (save
                // carregado, valor posto por evento), senão o Text da cena vira o valor inicial.
                // Sem esta regra, um campo com texto padrão apagaria o nome do save toda vez que
                // a tela abrisse.
                if (!field.SyncStarted)
                {
                    field.SyncStarted = true;
                    if (state.HasText(field.Variable))
                        field.Text = state.GetText(field.Variable);
                    else
                        state.SetText(field.Variable, field.Text);

                    field.LastSynced = field.Text;
                    continue;
                }

                if (!string.Equals(field.Text, field.LastSynced, StringComparison.Ordinal))
                {
                    state.SetText(field.Variable, field.Text);
                    field.LastSynced = field.Text;
                    continue;
                }

                string stored = state.GetText(field.Variable);
                if (!string.Equals(stored, field.Text, StringComparison.Ordinal))
                {
                    field.Text = stored;
                    field.LastSynced = stored;
                }
            }
        }
    }

    /// <summary>Foca o campo sob o toque e desfoca o resto.</summary>
    private void UpdateFocus(Vector2 point, float screenWidth, float screenHeight)
    {
        UiTextInput? hit = null;

        foreach (var screen in _screens.Values)
        {
            if (!screen.Visible) continue;

            foreach (var field in screen.Elements.OfType<UiTextInput>())
            {
                var position = ResolvePosition(field, new Vector2(field.Width, field.Height), screenWidth, screenHeight);
                bool inside = point.X >= position.X && point.X <= position.X + field.Width
                           && point.Y >= position.Y && point.Y <= position.Y + field.Height;

                if (inside) hit = field;
            }
        }

        foreach (var screen in _screens.Values)
            foreach (var field in screen.Elements.OfType<UiTextInput>())
                field.Focused = ReferenceEquals(field, hit);
    }

    /// <summary>Entrega ao campo focado o que foi digitado no frame.</summary>
    private void UpdateTyping(InputManager input)
    {
        foreach (var screen in _screens.Values)
        {
            if (!screen.Visible) continue;

            foreach (var field in screen.Elements.OfType<UiTextInput>())
            {
                if (!field.Focused) continue;

                if (input.WasKeyPressed(Key.Backspace) && field.Text.Length > 0)
                    field.Text = field.Text[..^1];

                foreach (char c in input.TypedText)
                {
                    if (field.Text.Length >= field.MaxLength) break;
                    if (field.Allowed.Length > 0 && !field.Allowed.Contains(c)) continue;

                    field.Text += c;
                }

                if (input.WasKeyPressed(Key.Enter) || input.WasKeyPressed(Key.KeypadEnter))
                    field.Submitted = true;
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
                        string laidOut = WrapCached(text, resolved, font);
                        var size = font.MeasureText(laidOut, text.Scale);
                        var position = ResolvePosition(text, size, screenWidth, screenHeight);
                        font.Draw(batch, laidOut, position, Color.FromHex(text.Color), text.Scale);
                        break;
                    }

                    case UiButton button:
                    {
                        var size = new Vector2(button.Width, button.Height);
                        var position = ResolvePosition(button, size, screenWidth, screenHeight);

                        if (button.Texture is not null)
                        {
                            // Botão com imagem: a imagem substitui o retângulo colorido. Sem
                            // imagem própria pro estado, o feedback vem de tinta (clarear no
                            // hover, escurecer no clique) — um botão que não reage ao dedo passa
                            // a impressão de travado, e exigir três PNGs pra isso seria demais.
                            var texture = button.Pressed ? button.PressedTexture ?? button.Texture
                                : button.Hovered ? button.HoverTexture ?? button.Texture
                                : button.Texture;

                            var tint = button.Pressed && button.PressedTexture is null ? new Color(0.78f, 0.78f, 0.78f)
                                : button.Hovered && button.HoverTexture is null ? new Color(1.12f, 1.12f, 1.12f)
                                : Color.White;

                            batch.Draw(texture, position, size, Vector2.Zero, 0f, tint);
                        }
                        else
                        {
                            string bg = button.Pressed ? button.PressedColor
                                : button.Hovered ? button.HoverColor
                                : button.Color;
                            batch.DrawRect(position, size, Color.FromHex(bg));
                        }

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

                    case UiTextInput field:
                    {
                        var position = ResolvePosition(field, new Vector2(field.Width, field.Height), screenWidth, screenHeight);
                        batch.DrawRect(position, new Vector2(field.Width, field.Height),
                            Color.FromHex(field.Focused ? field.FocusColor : field.Color));

                        if (font is null) break;

                        const float padding = 6f;
                        bool empty = field.Text.Length == 0;
                        string shown = empty ? field.Placeholder : VisibleTail(field.Text, font, field.Width - padding * 2f);
                        var textSize = font.MeasureText(shown);
                        var textPos = position + new Vector2(padding, (field.Height - textSize.Y) / 2f);

                        if (shown.Length > 0)
                            font.Draw(batch, shown, textPos, Color.FromHex(empty ? field.PlaceholderColor : field.TextColor));

                        if (field.Focused)
                        {
                            // Cursor fixo, sem piscar: o Update da UI nao recebe deltaTime, e
                            // pedir um so pra animar cursor mudaria a assinatura pra todo mundo.
                            var caretPos = position + new Vector2(padding + (empty ? 0f : textSize.X) + 1f, 4f);
                            batch.DrawRect(caretPos, new Vector2(2f, field.Height - 8f), Color.FromHex(field.CaretColor));
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

    /// <summary>Fim do texto que cabe na largura do campo. Campo de texto mostra o fim, nao o
    /// comeco: o jogador precisa ver o que esta digitando agora.</summary>
    private static string VisibleTail(string text, Font font, float maxWidth)
    {
        if (maxWidth <= 0f) return text;

        int start = 0;
        while (start < text.Length && font.MeasureText(text[start..]).X > maxWidth)
            start++;

        return text[start..];
    }

    /// <summary>Texto já quebrado pra caber em <c>MaxWidth</c>, reaproveitando o resultado
    /// anterior. Sem o memo, cada UiText com quebra ligada refaria o layout todo frame.</summary>
    private static string WrapCached(UiText text, string resolved, Font font)
    {
        if (text.MaxWidth <= 0f)
            return resolved;

        if (text.WrapSource == resolved && text.WrapWidth == text.MaxWidth
            && text.WrapScale == text.Scale && ReferenceEquals(text.WrapFont, font))
        {
            return text.WrapResult;
        }

        text.WrapSource = resolved;
        text.WrapWidth = text.MaxWidth;
        text.WrapScale = text.Scale;
        text.WrapFont = font;
        text.WrapResult = font.WrapText(resolved, text.MaxWidth, text.Scale);
        return text.WrapResult;
    }

    /// <summary>
    /// Troca <c>{token}</c> pelo valor no texto de um UiText. Tokens:
    /// <list type="bullet">
    ///   <item><c>{Item:id}</c> — quantidade no inventário;</item>
    ///   <item><c>{ItemName:id}</c>, <c>{ItemDesc:id}</c>, <c>{ItemPrice:id}</c> — campos da ficha
    ///   do item no banco (os três existiam no cadastro sem nada no jogo que os lesse);</item>
    ///   <item><c>{Term:chave}</c> — texto de interface do banco de termos;</item>
    ///   <item><c>{Quest:id}</c> — estágio da quest;</item>
    ///   <item>qualquer outra coisa — variável do GameState.</item>
    /// </list>
    /// </summary>
    internal string Interpolate(string template, GameState state, InventoryManager? inventory, QuestManager? quests)
        => TokenPattern.Replace(template, match =>
        {
            string token = match.Groups[1].Value;
            if (token.StartsWith("Term:", StringComparison.OrdinalIgnoreCase))
                return Terms?.Get(token[5..]) ?? token[5..];
            if (token.StartsWith("ItemName:", StringComparison.OrdinalIgnoreCase))
                return Items?.DisplayName(token[9..]) ?? token[9..];
            if (token.StartsWith("ItemDesc:", StringComparison.OrdinalIgnoreCase))
                return Items?.Get(token[9..])?.Description ?? "";
            if (token.StartsWith("ItemPrice:", StringComparison.OrdinalIgnoreCase))
                return (Items?.Get(token[10..])?.Price ?? 0).ToString();
            if (token.StartsWith("Item:", StringComparison.OrdinalIgnoreCase))
                return (inventory?.GetCount(token[5..]) ?? 0).ToString();
            if (token.StartsWith("Quest:", StringComparison.OrdinalIgnoreCase))
                return (quests?.GetStage(token[6..]) ?? 0).ToString();

            // Texto antes de número: um nome de jogador tem que sair como nome. Sem isto o
            // token cairia no GetVariable e desenharia "0" — o mesmo símbolo de "variável que
            // não existe", então nem dava pra perceber que o nome tinha se perdido.
            if (state.HasText(token))
                return state.GetText(token);

            return state.GetVariable(token).ToString(System.Globalization.CultureInfo.InvariantCulture);
        });
}
