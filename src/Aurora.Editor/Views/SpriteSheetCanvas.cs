using System.Globalization;
using Aurora.Editor.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Aurora.Editor.Views;

/// <summary>
/// A folha desenhada em tamanho ampliado com a grade de recorte por cima: cada célula numerada
/// com o índice que o clipe usa, a seleção pintada, e o frame que o preview está tocando com
/// borda destacada.
///
/// <para>É este desenho que resolve o problema de montar animação no olho: sem ver a grade em
/// cima da arte, decidir "frame 6 até 9" é adivinhar, e o erro só aparece no Play. O controle
/// não guarda estado do recorte — quem sabe da grade é o <see cref="SpriteSheetViewModel"/>;
/// aqui só se converte pixel de tela em pixel de imagem e de volta.</para>
/// </summary>
public sealed class SpriteSheetCanvas : Control
{
    private SpriteSheetViewModel? _vm;

    private bool _dragging;
    private Point _dragStart;
    private Point _dragCurrent;
    private bool _dragAdditive;

    public SpriteSheetCanvas()
    {
        // Pixel art ampliada 8× precisa sair quadrada, não borrada — interpolar aqui esconderia
        // justamente a borda do frame que o autor está tentando alinhar.
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        Focusable = true;
        ClipToBounds = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm is not null)
            _vm.VisualChanged -= OnVisualChanged;

        _vm = DataContext as SpriteSheetViewModel;

        if (_vm is not null)
            _vm.VisualChanged += OnVisualChanged;

