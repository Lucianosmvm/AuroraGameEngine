using Avalonia.Media.Imaging;

namespace Aurora.Editor.ViewModels;

/// <summary>Uma textura encontrada na pasta de assets do projeto.</summary>
public sealed class AssetViewModel
{
    /// <summary>Caminho relativo à raiz de assets, com '/' — o formato usado nas cenas.</summary>
    public string RelativePath { get; }

    public string Name => Path.GetFileNameWithoutExtension(RelativePath);

    public Bitmap? Thumbnail { get; }

    public AssetViewModel(string assetsRoot, string relativePath)
    {
        RelativePath = relativePath;

        try
        {
            using var stream = File.OpenRead(Path.Combine(assetsRoot, relativePath));
            Thumbnail = Bitmap.DecodeToWidth(stream, 48);
        }
        catch (Exception)
        {
            Thumbnail = null;
        }
    }
}
