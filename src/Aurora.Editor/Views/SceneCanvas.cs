using Aurora.Editor.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Aurora.Editor.Models;

namespace Aurora.Editor.Views;

/// <summary>
/// Viewport 2D da cena: sprites (com rotação/escala), pan (botão do meio),
/// zoom (scroll), seleção, arrasto para mover e gizmos de escala/rotação.
/// Mesma convenção do runtime: Y cresce para baixo, câmera centrada.
/// </summary>
public sealed class SceneCanvas : Control
{
    private const double HandleSize = 8;
    private const double RotationHandleOffset = 26;

    private enum DragMode { None, Pan, Move, Scale, Rotate, Paint, MoveUi }

    /// <summary>Sprite pronto para desenhar/testar: matriz local→tela e retângulo local.</summary>
    private readonly record struct SpriteView(EntityViewModel Entity, Matrix LocalToScreen, Rect LocalRect, float Layer);

    /// <summary>Elemento de UI (HUD/menu) pronto para desenhar: coordenadas de pixel de tela
    /// diretas, sem passar pela câmera do mundo — mesma convenção do UIManager em runtime.
    /// <paramref name="Text"/> é o texto já com as quebras de linha resolvidas (só UiText usa).</summary>
    private readonly record struct UiElementView(EntityViewModel Entity, ComponentViewModel Component,
        string Kind, Rect Rect, string Text);

    /// <summary>Tilemap pronto para desenhar: célula em unidades do mundo, grade e tileset.</summary>
    private readonly record struct TilemapView(EntityViewModel Entity, Matrix LocalToScreen,
        int Columns, int Rows, double CellWidth, double CellHeight,
        int TileWidth, int TileHeight, Bitmap? Tileset, float Layer)
    {
        public Rect LocalRect => new(0, 0, Columns * CellWidth, Rows * CellHeight);
    }

    private readonly Dictionary<string, Bitmap?> _textures = new(StringComparer.OrdinalIgnoreCase);

    private Point _cameraPosition;
    private double _zoom = 0.5;

    private DragMode _drag = DragMode.None;
    private Point _lastPointer;
    private EntityViewModel? _target;
    private Point _dragOffset;

    // Alvo do drag de elemento de UI (MoveUi) — X/Y em pixel de tela do jogo, sem ScreenToWorld.
    private ComponentViewModel? _uiTarget;

    // Tamanho do elemento arrastado em pixel de tela do jogo: X/Y ancorado em Center/Right/Bottom
    // é relativo à borda da tela e ao próprio tamanho, então converter canto → X/Y precisa dele.
    private Size _uiTargetSize;

    // Estado inicial do gesto de escala/rotação.
    private Point _gestureLocalStart;
    private float _startScaleX, _startScaleY, _startRotation;
    private double _startAngle;

    private MainViewModel? _viewModel;

    /// <summary>
    /// Zoom e deslocamento da MOLDURA da UI, separados do zoom do mundo.
    ///
    /// <para>São dois espaços diferentes: o mundo tem câmera, a UI é pixel de tela. Numa tela de
    /// referência grande (1920x1080) a moldura inteira encolhia pra caber no painel e não havia
    /// como aproximar — dava pra montar o menu, mas não pra enxergar o que se estava montando.
    /// Com zoom próprio, rolar o scroll aproxima a folha da UI sem mexer na câmera do mundo (e
    /// vice-versa), que é o que mantém o HUD parado enquanto se navega a cena.</para>
    /// </summary>
    private double _uiZoom = 1.0;
    private Point _uiPan;

    /// <summary>Ponto do mundo no centro do viewport — onde entidades novas nascem.</summary>
    public Point CameraCenter => _cameraPosition;

    /// <summary>Volta câmera/zoom pro estado inicial — scroll de zoom não tem limite visual
    /// (só o clamp 0.05x-20x), então rolar demais deixa a cena minúscula/fora de vista sem
    /// nenhum jeito óbvio de voltar. Chamado pela tecla Home (ver MainWindow.axaml.cs).</summary>
    public void ResetView()
    {
        _zoom = 0.5;
        _cameraPosition = default;
        _uiZoom = 1.0;
        _uiPan = default;
        InvalidateVisual();
    }

    public SceneCanvas()
    {
        ClipToBounds = true;
        Focusable = true;

        // API de DnD clássica: obsoleta no 11.3 mas funcional em todo o 11.x.
        // Migrar para DataTransfer/DoDragDropAsync junto com o upgrade para Avalonia 12.
#pragma warning disable CS0618
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.Data.Contains(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None;
        });
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (_viewModel is null || e.Data.GetText() is not { } texturePath)
                return;

            var world = ScreenToWorld(e.GetPosition(this));
            _viewModel.CreateEntity(world.X, world.Y, texturePath);
            e.Handled = true;
        });
