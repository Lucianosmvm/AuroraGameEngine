using Aurora.Runtime.Graphics;
using Silk.NET.OpenGL;

namespace Aurora.Runtime.Assets;

/// <summary>Carrega e cacheia assets de um <see cref="IAssetSource"/>.</summary>
public sealed class AssetManager : IDisposable
{
    private readonly GL _gl;
    private readonly IAssetSource _source;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Font> _fonts = new(StringComparer.OrdinalIgnoreCase);

    public AssetManager(GL gl, IAssetSource source)
    {
        _gl = gl;
        _source = source;
    }

    public AssetManager(GL gl, string rootPath = "Assets")
        : this(gl, new FileAssetSource(rootPath))
    {
    }

    public Texture2D LoadTexture(string path)
    {
        if (_textures.TryGetValue(path, out var cached))
            return cached;

        using var stream = _source.Open(path);
        var texture = Texture2D.FromStream(_gl, stream);
        _textures[path] = texture;
        return texture;
    }

    /// <summary>Caminho pelo qual a textura foi carregada, ou null se não veio deste manager (serialização de cenas).</summary>
    public string? GetTexturePath(Texture2D texture)
    {
        foreach (var (path, cached) in _textures)
        {
            if (ReferenceEquals(cached, texture))
                return path;
        }

        return null;
    }

    /// <summary>Carrega uma fonte TTF rasterizada no tamanho dado (cache por caminho+tamanho).</summary>
    public Font LoadFont(string path, float pixelSize)
    {
        string key = $"{path}#{pixelSize}";
        if (_fonts.TryGetValue(key, out var cached))
            return cached;

        using var stream = _source.Open(path);
        var font = Font.FromStream(_gl, stream, pixelSize);
        _fonts[key] = font;
        return font;
    }

    /// <summary>Lê um asset de texto inteiro (JSON de cena, configs).</summary>
    public string LoadText(string path)
    {
        using var reader = new StreamReader(_source.Open(path));
        return reader.ReadToEnd();
    }

    public bool Exists(string path) => _source.Exists(path);

    public void Dispose()
    {
        foreach (var texture in _textures.Values)
            texture.Dispose();
        _textures.Clear();

        foreach (var font in _fonts.Values)
            font.Dispose();
        _fonts.Clear();
    }
}
