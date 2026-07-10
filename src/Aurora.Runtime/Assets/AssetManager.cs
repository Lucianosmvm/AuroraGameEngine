using Aurora.Runtime.Graphics;
using Silk.NET.OpenGL;

namespace Aurora.Runtime.Assets;

/// <summary>Carrega e cacheia assets do disco. Caminhos relativos a <see cref="RootPath"/>.</summary>
public sealed class AssetManager : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pasta raiz dos assets. Padrão: "Assets" ao lado do executável.</summary>
    public string RootPath { get; set; }

    public AssetManager(GL gl, string rootPath = "Assets")
    {
        _gl = gl;
        RootPath = rootPath;
    }

    public Texture2D LoadTexture(string relativePath)
    {
        if (_textures.TryGetValue(relativePath, out var cached))
            return cached;

        var texture = Texture2D.FromFile(_gl, Path.Combine(RootPath, relativePath));
        _textures[relativePath] = texture;
        return texture;
    }

    public void Dispose()
    {
        foreach (var texture in _textures.Values)
            texture.Dispose();
        _textures.Clear();
    }
}
