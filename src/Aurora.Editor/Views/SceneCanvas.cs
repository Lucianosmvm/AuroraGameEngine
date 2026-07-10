using Aurora.Editor.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Aurora.Editor.Views;

/// <summary>
/// Viewport 2D da cena: desenha sprites, pan (botão do meio), zoom (scroll),
/// seleção (clique) e mover entidade (arrastar). Mesma convenção do runtime:
/// Y cresce para baixo, câmera centrada em Position.
/// </summary>
public sealed class SceneCanvas : Control
{
    private readonly Dictionary<string, Bitmap?> _textures = new(StringComparer.OrdinalIgnoreCase);

    private Point _cameraPosition;
    private double _zoom = 0.5;

    private bool _panning;
    private Point _lastPointer;
    private EntityViewModel? _dragging;
    private Point _dragOffset;

    private MainViewModel? _viewModel;

    public SceneCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
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

    // ---- Transformação mundo ↔ tela ----

    private Point WorldToScreen(Point world) => new(
        (world.X - _cameraPosition.X) * _zoom + Bounds.Width / 2,
        (world.Y - _cameraPosition.Y) * _zoom + Bounds.Height / 2);

    private Point ScreenToWorld(Point screen) => new(
        (screen.X - Bounds.Width / 2) / _zoom + _cameraPosition.X,
        (screen.Y - Bounds.Height / 2) / _zoom + _cameraPosition.Y);

    // ---- Renderização ----

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(24, 22, 38)), new Rect(Bounds.Size));

        DrawAxes(context);

        if (_viewModel is null)
            return;

        foreach (var (entity, rect) in VisibleSprites().OrderBy(s => s.Layer).Select(s => (s.Entity, s.Rect)))
        {
            var bitmap = ResolveTexture(entity);
            if (bitmap is not null)
                context.DrawImage(bitmap, new Rect(bitmap.Size), rect);
            else
                context.FillRectangle(Brushes.Magenta, rect);

            if (ReferenceEquals(entity, _viewModel.SelectedEntity))
                context.DrawRectangle(new Pen(Brushes.Cyan, 2), rect.Inflate(2));
        }
    }

    private void DrawAxes(DrawingContext context)
    {
        var origin = WorldToScreen(new Point(0, 0));
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(60, 56, 84)), 1);
        context.DrawLine(pen, new Point(0, origin.Y), new Point(Bounds.Width, origin.Y));
        context.DrawLine(pen, new Point(origin.X, 0), new Point(origin.X, Bounds.Height));
    }

    /// <summary>Entidades com Transform + SpriteRenderer visível, com o retângulo em tela.</summary>
    private IEnumerable<(EntityViewModel Entity, Rect Rect, float Layer)> VisibleSprites()
    {
        if (_viewModel is null)
            yield break;

        foreach (var entity in _viewModel.Entities)
        {
            var transform = entity.Transform;
            var sprite = entity.Sprite;
            if (transform is null || sprite is null || !sprite.GetBool("Visible", true))
                continue;

            var bitmap = ResolveTexture(entity);
            double width = bitmap?.Size.Width ?? 32;
            double height = bitmap?.Size.Height ?? 32;

            width *= transform.GetFloat("ScaleX", 1f);
            height *= transform.GetFloat("ScaleY", 1f);

            double originX = sprite.GetFloat("OriginX", 0.5f);
            double originY = sprite.GetFloat("OriginY", 0.5f);

            var topLeft = WorldToScreen(new Point(
                transform.GetFloat("X", 0f) - width * originX,
                transform.GetFloat("Y", 0f) - height * originY));

            yield return (entity,
                new Rect(topLeft, new Size(width * _zoom, height * _zoom)),
                sprite.GetFloat("Layer", 0f));
        }
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
            _panning = true;
            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed && _viewModel is not null)
        {
            // Topo primeiro: maior camada vence o clique.
            var hit = VisibleSprites()
                .OrderByDescending(s => s.Layer)
                .FirstOrDefault(s => s.Rect.Contains(point.Position));

            _viewModel.SelectedEntity = hit.Entity;

            if (hit.Entity is not null)
            {
                _dragging = hit.Entity;
                var world = ScreenToWorld(point.Position);
                var transform = hit.Entity.Transform!;
                _dragOffset = new Point(
                    transform.GetFloat("X", 0f) - world.X,
                    transform.GetFloat("Y", 0f) - world.Y);
            }

            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var position = e.GetPosition(this);

        if (_panning)
        {
            _cameraPosition = new Point(
                _cameraPosition.X - (position.X - _lastPointer.X) / _zoom,
                _cameraPosition.Y - (position.Y - _lastPointer.Y) / _zoom);
            _lastPointer = position;
            InvalidateVisual();
            return;
        }

        if (_dragging is not null && _viewModel is not null)
        {
            var world = ScreenToWorld(position);
            _dragging.SetPosition(
                (float)(world.X + _dragOffset.X),
                (float)(world.Y + _dragOffset.Y));
            _viewModel.NotifyEdited();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _panning = false;
        _dragging = null;
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
