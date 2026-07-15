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

        Texture2D texture;
        try
        {
            using var stream = _source.Open(path);
            texture = Texture2D.FromStream(_gl, stream);
        }
        catch (Exception ex)
        {
            // Textura faltando não pode derrubar o jogo publicado (typo de caminho, asset
            // esquecido no export) — loga e desenha um quadriculado magenta no lugar, igual
            // engines maiores fazem.
            Console.Error.WriteLine($"[AssetManager] Textura '{path}' não carregou ({ex.Message}) — usando placeholder.");
            texture = CreateMissingTexture();
        }

        _textures[path] = texture;
        return texture;
    }

    /// <summary>Quadriculado magenta/preto 8x8 — placeholder de textura faltando (não cacheado
    /// entre paths: cada entrada do dicionário é dona da própria instância, Dispose fica simples).</summary>
    private Texture2D CreateMissingTexture()
    {
        const int size = 8;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool magenta = ((x / 4) + (y / 4)) % 2 == 0;
                int i = (y * size + x) * 4;
                pixels[i + 0] = (byte)(magenta ? 255 : 0);
                pixels[i + 1] = 0;
                pixels[i + 2] = (byte)(magenta ? 255 : 0);
                pixels[i + 3] = 255;
            }
        }
        return Texture2D.FromPixels(_gl, size, size, pixels);
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
