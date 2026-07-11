using Aurora.Editor.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

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

    private enum DragMode { None, Pan, Move, Scale, Rotate, Paint }

    /// <summary>Sprite pronto para desenhar/testar: matriz local→tela e retângulo local.</summary>
    private readonly record struct SpriteView(EntityViewModel Entity, Matrix LocalToScreen, Rect LocalRect, float Layer);

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

    // Estado inicial do gesto de escala/rotação.
    private Point _gestureLocalStart;
    private float _startScaleX, _startScaleY, _startRotation;
    private double _startAngle;

    private MainViewModel? _viewModel;

    /// <summary>Ponto do mundo no centro do viewport — onde entidades novas nascem.</summary>
    public Point CameraCenter => _cameraPosition;

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
                if (args.PropertyName == nameof(MainViewModel.SelectedEntity))
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
            double width = (bitmap?.Size.Width ?? 32) * Math.Abs(transform.GetFloat("ScaleX", 1f));
            double height = (bitmap?.Size.Height ?? 32) * Math.Abs(transform.GetFloat("ScaleY", 1f));
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

    // ---- Renderização ----

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(24, 22, 38)), new Rect(Bounds.Size));
        DrawAxes(context);

        if (_viewModel is null)
            return;

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
                using (context.PushTransform(sprite.LocalToScreen))
                {
                    var bitmap = ResolveTexture(sprite.Entity.Sprite?.GetString("Texture"));
                    if (bitmap is not null)
                        context.DrawImage(bitmap, new Rect(bitmap.Size), sprite.LocalRect);
                    else
                        context.FillRectangle(Brushes.Magenta, sprite.LocalRect);
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

        if (selectedSprite is { } sel)
            DrawGizmos(context, sel);
        if (selectedMap is { } selMap)
            DrawTilemapSelection(context, selMap);

        // Preview do viewport da câmera quando a entidade selecionada tem CameraController.
        var selEntity = _viewModel.SelectedEntity;
        if (selEntity?.Camera is { } camComp && selEntity.Transform is { } camTransform)
            DrawCameraPreview(context, camTransform, camComp);
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

        for (int cell = 0; cell < total; cell++)
        {
            int index = tilesNode[cell]?.GetValue<int>() ?? -1;
            if (index < 0)
                continue;

            var source = new Rect(index % perRow * map.TileWidth, index / perRow * map.TileHeight,
                map.TileWidth, map.TileHeight);
            var dest = new Rect(cell % map.Columns * map.CellWidth, cell / map.Columns * map.CellHeight,
                map.CellWidth, map.CellHeight);
            context.DrawImage(map.Tileset, source, dest);
        }
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
            case DragMode.Pan:
                _cameraPosition = new Point(
                    _cameraPosition.X - (position.X - _lastPointer.X) / _zoom,
                    _cameraPosition.Y - (position.Y - _lastPointer.Y) / _zoom);
                _lastPointer = position;
                InvalidateVisual();
                break;

            case DragMode.Move when _target is not null:
            {
                var world = ScreenToWorld(position);
                _target.SetPosition(
                    (float)(world.X + _dragOffset.X),
                    (float)(world.Y + _dragOffset.Y));
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
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
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
}
