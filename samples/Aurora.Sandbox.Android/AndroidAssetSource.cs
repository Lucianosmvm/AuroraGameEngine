using Android.App;
using Aurora.Runtime.Assets;

namespace Aurora.Sandbox.Droid;

/// <summary>Lê assets empacotados no APK (itens AndroidAsset do csproj).</summary>
public sealed class AndroidAssetSource : IAssetSource
{
    private readonly Android.Content.Res.AssetManager _assets =
        Application.Context.Assets ?? throw new InvalidOperationException("AssetManager Android indisponível.");

    public Stream Open(string path) => _assets.Open(Normalize(path));

    public bool Exists(string path)
    {
        try
        {
            using var stream = _assets.Open(Normalize(path));
            return true;
        }
        catch (Java.IO.IOException)
        {
            return false;
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
