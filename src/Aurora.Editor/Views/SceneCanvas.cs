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

    private enum DragMode { None, Pan, Move, Scale, Rotate }

    /// <summary>Sprite pronto para desenhar/testar: matriz local→tela e retângulo local.</summary>
    private readonly record struct SpriteView(EntityViewModel Entity, Matrix LocalToScreen, Rect LocalRect, float Layer);

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

            var bitmap = ResolveTexture(entity);
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

        SpriteView? selected = null;

        foreach (var view in VisibleSprites().OrderBy(s => s.Layer))
        {
            using (context.PushTransform(view.LocalToScreen))
            {
                var bitmap = ResolveTexture(view.Entity);
                if (bitmap is not null)
                    context.DrawImage(bitmap, new Rect(bitmap.Size), view.LocalRect);
                else
                    context.FillRectangle(Brushes.Magenta, view.LocalRect);
            }

            if (ReferenceEquals(view.Entity, _viewModel.SelectedEntity))
                selected = view;
        }

        if (selected is { } sel)
            DrawGizmos(context, sel);
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

    private Bitmap? ResolveTexture(EntityViewModel entity)
    {
        string? path = entity.Sprite?.GetString("Texture");
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

        // 2) Clique no corpo de um sprite: seleciona e começa a mover (maior camada vence).
        var hit = VisibleSprites()
            .OrderByDescending(s => s.Layer)
            .FirstOrDefault(s => HitsBody(s, point.Position));

        _viewModel.SelectedEntity = hit.Entity;

        if (hit.Entity is not null)
        {
            _drag = DragMode.Move;
            _target = hit.Entity;
            var world = ScreenToWorld(point.Position);
            var transform = hit.Entity.Transform!;
            _dragOffset = new Point(
                transform.GetFloat("X", 0f) - world.X,
                transform.GetFloat("Y", 0f) - world.Y);
        }

        InvalidateVisual();
        e.Handled = true;
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