        OnVisualChanged();
    }

    private void OnVisualChanged()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>O controle mede pelo tamanho da imagem ampliada: quem rola é o ScrollViewer de
    /// fora, então uma folha de 512px em 8× pede 4096px e ganha barra de rolagem sozinha.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_vm is null || !_vm.HasImage)
            return new Size(240, 160);

        return new Size(_vm.ImageWidth * _vm.Zoom, _vm.ImageHeight * _vm.Zoom);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_vm is null)
            return;

        double zoom = _vm.Zoom;
        var area = new Rect(0, 0, _vm.ImageWidth * zoom, _vm.ImageHeight * zoom);

        DrawCheckerboard(context, area);

        if (_vm.Image is { } bitmap)
            context.DrawImage(bitmap, new Rect(bitmap.Size), area);

        if (!_vm.HasImage)
        {
            DrawHint(context, "Escolha uma imagem à esquerda.");
            return;
        }

        DrawFrames(context, zoom);
        DrawDragRectangle(context);
    }

    /// <summary>Xadrez atrás da imagem: PNG de personagem é quase todo transparente, e sobre um
    /// fundo liso não dá pra distinguir "fundo transparente" de "pixel dessa cor".</summary>
    private static void DrawCheckerboard(DrawingContext context, Rect area)
    {
        if (area.Width <= 0 || area.Height <= 0)
            return;

        const int cell = 8;
        var light = new SolidColorBrush(Color.FromRgb(0x2A, 0x27, 0x39));
        var dark = new SolidColorBrush(Color.FromRgb(0x21, 0x1E, 0x2F));

        context.FillRectangle(dark, area);
        for (int y = 0; y * cell < area.Height; y++)
        {
            for (int x = (y % 2); x * cell < area.Width; x += 2)
            {
                var square = new Rect(x * cell, y * cell,
                    Math.Min(cell, area.Width - x * cell), Math.Min(cell, area.Height - y * cell));
                context.FillRectangle(light, square);
            }
        }
    }

    private void DrawFrames(DrawingContext context, double zoom)
    {
        if (_vm is null)
            return;

        var gridPen = new Pen(new SolidColorBrush(Colors.White, 0.35), 1);
        var selectedPen = new Pen(new SolidColorBrush(Color.FromRgb(0x7C, 0x5C, 0xFF)), 2);
        var playingPen = new Pen(new SolidColorBrush(Color.FromRgb(0x3F, 0xB6, 0x5C)), 2.5);
        var selectedFill = new SolidColorBrush(Color.FromRgb(0x7C, 0x5C, 0xFF), 0.22);

        int playing = _vm.PreviewFrame;
        bool showNumbers = _vm.FrameWidth * zoom >= 22 && _vm.FrameHeight * zoom >= 16;

        for (int i = 0; i < _vm.FrameCount; i++)
        {
            if (_vm.RectOf(i) is not { } r)
                continue;

            var rect = new Rect(r.X * zoom, r.Y * zoom, r.Width * zoom, r.Height * zoom);
            int order = _vm.Selection.IndexOf(i);

            if (order >= 0)
                context.FillRectangle(selectedFill, rect);

            context.DrawRectangle(order >= 0 ? selectedPen : gridPen, rect);

            if (i == playing)
                context.DrawRectangle(playingPen, rect.Inflate(-1));

            if (!showNumbers)
                continue;

            // Na seleção o número vira a POSIÇÃO na sequência (1º, 2º...) e não o índice: é a
            // ordem de reprodução que o autor está montando ao clicar fora de ordem.
            string label = order >= 0 ? $"{i} ({order + 1}º)" : i.ToString(CultureInfo.InvariantCulture);
            var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Sans-Serif"), 10,
                new SolidColorBrush(Colors.White, order >= 0 ? 0.95 : 0.55));

            var origin = new Point(rect.X + 2, rect.Y + 1);
            context.FillRectangle(new SolidColorBrush(Colors.Black, 0.45),
                new Rect(origin, new Size(text.Width + 4, text.Height)));
            context.DrawText(text, origin + new Point(2, 0));
        }
    }

    private void DrawDragRectangle(DrawingContext context)
    {
        if (!_dragging)
            return;

        var rect = new Rect(_dragStart, _dragCurrent);
        var color = _vm?.FreeCut == true ? Color.FromRgb(0xE0, 0xA0, 0x30) : Color.FromRgb(0x7C, 0x5C, 0xFF);
        context.FillRectangle(new SolidColorBrush(color, 0.15), rect);
        context.DrawRectangle(new Pen(new SolidColorBrush(color, 0.9), 1) { DashStyle = DashStyle.Dash }, rect);
    }

    private void DrawHint(DrawingContext context, string message)
    {
        var text = new FormattedText(message, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Sans-Serif"), 12, new SolidColorBrush(Colors.White, 0.5));
        context.DrawText(text, new Point(12, 12));
    }

    // ------------------------------------------------------------- ponteiro

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_vm is null || !_vm.HasImage)
            return;

        _dragging = true;
        _dragStart = _dragCurrent = e.GetPosition(this);
        _dragAdditive = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_dragging)
            return;

        _dragCurrent = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_vm is null || !_dragging)
            return;

        _dragging = false;
        e.Pointer.Capture(null);
        _dragCurrent = e.GetPosition(this);

        double zoom = _vm.Zoom;
        int x0 = (int)Math.Floor(Math.Min(_dragStart.X, _dragCurrent.X) / zoom);
        int y0 = (int)Math.Floor(Math.Min(_dragStart.Y, _dragCurrent.Y) / zoom);
        int x1 = (int)Math.Ceiling(Math.Max(_dragStart.X, _dragCurrent.X) / zoom);
        int y1 = (int)Math.Ceiling(Math.Max(_dragStart.Y, _dragCurrent.Y) / zoom);

        // Arrasto de menos de um frame de tela conta como clique: mão treme, e perder a seleção
        // por dois pixels de movimento é o tipo de coisa que faz parecer que o botão falhou.
        bool isClick = Math.Abs(_dragCurrent.X - _dragStart.X) < 4 && Math.Abs(_dragCurrent.Y - _dragStart.Y) < 4;

        if (_vm.FreeCut && !isClick)
            _vm.AddFreeRect(x0, y0, x1 - x0, y1 - y0);
        else if (isClick)
            _vm.ToggleFrame(_vm.FrameAt((int)(_dragCurrent.X / zoom), (int)(_dragCurrent.Y / zoom)), _dragAdditive);
        else
            _vm.SelectArea(x0, y0, x1, y1, _dragAdditive);

        InvalidateVisual();
    }

    /// <summary>Ctrl+scroll amplia, como em todo editor de imagem. Scroll puro fica com o
    /// ScrollViewer de fora, senão não dá pra descer numa folha alta.</summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (_vm is null || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            base.OnPointerWheelChanged(e);
            return;
        }

        _vm.Zoom += e.Delta.Y > 0 ? 1 : -1;
        e.Handled = true;
    }
}