#pragma warning restore CS0618
    }

    /// <summary>Esquece bitmaps carregados — usado ao reescanear a pasta de assets.</summary>
    public void ClearTextureCache()
    {
        _textures.Clear();
        InvalidateVisual();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
            _viewModel.SceneEdited -= InvalidateVisual;

        _viewModel = DataContext as MainViewModel;

        if (_viewModel is not null)
        {
            _viewModel.SceneEdited += InvalidateVisual;
            _viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(MainViewModel.SelectedEntity)
                                      or nameof(MainViewModel.ShowColliders))
                    InvalidateVisual();
            };
        }
    }

    // ---- Transformações ----

    private Matrix ViewMatrix => Matrix.CreateTranslation(-_cameraPosition.X, -_cameraPosition.Y)
                               * Matrix.CreateScale(_zoom, _zoom)
                               * Matrix.CreateTranslation(Bounds.Width / 2, Bounds.Height / 2);

    private Point ScreenToWorld(Point screen) => new(
        (screen.X - Bounds.Width / 2) / _zoom + _cameraPosition.X,
        (screen.Y - Bounds.Height / 2) / _zoom + _cameraPosition.Y);

    private Point WorldToScreen(Point world) => new(
        (world.X - _cameraPosition.X) * _zoom + Bounds.Width / 2,
        (world.Y - _cameraPosition.Y) * _zoom + Bounds.Height / 2);

    /// <summary>Tilemaps com Transform, na ordem da hierarquia. Rotação de tilemap é ignorada.</summary>
    private IEnumerable<TilemapView> VisibleTilemaps()
    {
        if (_viewModel is null)
            yield break;

        var view = ViewMatrix;

        foreach (var entity in _viewModel.Entities)
        {
            var transform = entity.Transform;
            var map = entity.Tilemap;
            if (transform is null || map is null)
                continue;

            int columns = (int)map.GetFloat("Width", 0f);
            int rows = (int)map.GetFloat("Height", 0f);
            int tileWidth = (int)map.GetFloat("TileWidth", 16f);
            int tileHeight = (int)map.GetFloat("TileHeight", 16f);
            if (columns <= 0 || rows <= 0 || tileWidth <= 0 || tileHeight <= 0)
                continue;

            var localToScreen = Matrix.CreateTranslation(
                transform.GetFloat("X", 0f), transform.GetFloat("Y", 0f)) * view;

            yield return new TilemapView(entity, localToScreen, columns, rows,
                tileWidth * Math.Abs(transform.GetFloat("ScaleX", 1f)),
                tileHeight * Math.Abs(transform.GetFloat("ScaleY", 1f)),
                tileWidth, tileHeight,
                ResolveTexture(map.GetString("Texture")),
                map.GetFloat("Layer", 0f));
        }
    }

    /// <summary>Sprites visíveis com matriz e retângulo local (ordem da hierarquia).</summary>
    private IEnumerable<SpriteView> VisibleSprites()
    {
        if (_viewModel is null)
            yield break;

        var view = ViewMatrix;

        foreach (var entity in _viewModel.Entities)
        {
            var transform = entity.Transform;
            var sprite = entity.Sprite;
            if (transform is null || sprite is null || !sprite.GetBool("Visible", true))
                continue;

            var bitmap = ResolveTexture(sprite.GetString("Texture"));

            // Espelha World.Render: SizeX/SizeY substituem o tamanho natural da textura (os dois
            // precisam ser positivos — zero quer dizer "natural", igual no SceneSerializer), e o
            // ScaleX/ScaleY do Transform multiplica os dois casos. Sem isso o canvas mostraria o
            // PNG no tamanho do arquivo enquanto o jogo desenha no tamanho pedido.
            float sizeX = sprite.GetFloat("SizeX", 0f);
            float sizeY = sprite.GetFloat("SizeY", 0f);
            bool hasSize = sizeX > 0f && sizeY > 0f;

            double baseWidth = hasSize ? sizeX : bitmap?.Size.Width ?? 32;
            double baseHeight = hasSize ? sizeY : bitmap?.Size.Height ?? 32;
            double width = baseWidth * Math.Abs(transform.GetFloat("ScaleX", 1f));
            double height = baseHeight * Math.Abs(transform.GetFloat("ScaleY", 1f));
            if (width < 0.01 || height < 0.01)
                continue;

            double originX = sprite.GetFloat("OriginX", 0.5f);
            double originY = sprite.GetFloat("OriginY", 0.5f);

            var localRect = new Rect(-width * originX, -height * originY, width, height);

            var localToScreen = Matrix.CreateRotation(transform.GetFloat("Rotation", 0f))
                              * Matrix.CreateTranslation(transform.GetFloat("X", 0f), transform.GetFloat("Y", 0f))
                              * view;

            yield return new SpriteView(entity, localToScreen, localRect, sprite.GetFloat("Layer", 0f));
        }
    }

    /// <summary>Colisor pronto pra desenhar: retângulo já em pixel do canvas (Circle usa o
    /// retângulo como bounding box do círculo) e as flags que definem a cor.</summary>
    private readonly record struct ColliderView(EntityViewModel Entity, bool IsCircle, Rect Rect,
        bool IsSolid, bool IsKinematic, string SizeLabel);

    /// <summary>Colisores de todas as entidades com Transform + Collider.
    ///
    /// Segue a convenção do runtime (<c>World.ProcessCollisions</c>): a forma fica centrada em
    /// <c>Transform.Position + Collider.Offset</c>, com Width/Height (ou Radius) em pixels de
    /// mundo — <b>sem</b> passar por ScaleX/ScaleY nem por Rotation do Transform. Ou seja: mudar a
    /// escala do sprite não muda a hitbox, e a hitbox nunca gira. É de propósito no runtime (AABB),
    /// e é justamente por isso que ver o colisor desenhado importa: ele não acompanha o sprite.</summary>
    private IEnumerable<ColliderView> VisibleColliders()
    {
        if (_viewModel is null)
            yield break;

        foreach (var entity in _viewModel.Entities)
        {
            var transform = entity.Transform;
            var collider = entity.Collider;
            if (transform is null || collider is null)
                continue;

            double centerX = transform.GetFloat("X", 0f) + collider.GetFloat("OffsetX", 0f);
            double centerY = transform.GetFloat("Y", 0f) + collider.GetFloat("OffsetY", 0f);

            bool isCircle = collider.GetString("Shape") == "Circle";
            double width, height;
            string label;
            if (isCircle)
            {
                double radius = collider.GetFloat("Radius", 8f);
                width = height = radius * 2;
                label = $"r {radius:0.##}";
            }
            else
            {
                width = collider.GetFloat("Width", 16f);
                height = collider.GetFloat("Height", 16f);
                label = $"{width:0.##} × {height:0.##}";
            }

            if (width < 0.01 || height < 0.01)
                continue;

            var topLeft = new Point(centerX - width / 2, centerY - height / 2).Transform(ViewMatrix);
            yield return new ColliderView(entity, isCircle,
                new Rect(topLeft.X, topLeft.Y, width * _zoom, height * _zoom),
                collider.GetBool("IsSolid", true), collider.GetBool("IsKinematic", false), label);
        }
    }

    /// <summary>Células sólidas do tilemap (índices de <c>SolidTiles</c>) pintadas por cima dos
    /// tiles. Elas colidem no runtime (<c>World.ResolveTilemap</c>) sem existir nenhum componente
    /// Collider na cena — olhando só o tileset desenhado não dá pra distinguir a parede do chão
    /// decorativo, e é aí que o personagem trava num lugar que parecia vazio.</summary>
    private void DrawSolidTiles(DrawingContext context, TilemapView map)
    {
        var tilemap = map.Entity.Tilemap;
        if (tilemap is null)
            return;

        // "1, 3, 5" no inspector → conjunto de índices. Entrada meio digitada é ignorada em vez
        // de derrubar o render.
        var solid = new HashSet<int>();
        foreach (var part in (tilemap.GetString("SolidTiles") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(part.Trim(), out int parsed))
                solid.Add(parsed);

        if (solid.Count == 0 || map.Entity.Tilemap?.Node["Tiles"] is not System.Text.Json.Nodes.JsonArray tiles)
            return;

        using var _ = context.PushTransform(map.LocalToScreen);

        var color = Color.FromRgb(80, 226, 130);
        var fill = new SolidColorBrush(color, 0.22);
        var pen = new Pen(new SolidColorBrush(color, 0.7), 1 / _zoom);
        int total = Math.Min(tiles.Count, map.Columns * map.Rows);

        for (int cell = 0; cell < total; cell++)
        {
            if (!solid.Contains(tiles[cell].AsInt(-1)))
                continue;

            context.DrawRectangle(fill, pen, new Rect(
                cell % map.Columns * map.CellWidth, cell / map.Columns * map.CellHeight,
                map.CellWidth, map.CellHeight));
        }
    }

    /// <summary>Contorno dos colisores por cima dos sprites. Cor conta o tipo: verde = sólido
    /// (empurra), laranja = trigger (IsSolid=false, só dispara callback). Tracejado = cinemático
    /// (não é empurrado por ninguém). O selecionado ganha traço mais forte e o tamanho escrito.</summary>
    private void DrawColliders(DrawingContext context)
    {
        foreach (var view in VisibleColliders())
        {
            bool selected = ReferenceEquals(view.Entity, _viewModel?.SelectedEntity);
            var color = view.IsSolid ? Color.FromRgb(80, 226, 130) : Color.FromRgb(255, 176, 60);

            var pen = new Pen(new SolidColorBrush(color, selected ? 1.0 : 0.75), selected ? 2 : 1.25)
            {
                DashStyle = view.IsKinematic ? DashStyle.Dash : null,
            };
            var fill = new SolidColorBrush(color, selected ? 0.16 : 0.08);

            if (view.IsCircle)
            {
                double radius = view.Rect.Width / 2;
                context.DrawEllipse(fill, pen, view.Rect.Center, radius, radius);
            }
            else
            {
                context.DrawRectangle(fill, pen, view.Rect);
            }

            // Cruz no centro: com colisor grande a borda pode sair da tela, e o centro é o que
            // o Offset move — é ele que precisa ficar achável.
            var center = view.Rect.Center;
            var crossPen = new Pen(new SolidColorBrush(color, 0.9), 1);
            context.DrawLine(crossPen, new Point(center.X - 4, center.Y), new Point(center.X + 4, center.Y));
            context.DrawLine(crossPen, new Point(center.X, center.Y - 4), new Point(center.X, center.Y + 4));

            if (!selected)
                continue;

            var label = new Avalonia.Media.FormattedText(view.SizeLabel,
                System.Globalization.CultureInfo.CurrentCulture, Avalonia.Media.FlowDirection.LeftToRight,
                new Avalonia.Media.Typeface("Sans-Serif"), 11, new SolidColorBrush(color));
            context.DrawText(label, new Point(view.Rect.X, view.Rect.Y - label.Height - 2));
        }
    }

    private static readonly HashSet<string> UiTypesWithBounds = ["UiButton", "UiPanel", "UiBar"];

    /// <summary>Moldura da tela do jogo dentro do viewport do editor: retângulo da resolução de
    /// referência (aurora.project.json → designWidth/Height, padrão 1280x720) encaixado no painel,
    /// com a escala usada pra converter pixel de tela do jogo em pixel do canvas.
    ///
    /// Resolver Anchor contra o painel do editor (o que era feito antes) mostra o menu montado
    /// numa tela do tamanho do painel — que nunca é o tamanho da janela do jogo. Um UiButton
    /// AnchorX=Center e outro Left encostam um no outro no editor e aparecem separados no jogo,
    /// porque só o Center anda junto com a largura da tela.</summary>
    private (Rect Rect, double Scale) UiFrame()
    {
        double designWidth = Math.Max(1, _viewModel?.DesignWidth ?? 1280);
        double designHeight = Math.Max(1, _viewModel?.DesignHeight ?? 720);

        // Com comparação ligada, quem tem que caber no painel é a tela do APARELHO (que é maior
        // que a do jogo numa das direções) — senão a moldura do aparelho nasceria fora da vista.
        var (outerWidth, outerHeight) = DeviceSize(designWidth, designHeight);

        double scale = Math.Min(Bounds.Width / outerWidth, Bounds.Height / outerHeight);
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
            scale = 1;

        // A escala base é a que faz a tela do jogo caber no painel; o zoom da UI multiplica em
        // cima dela. Com zoom 1 a moldura aparece inteira, como sempre apareceu.
        scale *= _uiZoom;

        double width = designWidth * scale;
        double height = designHeight * scale;
        return (new Rect(
            (Bounds.Width - width) / 2 + _uiPan.X,
            (Bounds.Height - height) / 2 + _uiPan.Y,
            width, height), scale);
    }

    /// <summary>
    /// Tamanho da tela do aparelho comparado, na mesma unidade da resolução de referência: a menor
    /// tela com a proporção dele que contém o jogo inteiro. É exatamente a conta do runtime
    /// (Game.ApplyViewport): o jogo é encaixado inteiro e o resto vira barra.
    /// </summary>
    private (double Width, double Height) DeviceSize(double designWidth, double designHeight)
    {
        if (_viewModel?.CompareDevice is not { Width: > 0, Height: > 0 } device)
            return (designWidth, designHeight);

        double designAspect = designWidth / designHeight;
        double deviceAspect = device.Width / (double)device.Height;

        return deviceAspect > designAspect
            ? (designHeight * deviceAspect, designHeight)   // sobra nas laterais
            : (designWidth, designWidth / deviceAspect);    // sobra em cima e embaixo
    }

    /// <summary>Pixel do canvas → pixel de tela do jogo (o X/Y que o UiElement guarda).</summary>
    private Point CanvasToUi(Point canvasPoint)
    {
        var (rect, scale) = UiFrame();
        return new Point((canvasPoint.X - rect.X) / scale, (canvasPoint.Y - rect.Y) / scale);
    }

    /// <summary>Elementos de UI (HUD/menu) de todas as entidades, posicionados na moldura da
    /// resolução de referência — não passam pela câmera/pan/zoom do mundo, igual ao runtime
    /// (UIManager.Draw). O Rect devolvido já está em pixel do canvas, pronto pra desenhar.</summary>
    private IEnumerable<UiElementView> VisibleUiElements()
    {
        if (_viewModel is null)
            yield break;

        var (frame, uiScale) = UiFrame();
        double screenWidth = frame.Width / uiScale;
        double screenHeight = frame.Height / uiScale;

        Rect ToCanvas(double left, double top, double width, double height) => new(
            frame.X + left * uiScale, frame.Y + top * uiScale, width * uiScale, height * uiScale);

        foreach (var entity in _viewModel.Entities)
        {
            foreach (var comp in entity.Components)
            {
                float x = comp.GetFloat("X", 0f);
                float y = comp.GetFloat("Y", 0f);
                string anchorX = comp.GetString("AnchorX") ?? "Left";
                string anchorY = comp.GetString("AnchorY") ?? "Top";

                if (UiTypesWithBounds.Contains(comp.Type))
                {
                    var (defaultWidth, defaultHeight) = comp.Type switch
                    {
                        "UiButton" => (120f, 32f),
                        "UiBar"    => (100f, 12f),
                        _          => (100f, 100f), // UiPanel
                    };
                    double width = comp.GetFloat("Width", defaultWidth);
                    double height = comp.GetFloat("Height", defaultHeight);

                    // Botão com imagem e Width/Height zerados herda o tamanho do PNG
                    // (UIManager.BuildButton) — mesma regra do UiImage.
                    if (comp.Type == "UiButton" && (width <= 0 || height <= 0))
                    {
                        var buttonBitmap = ResolveTexture(comp.GetString("Texture"));
                        if (width <= 0) width = buttonBitmap?.Size.Width ?? defaultWidth;
                        if (height <= 0) height = buttonBitmap?.Size.Height ?? defaultHeight;
                    }

                    double left = ResolveAnchorAxis(anchorX, x, screenWidth, width);
                    double top = ResolveAnchorAxis(anchorY, y, screenHeight, height);
                    yield return new UiElementView(entity, comp, comp.Type, ToCanvas(left, top, width, height), "");
                }
                else if (comp.Type == "UiImage")
                {
                    // Width/Height ausentes ou <= 0: o runtime usa o tamanho natural da textura
                    // (UIManager.BuildImage). Assumir 32x32 aqui mostrava um quadradinho no editor
                    // e a imagem inteira no jogo.
                    var bitmap = ResolveTexture(comp.GetString("Texture"));
                    double width = comp.GetFloat("Width", 0f);
                    double height = comp.GetFloat("Height", 0f);
                    if (width <= 0) width = bitmap?.Size.Width ?? 32;
                    if (height <= 0) height = bitmap?.Size.Height ?? 32;

                    double left = ResolveAnchorAxis(anchorX, x, screenWidth, width);
                    double top = ResolveAnchorAxis(anchorY, y, screenHeight, height);
                    yield return new UiElementView(entity, comp, comp.Type, ToCanvas(left, top, width, height), "");
                }
                else if (comp.Type == "UiText")
                {
                    // Medido com o TTF do próprio projeto, igual Font.MeasureText faz em runtime:
                    // o tamanho entra no cálculo do Anchor, então estimativa por contagem de
                    // caracteres (o que havia aqui) desloca todo texto Center/Right no preview.
                    string text = comp.GetString("Text") ?? "";
                    float textScale = comp.GetFloat("Scale", 1f);
                    float maxTextWidth = comp.GetFloat("MaxWidth", 0f);
                    float fontSize = _viewModel.UiFontSize;

                    string laidOut;
                    double width, height;
                    if (_viewModel.UiFont is { } font)
                    {
                        laidOut = font.Wrap(text, maxTextWidth, fontSize, textScale);
                        (width, height) = font.Measure(laidOut, fontSize, textScale);
                    }
                    else
                    {
                        // Sem o TTF em disco: estimativa grosseira, mas ao menos proporcional ao
                        // tamanho de fonte do jogo em vez de um 7px/caractere fixo.
                        laidOut = text;
                        int longest = 0;
                        foreach (var line in text.Split('\n'))
                            longest = Math.Max(longest, line.Length);
                        width = longest * fontSize * 0.5 * textScale;
                        height = (text.Count(c => c == '\n') + 1) * fontSize * 1.17 * textScale;
                    }

                    width = Math.Max(width, 4);
                    double left = ResolveAnchorAxis(anchorX, x, screenWidth, width);
                    double top = ResolveAnchorAxis(anchorY, y, screenHeight, height);
                    yield return new UiElementView(entity, comp, comp.Type, ToCanvas(left, top, width, height), laidOut);
                }
                else if (comp.Type == "UiJoystick")
                {
                    // Mesma convenção de Aurora.Runtime.UI.UIManager.JoystickCenter: X/Y+Anchor
                    // definem o canto de um quadrado de lado 2*Radius, círculo fica no meio.
                    float radius = comp.GetFloat("Radius", 70f);
                    double side = radius * 2.0;
                    double left = ResolveAnchorAxis(anchorX, x, screenWidth, side);
                    double top = ResolveAnchorAxis(anchorY, y, screenHeight, side);
                    yield return new UiElementView(entity, comp, comp.Type, ToCanvas(left, top, side, side), "");
                }
            }
        }
    }

    // Mesmíssimo código que o runtime roda: UiAnchor.cs entra neste projeto por link no .csproj,
    // não por cópia. Era regra duplicada, e foi por isso que o preview divergiu do jogo.
    private static double ResolveAnchorAxis(string anchor, float coordinate, double screenSize, double elementSize)
        => Aurora.Runtime.UI.UiAnchor.Resolve(anchor, coordinate, (float)screenSize, (float)elementSize);

    private static double UnresolveAnchorAxis(string anchor, double edge, double screenSize, double elementSize)
        => Aurora.Runtime.UI.UiAnchor.Unresolve(anchor, (float)edge, (float)screenSize, (float)elementSize);

    /// <summary>Converte hex "#RRGGBB"/"#RRGGBBAA" (convenção do engine, alpha por último) —
    /// Avalonia.Media.Color.Parse espera alpha primeiro, não dá pra reusar direto.</summary>
    private static Color ParseEngineColor(string? hex, Color fallback)
        => Aurora.Editor.Models.EngineColor.Parse(hex, fallback);

    /// <summary>
    /// Desenha um SpriteRenderer com a cor do componente, como o <c>World.Render</c> faz:
    /// sem textura é um retângulo pintado com a Color (não um placeholder fixo), com textura
    /// a Color entra como tinta.
    ///
    /// <para>A tinta aqui é aproximação: o runtime multiplica textura × cor no shader e o
    /// <see cref="DrawingContext"/> não tem blend de multiplicação. O alpha é exato
    /// (opacidade) e o matiz é pintado por cima recortado pela silhueta da imagem — chega
    /// perto o bastante para decidir a cor no editor, que é para o que o preview serve.</para>
    /// </summary>
    private void DrawSprite(DrawingContext context, Bitmap? bitmap, Rect rect, Color tint)
    {
        if (bitmap is null)
        {
            // Cor 100% transparente não desenharia nada e a entidade ficaria impossível de
            // achar/clicar no editor — contorno tracejado mantém ela selecionável.
            if (tint.A == 0)
                context.DrawRectangle(
                    new Pen(new SolidColorBrush(Colors.Magenta, 0.8), 1 / _zoom) { DashStyle = DashStyle.Dash },
                    rect);
            else
                context.FillRectangle(new SolidColorBrush(tint), rect);

            return;
        }

        using var _ = context.PushOpacity(tint.A / 255.0);
        context.DrawImage(bitmap, new Rect(bitmap.Size), rect);

        if (tint is { R: 255, G: 255, B: 255 })
            return;                          // branco = sem tinta (caso mais comum)

        using var mask = context.PushOpacityMask(new ImageBrush(bitmap) { Stretch = Stretch.Fill }, rect);
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(tint.R, tint.G, tint.B), 0.55), rect);
    }

    /// <summary>Contorno da tela do jogo (resolução de referência) com o resto do viewport
    /// escurecido — sem essa moldura não dá pra saber, olhando o editor, onde é "a borda da
    /// tela" que os Anchor Right/Bottom/Center usam como referência.</summary>
    private void DrawUiFrame(DrawingContext context)
    {
        var (frame, scale) = UiFrame();

        DrawCompareDevice(context, frame, scale);

        var shade = new SolidColorBrush(Colors.Black, 0.28);
        context.FillRectangle(shade, new Rect(0, 0, Bounds.Width, frame.Y));
        context.FillRectangle(shade, new Rect(0, frame.Bottom, Bounds.Width, Bounds.Height - frame.Bottom));
        context.FillRectangle(shade, new Rect(0, frame.Y, frame.X, frame.Height));
        context.FillRectangle(shade, new Rect(frame.Right, frame.Y, Bounds.Width - frame.Right, frame.Height));

        context.DrawRectangle(new Pen(new SolidColorBrush(Colors.White, 0.45), 1), frame);

        int width = _viewModel?.DesignWidth ?? 1280;
        int height = _viewModel?.DesignHeight ?? 720;
        var label = new Avalonia.Media.FormattedText($"{width}x{height}", System.Globalization.CultureInfo.CurrentCulture,
            Avalonia.Media.FlowDirection.LeftToRight, new Avalonia.Media.Typeface("Sans-Serif"),
            11, new SolidColorBrush(Colors.White, 0.5));
        context.DrawText(label, new Point(frame.X + 4, Math.Max(0, frame.Y - label.Height - 2)));
    }

    /// <summary>
    /// Tela do aparelho escolhido em "Ver em", desenhada em volta da moldura do jogo. As faixas
    /// entre uma e outra são as barras pretas que o jogador veria — a resposta visual pra "vai
    /// caber no celular?": o conteúdo não é cortado nem se desloca, só encolhe junto.
    /// </summary>
    private void DrawCompareDevice(DrawingContext context, Rect frame, double scale)
    {
        if (_viewModel?.CompareDevice is not { Width: > 0, Height: > 0 } device)
            return;

        var (deviceWidth, deviceHeight) = DeviceSize(
            Math.Max(1, _viewModel.DesignWidth), Math.Max(1, _viewModel.DesignHeight));

        var outer = new Rect(
            frame.Center.X - deviceWidth * scale / 2,
            frame.Center.Y - deviceHeight * scale / 2,
            deviceWidth * scale,
            deviceHeight * scale);

        // Preto de verdade nas barras: é o que a janela do jogo mostra ali (o glClear pinta o
        // fundo inteiro e o viewport do jogo cobre só o miolo).
        context.FillRectangle(Brushes.Black, outer);
        context.DrawRectangle(new Pen(new SolidColorBrush(Colors.White, 0.35), 1)
        {
            DashStyle = DashStyle.Dash,
        }, outer);

        var label = new Avalonia.Media.FormattedText(
            $"{device.Label}  ({device.Width}x{device.Height})",
            System.Globalization.CultureInfo.CurrentCulture, Avalonia.Media.FlowDirection.LeftToRight,
            new Avalonia.Media.Typeface("Sans-Serif"), 11, new SolidColorBrush(Colors.White, 0.55));
        context.DrawText(label, new Point(outer.X + 4, Math.Max(0, outer.Y + 3)));
    }

    private void DrawUiElements(DrawingContext context)
    {
        var (frame, uiScale) = UiFrame();
        int outOfBounds = 0;

        foreach (var element in VisibleUiElements())
        {
            var rect = element.Rect;

            switch (element.Kind)
            {
                case "UiText":
                {
                    var textColor = ParseEngineColor(element.Component.GetString("Color"), Colors.White);
                    float textScale = element.Component.GetFloat("Scale", 1f);
                    double lineHeight = (_viewModel?.UiFont?.LineHeight(_viewModel.UiFontSize)
                                         ?? _viewModel?.UiFontSize * 1.17 ?? 26) * textScale * uiScale;

                    // Uma linha por vez, avançando LineHeight — mesma coisa que Font.Draw faz.
                    // O glifo desenhado é o do sistema (Avalonia não carrega TTF de disco), então
                    // o traço pode diferir um pouco do jogo; a caixa medida é que tem que bater,
                    // porque é ela que decide a posição pelo Anchor.
                    double lineY = rect.Y;
                    foreach (string line in element.Text.Split('\n'))
                    {
                        context.DrawText(
                            new Avalonia.Media.FormattedText(line, System.Globalization.CultureInfo.CurrentCulture,
                                Avalonia.Media.FlowDirection.LeftToRight, new Avalonia.Media.Typeface("Sans-Serif"),
                                Math.Max(1, _viewModel?.UiFontSize * textScale * uiScale ?? 14), new SolidColorBrush(textColor)),
                            new Point(rect.X, lineY));
                        lineY += lineHeight;
                    }
                    break;
                }

                case "UiImage":
                {
                    var bitmap = ResolveTexture(element.Component.GetString("Texture"));
                    if (bitmap is not null)
                        DrawSprite(context, bitmap, rect,
                            ParseEngineColor(element.Component.GetString("Color"), Colors.White));
                    else
                        // Sem textura o runtime não desenha nada; o placeholder existe só pra
                        // dar onde clicar enquanto a imagem não foi escolhida.
                        context.DrawRectangle(new SolidColorBrush(Colors.Magenta, 0.35),
                            new Pen(new SolidColorBrush(Colors.Magenta, 0.8), 1), rect);
                    break;
                }

                case "UiBar":
                {
                    // Fundo + preenchimento, como o runtime desenha. A fração é ilustrativa: o
                    // valor real vem de uma variável do GameState que só existe rodando o jogo.
                    var back = ParseEngineColor(element.Component.GetString("BackColor"), Color.FromRgb(48, 48, 48));
                    var fillColor = ParseEngineColor(element.Component.GetString("FillColor"), Color.FromRgb(64, 192, 64));
                    context.FillRectangle(new SolidColorBrush(back), rect);
                    context.FillRectangle(new SolidColorBrush(fillColor),
                        rect.WithWidth(rect.Width * 0.7));
                    context.DrawRectangle(new Pen(new SolidColorBrush(Colors.White, 0.25), 1), rect);
                    break;
                }

                case "UiJoystick":
                {
                    var bg = ParseEngineColor(element.Component.GetString("BaseColor"), Color.FromArgb(70, 255, 255, 255));
                    var center = rect.Center;
                    double radius = rect.Width / 2.0;
                    context.DrawEllipse(new SolidColorBrush(bg), new Pen(new SolidColorBrush(Colors.White, 0.35), 1.5),
                        center, radius, radius);
                    context.DrawEllipse(new SolidColorBrush(Colors.White, 0.5), null, center, radius * 0.4, radius * 0.4);
                    break;
                }

                default:
                {
                    // UiButton com Texture: a imagem substitui o retângulo colorido, igual ao
                    // runtime (UIManager.Draw). Sem textura (ou com caminho quebrado) cai no
                    // retângulo de sempre, pra caixa de clique continuar visível no editor.
                    var buttonBitmap = element.Kind == "UiButton"
                        ? ResolveTexture(element.Component.GetString("Texture"))
                        : null;

                    if (buttonBitmap is not null)
                    {
                        context.DrawImage(buttonBitmap, new Rect(buttonBitmap.Size), rect);
                    }
                    else
                    {
                        var bg = ParseEngineColor(element.Component.GetString("Color"), Color.FromArgb(255, 58, 56, 96));
                        context.FillRectangle(new SolidColorBrush(bg), rect);
                        context.DrawRectangle(new Pen(new SolidColorBrush(Colors.White, 0.25), 1), rect);
                    }

                    if (element.Kind == "UiButton")
                    {
                        string text = element.Component.GetString("Text") ?? "";
                        var textColor = ParseEngineColor(element.Component.GetString("TextColor"), Colors.White);
                        var formatted = new Avalonia.Media.FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                            Avalonia.Media.FlowDirection.LeftToRight, new Avalonia.Media.Typeface("Sans-Serif"),
                            13 * uiScale, new SolidColorBrush(textColor));
                        context.DrawText(formatted, new Point(
                            rect.X + (rect.Width - formatted.Width) / 2,
                            rect.Y + (rect.Height - formatted.Height) / 2));
                    }
                    break;
                }
            }

            // Fora da moldura = fora da tela em QUALQUER aparelho (a resolução de referência é a
            // tela inteira do jogo, não o pedaço visível de um monitor). Marca em vermelho: o
            // elemento continua desenhado onde está, mas para de passar por "só está escondido".
            if (!ContainsWithTolerance(frame, rect))
            {
                outOfBounds++;
                context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromRgb(224, 87, 93)), 1.5)
                {
                    DashStyle = DashStyle.Dash,
                }, rect);
            }

            if (ReferenceEquals(element.Entity, _viewModel?.SelectedEntity))
                context.DrawRectangle(new Pen(Brushes.Cyan, 1.5), rect);
        }

        if (outOfBounds > 0)
        {
            string texto = outOfBounds == 1
                ? "1 elemento fora da tela"
                : $"{outOfBounds} elementos fora da tela";

            var aviso = new Avalonia.Media.FormattedText(texto,
                System.Globalization.CultureInfo.CurrentCulture, Avalonia.Media.FlowDirection.LeftToRight,
                new Avalonia.Media.Typeface("Sans-Serif"), 12,
                new SolidColorBrush(Color.FromRgb(224, 87, 93)));
            context.DrawText(aviso, new Point(frame.X + 6, frame.Bottom - aviso.Height - 6));
        }
    }

    /// <summary>Contém, perdoando meio pixel. Elemento encostado na borda cai em arredondamento e
    /// seria acusado de estar fora — aviso que aparece sozinho ensina a ignorar aviso.</summary>
    private static bool ContainsWithTolerance(Rect frame, Rect element)
        => element.X >= frame.X - 0.5
           && element.Y >= frame.Y - 0.5
           && element.Right <= frame.Right + 0.5
           && element.Bottom <= frame.Bottom + 0.5;

    // ---- Renderização ----

    /// <summary>Desenha a grade do snap sobre a área visível, só quando o snap está ligado —
    /// arrastar preso a uma grade invisível parece bug. Some quando a grade fica densa demais
    /// pra ter sentido na tela (linha a cada 4px vira um borrão que esconde a cena).</summary>
    private void DrawSnapGrid(DrawingContext context)
    {
        if (_viewModel is not { SnapToGrid: true } vm || vm.SnapSize <= 0)
            return;

        double step = (double)vm.SnapSize;
        if (step * _zoom < 6)
            return;

        var topLeft = ScreenToWorld(new Point(0, 0));
        var bottomRight = ScreenToWorld(new Point(Bounds.Width, Bounds.Height));

        var pen = new Pen(new SolidColorBrush(Colors.White, 0.10), 1);

        for (double x = Math.Floor(topLeft.X / step) * step; x <= bottomRight.X; x += step)
        {
            double screenX = WorldToScreen(new Point(x, 0)).X;
            context.DrawLine(pen, new Point(screenX, 0), new Point(screenX, Bounds.Height));
        }

        for (double y = Math.Floor(topLeft.Y / step) * step; y <= bottomRight.Y; y += step)
        {
            double screenY = WorldToScreen(new Point(0, y)).Y;
            context.DrawLine(pen, new Point(0, screenY), new Point(Bounds.Width, screenY));
        }
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(24, 22, 38)), new Rect(Bounds.Size));
        DrawAxes(context);

        if (_viewModel is null)
            return;

        DrawSnapGrid(context);

        // Sprites e tilemaps intercalados por camada, como no runtime.
        var drawables = VisibleSprites().Select(s => (s.Layer, Sprite: (SpriteView?)s, Map: (TilemapView?)null))
            .Concat(VisibleTilemaps().Select(t => (t.Layer, Sprite: (SpriteView?)null, Map: (TilemapView?)t)))
            .OrderBy(d => d.Layer);

        SpriteView? selectedSprite = null;
        TilemapView? selectedMap = null;

        foreach (var (_, spriteView, mapView) in drawables)
        {
            if (spriteView is { } sprite)
            {
                // Numa tela de UI ("UI":true) o UIManager.Load só lê componentes Ui* — Transform e
                // SpriteRenderer são silenciosamente descartados e NÃO existem no jogo. Desenhar
                // igual a uma cena normal fazia o editor mostrar um objeto que nunca ia aparecer;
                // fantasma + contorno vermelho tracejado deixa claro sem impedir de selecionar
                // pra apagar.
                bool ghost = _viewModel.IsUiScreenDocument;

                using (context.PushTransform(sprite.LocalToScreen))
                using (context.PushOpacity(ghost ? 0.25 : 1.0))
                {
                    var component = sprite.Entity.Sprite;
                    DrawSprite(context, ResolveTexture(component?.GetString("Texture")), sprite.LocalRect,
                        ParseEngineColor(component?.GetString("Color"), Colors.White));
                }

                if (ghost)
                {
                    using var _ = context.PushTransform(sprite.LocalToScreen);
                    context.DrawRectangle(
                        new Pen(new SolidColorBrush(Colors.OrangeRed, 0.9), 1.5 / _zoom) { DashStyle = DashStyle.Dash },
                        sprite.LocalRect);
                }

                if (ReferenceEquals(sprite.Entity, _viewModel.SelectedEntity))
                    selectedSprite = sprite;
            }
            else if (mapView is { } map)
            {
                DrawTilemap(context, map);
                if (ReferenceEquals(map.Entity, _viewModel.SelectedEntity))
                    selectedMap = map;
            }
        }

        // Colisores por cima dos sprites (senão o sprite tapa a hitbox) e abaixo dos gizmos, que
        // precisam continuar legíveis pra arrastar. Tiles sólidos entram junto: no runtime eles
        // colidem igual, mesmo sem componente Collider.
        if (_viewModel.ShowColliders)
        {
            foreach (var map in VisibleTilemaps())
                DrawSolidTiles(context, map);
            DrawColliders(context);
        }

        if (selectedSprite is { } sel)
            DrawGizmos(context, sel);
        if (selectedMap is { } selMap)
            DrawTilemapSelection(context, selMap);

        // Preview do viewport da câmera quando a entidade selecionada tem CameraController.
        var selEntity = _viewModel.SelectedEntity;
        if (selEntity?.Camera is { } camComp && selEntity.Transform is { } camTransform)
            DrawCameraPreview(context, camTransform, camComp);

        // Elementos de UI (HUD/menu) por cima de tudo — pixel de tela, sem câmera. A moldura da
        // resolução de referência só aparece quando há UI pra posicionar, pra não poluir cena
        // de gameplay pura.
        if (_viewModel.IsUiScreenDocument || VisibleUiElements().Any())
        {
            DrawUiFrame(context);
            DrawUiElements(context);
        }
    }

    private void DrawTilemap(DrawingContext context, TilemapView map)
    {
        using var _ = context.PushTransform(map.LocalToScreen);

        if (map.Tileset is null)
        {
            // Sem tileset ainda: só a moldura da grade.
            context.DrawRectangle(new Pen(Brushes.Magenta, 1 / _zoom), map.LocalRect);
            return;
        }

        var tilesNode = map.Entity.Tilemap?.Node["Tiles"] as System.Text.Json.Nodes.JsonArray;
        if (tilesNode is null)
            return;

        int perRow = Math.Max(1, (int)map.Tileset.Size.Width / map.TileWidth);
        int total = Math.Min(tilesNode.Count, map.Columns * map.Rows);

        // Só as células que aparecem no painel. Sem isto o editor desenha o mapa INTEIRO todo
        // frame: num mapa de 200x200 são 40 mil DrawImage por quadro, e mexer no viewport vira
        // um arrastão — enquanto o jogo, que já corta pelo campo de visão da câmera
        // (World.DrawTilemap), roda liso no mesmo mapa. Quem faz mapa grande batia nisso só no
        // editor e concluía que a engine não aguentava o mapa.
        var (firstColumn, firstRow, lastColumn, lastRow) = VisibleCells(map);

        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                int cell = row * map.Columns + column;
                if (cell < 0 || cell >= total)
                    continue;

                int index = tilesNode[cell].AsInt(-1);
                if (index < 0)
                    continue;

                var source = new Rect(index % perRow * map.TileWidth, index / perRow * map.TileHeight,
                    map.TileWidth, map.TileHeight);
                var dest = new Rect(column * map.CellWidth, row * map.CellHeight,
                    map.CellWidth, map.CellHeight);
                context.DrawImage(map.Tileset, source, dest);
            }
        }
    }

    /// <summary>
    /// Faixa de células do tilemap que cai dentro do painel, em índices de coluna/linha.
    ///
    /// <para>Trabalha com a caixa dos quatro cantos do painel levados pro espaço do mapa: assim a
    /// conta continua valendo com o mapa girado ou escalado, no lugar de assumir que ele está
    /// alinhado com a tela. Se a matriz não puder ser invertida (escala zero), devolve o mapa
    /// inteiro — desenhar demais é lento, desenhar de menos é bug.</para>
    /// </summary>
    private (int FirstColumn, int FirstRow, int LastColumn, int LastRow) VisibleCells(TilemapView map)
    {
        if (map.CellWidth <= 0 || map.CellHeight <= 0
            || !map.LocalToScreen.TryInvert(out var screenToLocal))
            return (0, 0, map.Columns - 1, map.Rows - 1);

        Span<Point> corners =
        [
            screenToLocal.Transform(new Point(0, 0)),
            screenToLocal.Transform(new Point(Bounds.Width, 0)),
            screenToLocal.Transform(new Point(0, Bounds.Height)),
            screenToLocal.Transform(new Point(Bounds.Width, Bounds.Height)),
        ];

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var corner in corners)
        {
            minX = Math.Min(minX, corner.X);
            minY = Math.Min(minY, corner.Y);
            maxX = Math.Max(maxX, corner.X);
            maxY = Math.Max(maxY, corner.Y);
        }

        int firstColumn = Math.Max(0, (int)Math.Floor(minX / map.CellWidth));
        int firstRow = Math.Max(0, (int)Math.Floor(minY / map.CellHeight));
        int lastColumn = Math.Min(map.Columns - 1, (int)Math.Ceiling(maxX / map.CellWidth));
        int lastRow = Math.Min(map.Rows - 1, (int)Math.Ceiling(maxY / map.CellHeight));

        return (firstColumn, firstRow, lastColumn, lastRow);
    }

    /// <summary>Moldura ciana + grade de células quando o pincel está ativo.</summary>
    private void DrawTilemapSelection(DrawingContext context, TilemapView map)
    {
        using var _ = context.PushTransform(map.LocalToScreen);

        var rect = map.LocalRect;
        context.DrawRectangle(new Pen(Brushes.Cyan, 1.5 / _zoom), rect);

        if (_viewModel?.SelectedTileIndex is null)
            return;

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1 / _zoom);
        for (int x = 1; x < map.Columns; x++)
            context.DrawLine(gridPen, new Point(x * map.CellWidth, 0), new Point(x * map.CellWidth, rect.Height));
        for (int y = 1; y < map.Rows; y++)
            context.DrawLine(gridPen, new Point(0, y * map.CellHeight), new Point(rect.Width, y * map.CellHeight));
    }

    private void DrawAxes(DrawingContext context)
    {
        var origin = new Point(0, 0).Transform(ViewMatrix);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(60, 56, 84)), 1);
        context.DrawLine(pen, new Point(0, origin.Y), new Point(Bounds.Width, origin.Y));
        context.DrawLine(pen, new Point(origin.X, 0), new Point(origin.X, Bounds.Height));
    }

    private void DrawGizmos(DrawingContext context, SpriteView view)
    {
        var outline = new Pen(Brushes.Cyan, 1.5);

        var corners = CornerScreenPoints(view);
        for (int i = 0; i < 4; i++)
            context.DrawLine(outline, corners[i], corners[(i + 1) % 4]);

        // Alças de escala nos cantos (quadrados fixos em pixels de tela).
        foreach (var corner in corners)
            context.DrawRectangle(Brushes.White, new Pen(Brushes.Cyan, 1), HandleRect(corner));

        // Alça de rotação acima do topo do sprite.
        var (anchor, handle) = RotationHandlePoints(view);
        context.DrawLine(new Pen(Brushes.Cyan, 1), anchor, handle);
        context.DrawEllipse(Brushes.White, new Pen(Brushes.Cyan, 1), handle, HandleSize / 2, HandleSize / 2);
    }

    private static Rect HandleRect(Point center)
        => new(center.X - HandleSize / 2, center.Y - HandleSize / 2, HandleSize, HandleSize);

    private static Point[] CornerScreenPoints(SpriteView view) =>
    [
        view.LocalRect.TopLeft.Transform(view.LocalToScreen),
        view.LocalRect.TopRight.Transform(view.LocalToScreen),
        view.LocalRect.BottomRight.Transform(view.LocalToScreen),
        view.LocalRect.BottomLeft.Transform(view.LocalToScreen),
    ];

    private (Point Anchor, Point Handle) RotationHandlePoints(SpriteView view)
    {
        var topCenterLocal = new Point(view.LocalRect.Center.X, view.LocalRect.Top);
        var anchor = topCenterLocal.Transform(view.LocalToScreen);

        // 26px de tela acima do topo, na direção "para cima" do sprite (segue a rotação).
        var upLocal = new Point(topCenterLocal.X, topCenterLocal.Y - RotationHandleOffset / _zoom);
        var handle = upLocal.Transform(view.LocalToScreen);

        return (anchor, handle);
    }

    /// <summary>
    /// Retângulo amarelo = viewport da câmera no mundo.
    /// Retângulo laranja tracejado = bounds de clamping (quando ativo).
    /// </summary>
    private void DrawCameraPreview(DrawingContext context,
        ViewModels.ComponentViewModel transform, ViewModels.ComponentViewModel cam)
    {
        // Centro: segue a entidade Follow se configurada, senão a própria entidade.
        float cx, cy;
        var followName = cam.GetString("Follow");
        if (!string.IsNullOrEmpty(followName)
            && _viewModel?.Entities.FirstOrDefault(e => e.Name == followName) is { } followEntity
            && followEntity.Transform is { } ft)
        {
            cx = ft.GetFloat("X", 0f);
            cy = ft.GetFloat("Y", 0f);
        }
        else
        {
            cx = transform.GetFloat("X", 0f);
            cy = transform.GetFloat("Y", 0f);
        }

        cx += cam.GetFloat("OffsetX", 0f);
        cy += cam.GetFloat("OffsetY", 0f);

        float zoom  = Math.Max(cam.GetFloat("Zoom", 1f), 0.001f);
        float halfW = cam.GetFloat("ViewWidth",  1280f) / (2f * zoom);
        float halfH = cam.GetFloat("ViewHeight", 720f)  / (2f * zoom);

        var tl = new Point(cx - halfW, cy - halfH).Transform(ViewMatrix);
        var br = new Point(cx + halfW, cy + halfH).Transform(ViewMatrix);
        context.DrawRectangle(null, new Pen(Brushes.Yellow, 2), new Rect(tl, br));

        // Label "CÂMERA" no canto superior esquerdo do retângulo.
        context.DrawText(
            new Avalonia.Media.FormattedText("CÂMERA", System.Globalization.CultureInfo.CurrentCulture,
                Avalonia.Media.FlowDirection.LeftToRight,
                new Avalonia.Media.Typeface("Sans-Serif"), 11, Brushes.Yellow),
            new Point(tl.X + 4, tl.Y + 4));

        // Bounds de clamping (tracejado laranja).
        if (cam.GetBool("ClampBounds", false))
        {
            float bx = cam.GetFloat("BoundsX", 0f);
            float by = cam.GetFloat("BoundsY", 0f);
            float bw = cam.GetFloat("BoundsWidth",  1280f);
            float bh = cam.GetFloat("BoundsHeight", 720f);

            var btl = new Point(bx,      by     ).Transform(ViewMatrix);
            var bbr = new Point(bx + bw, by + bh).Transform(ViewMatrix);
            context.DrawRectangle(null,
                new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 165, 0)), 1.5, DashStyle.Dash),
                new Rect(btl, bbr));
        }
    }

    private Bitmap? ResolveTexture(string? path)
    {
        if (path is null || _viewModel?.Document is null)
            return null;

        if (_textures.TryGetValue(path, out var cached))
            return cached;

        string full = Path.Combine(_viewModel.Document.AssetsRoot, path);
        Bitmap? bitmap = File.Exists(full) ? new Bitmap(full) : null;
        _textures[path] = bitmap;
        return bitmap;
    }

    // ---- Interação ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var point = e.GetCurrentPoint(this);
        _lastPointer = point.Position;

        if (point.Properties.IsMiddleButtonPressed || point.Properties.IsRightButtonPressed)
        {
            _drag = DragMode.Pan;
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed || _viewModel is null)
            return;

        // 0-a) Elementos de UI ficam por cima de tudo (desenhados por último) — checa primeiro.
        var hitUi = VisibleUiElements().LastOrDefault(u => u.Rect.Contains(point.Position));
        if (hitUi.Entity is not null)
        {
            _viewModel.SelectedEntity = hitUi.Entity;
            _drag = DragMode.MoveUi;
            _target = hitUi.Entity;
            _uiTarget = hitUi.Component;
            // Offset guardado no canto superior-esquerdo (não no X/Y bruto): com AnchorX=Right o
            // X cresce pra esquerda, então arrastar somando no X direto move o elemento ao contrário.
            var uiPoint = CanvasToUi(point.Position);
            var uiTopLeft = CanvasToUi(hitUi.Rect.TopLeft);
            double uiScale = UiFrame().Scale;
            _uiTargetSize = new Size(hitUi.Rect.Width / uiScale, hitUi.Rect.Height / uiScale);
            _dragOffset = new Point(uiTopLeft.X - uiPoint.X, uiTopLeft.Y - uiPoint.Y);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // 0) Pincel ativo + clique dentro do tilemap selecionado = pintar.
        if (_viewModel.SelectedTileIndex is not null
            && VisibleTilemaps().FirstOrDefault(t => ReferenceEquals(t.Entity, _viewModel.SelectedEntity))
                is { Entity: not null } paintTarget
            && TryPaintAt(paintTarget, point.Position))
        {
            _drag = DragMode.Paint;
            _target = paintTarget.Entity;
            e.Handled = true;
            return;
        }

        // 1) Gizmos da seleção atual têm prioridade sobre tudo.
        if (_viewModel.SelectedEntity is not null
            && VisibleSprites().FirstOrDefault(s => ReferenceEquals(s.Entity, _viewModel.SelectedEntity)) is { Entity: not null } selectedView)
        {
            if (TryStartGizmoDrag(selectedView, point.Position))
            {
                e.Handled = true;
                return;
            }
        }

        // 2) Clique no corpo: sprites primeiro (maior camada vence), depois tilemaps.
        var hitEntity = VisibleSprites()
            .OrderByDescending(s => s.Layer)
            .FirstOrDefault(s => HitsBody(s, point.Position)).Entity;

        hitEntity ??= VisibleTilemaps()
            .OrderByDescending(t => t.Layer)
            .FirstOrDefault(t => t.LocalToScreen.TryInvert(out var inverse)
                && t.LocalRect.Contains(point.Position.Transform(inverse))).Entity;

        _viewModel.SelectedEntity = hitEntity;

        if (hitEntity is not null)
        {
            _drag = DragMode.Move;
            _target = hitEntity;
            var world = ScreenToWorld(point.Position);
            var transform = hitEntity.Transform!;
            _dragOffset = new Point(
                transform.GetFloat("X", 0f) - world.X,
                transform.GetFloat("Y", 0f) - world.Y);
        }

        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>Pinta a célula sob o cursor com o pincel ativo. False se fora da grade.</summary>
    private bool TryPaintAt(TilemapView map, Point screenPoint)
    {
        if (_viewModel?.SelectedTileIndex is not { } brush
            || !map.LocalToScreen.TryInvert(out var inverse))
            return false;

        var local = screenPoint.Transform(inverse);
        int x = (int)Math.Floor(local.X / map.CellWidth);
        int y = (int)Math.Floor(local.Y / map.CellHeight);
        if (x < 0 || y < 0 || x >= map.Columns || y >= map.Rows)
            return false;

        map.Entity.SetTile(x, y, brush);
        return true;
    }

    private static bool HitsBody(SpriteView view, Point screenPoint)
        => view.LocalToScreen.TryInvert(out var inverse)
           && view.LocalRect.Contains(screenPoint.Transform(inverse));

    private bool TryStartGizmoDrag(SpriteView view, Point screenPoint)
    {
        var transform = view.Entity.Transform!;

        var (_, rotationHandle) = RotationHandlePoints(view);
        if (Distance(screenPoint, rotationHandle) <= HandleSize)
        {
            _drag = DragMode.Rotate;
            _target = view.Entity;
            _startRotation = transform.GetFloat("Rotation", 0f);
            var world = ScreenToWorld(screenPoint);
            _startAngle = Math.Atan2(
                world.Y - transform.GetFloat("Y", 0f),
                world.X - transform.GetFloat("X", 0f));
            return true;
        }

        foreach (var corner in CornerScreenPoints(view))
        {
            if (Distance(screenPoint, corner) > HandleSize)
                continue;

            if (!view.LocalToScreen.TryInvert(out var inverse))
                return false;

            _drag = DragMode.Scale;
            _target = view.Entity;
            _startScaleX = transform.GetFloat("ScaleX", 1f);
            _startScaleY = transform.GetFloat("ScaleY", 1f);
            _gestureLocalStart = screenPoint.Transform(inverse);
            return true;
        }

        return false;
    }

    private static double Distance(Point a, Point b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var position = e.GetPosition(this);

        switch (_drag)
        {
            case DragMode.Pan when _viewModel?.IsUiScreenDocument == true:
                // Arrasta a folha da UI junto com o cursor (a moldura segue o mouse), em vez de
                // mover uma câmera que não existe neste documento.
                _uiPan = new Point(
                    _uiPan.X + (position.X - _lastPointer.X),
                    _uiPan.Y + (position.Y - _lastPointer.Y));
                _lastPointer = position;
                InvalidateVisual();
                break;

            case DragMode.Pan:
                _cameraPosition = new Point(
                    _cameraPosition.X - (position.X - _lastPointer.X) / _zoom,
                    _cameraPosition.Y - (position.Y - _lastPointer.Y) / _zoom);
                _lastPointer = position;
                InvalidateVisual();
                break;

            case DragMode.MoveUi when _target is not null && _uiTarget is not null:
            {
                var uiPoint = CanvasToUi(position);
                double screenWidth = _viewModel?.DesignWidth ?? 1280;
                double screenHeight = _viewModel?.DesignHeight ?? 720;
                _target.SetUiPosition(_uiTarget,
                    (float)UnresolveAnchorAxis(_uiTarget.GetString("AnchorX") ?? "Left",
                        uiPoint.X + _dragOffset.X, screenWidth, _uiTargetSize.Width),
                    (float)UnresolveAnchorAxis(_uiTarget.GetString("AnchorY") ?? "Top",
                        uiPoint.Y + _dragOffset.Y, screenHeight, _uiTargetSize.Height));
                break;
            }

            case DragMode.Move when _target is not null:
            {
                var world = ScreenToWorld(position);

                // Pelo ViewModel, nao por _target.SetPosition: arrastar um pai tem que levar os
                // filhos junto no viewport, igual o runtime faz no jogo.
                _viewModel?.MoveEntityWithChildren(_target,
                    (float)Snap(world.X + _dragOffset.X, e.KeyModifiers),
                    (float)Snap(world.Y + _dragOffset.Y, e.KeyModifiers));
                break;
            }

            case DragMode.Scale when _target is not null:
            {
                var view = VisibleSprites().FirstOrDefault(s => ReferenceEquals(s.Entity, _target));
                if (view.Entity is null || !view.LocalToScreen.TryInvert(out var inverse))
                    break;

                // Fator = quanto o cursor se afastou do pivô, por eixo, no espaço local.
                var local = position.Transform(inverse);
                double factorX = Math.Abs(_gestureLocalStart.X) > 1 ? local.X / _gestureLocalStart.X : 1;
                double factorY = Math.Abs(_gestureLocalStart.Y) > 1 ? local.Y / _gestureLocalStart.Y : 1;

                _target.SetScale(
                    (float)Math.Clamp(_startScaleX * Math.Max(factorX, 0.05), 0.01, 1000),
                    (float)Math.Clamp(_startScaleY * Math.Max(factorY, 0.05), 0.01, 1000));
                break;
            }

            case DragMode.Paint when _target is not null:
            {
                var view = VisibleTilemaps().FirstOrDefault(t => ReferenceEquals(t.Entity, _target));
                if (view.Entity is not null)
                    TryPaintAt(view, position);
                break;
            }

            case DragMode.Rotate when _target is not null:
            {
                var transform = _target.Transform!;
                var world = ScreenToWorld(position);
                double angle = Math.Atan2(
                    world.Y - transform.GetFloat("Y", 0f),
                    world.X - transform.GetFloat("X", 0f));
                _target.SetRotation((float)(_startRotation + angle - _startAngle));
                break;
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _drag = DragMode.None;
        _target = null;
        _uiTarget = null;
    }

    /// <summary>Arredonda uma coordenada de mundo pra grade, quando o snap está ligado.
    /// Segurar Alt inverte a decisão: com snap ligado ele solta, com snap desligado ele prende —
    /// alinhar dez plataformas e depois encostar uma no pixel é o mesmo gesto, sem ir no
    /// checkbox no meio do caminho.</summary>
    private double Snap(double value, Avalonia.Input.KeyModifiers modifiers)
    {
        bool invert = modifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt);
        bool snapping = (_viewModel?.SnapToGrid ?? false) ^ invert;
        double step = (double)(_viewModel?.SnapSize ?? 0);

        return snapping && step > 0 ? Math.Round(value / step) * step : value;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        // Documento de tela de UI não tem mundo pra navegar: o scroll aproxima a folha da UI.
        // Numa cena de gameplay o scroll continua sendo a câmera do mundo, e a moldura do HUD
        // fica parada — que é como ela se comporta em jogo.
        if (_viewModel?.IsUiScreenDocument == true)
        {
            ZoomUi(e.GetPosition(this), e.Delta.Y > 0 ? 1.15 : 1 / 1.15);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // Zoom ancorado no cursor: o ponto do mundo sob o mouse não se move.
        var anchor = ScreenToWorld(e.GetPosition(this));
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), 0.05, 20.0);

        var afterAnchor = ScreenToWorld(e.GetPosition(this));
        _cameraPosition = new Point(
            _cameraPosition.X + (anchor.X - afterAnchor.X),
            _cameraPosition.Y + (anchor.Y - afterAnchor.Y));

        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>Aproxima/afasta a moldura da UI mantendo parado o ponto sob o cursor — sem isso o
    /// zoom foge do lugar que se estava olhando e edição vira caça ao elemento.</summary>
    private void ZoomUi(Point cursor, double factor)
    {
        var before = CanvasToUi(cursor);
        _uiZoom = Math.Clamp(_uiZoom * factor, 0.1, 8.0);

        var (_, scale) = UiFrame();
        var after = CanvasToUi(cursor);
        _uiPan = new Point(
            _uiPan.X + (after.X - before.X) * scale,
            _uiPan.Y + (after.Y - before.Y) * scale);
    }
}
