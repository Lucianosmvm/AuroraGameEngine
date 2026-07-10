using Avalonia.Media.Imaging;

namespace Aurora.Editor.ViewModels;

/// <summary>Um tile na paleta de pintura. Índice -1 = borracha (célula vazia).</summary>
public sealed class TileBrushViewModel
{
    public int Index { get; }
    public CroppedBitmap? Image { get; }
    public bool IsEraser => Index < 0;
    public string Label => IsEraser ? "⌫" : "";

    public TileBrushViewModel(int index, CroppedBitmap? image)
    {
        Index = index;
        Image = image;
    }
}
