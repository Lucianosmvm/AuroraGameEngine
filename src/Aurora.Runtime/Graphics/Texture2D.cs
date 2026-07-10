using Silk.NET.OpenGL;
using StbImageSharp;

namespace Aurora.Runtime.Graphics;

/// <summary>Textura 2D em memória de GPU. Filtro Nearest por padrão (pixel art).</summary>
public sealed class Texture2D : IDisposable
{
    private readonly GL _gl;

    public uint Handle { get; }
    public int Width { get; }
    public int Height { get; }

    private unsafe Texture2D(GL gl, int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length != width * height * 4)
            throw new ArgumentException($"Esperado {width * height * 4} bytes RGBA, recebido {rgba.Length}.");

        _gl = gl;
        Width = width;
        Height = height;

        Handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, Handle);

        fixed (byte* pixels = rgba)
        {
            gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba8,
                (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        }

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    /// <summary>Cria textura a partir de bytes RGBA crus (4 bytes por pixel).</summary>
    public static Texture2D FromPixels(GL gl, int width, int height, ReadOnlySpan<byte> rgba)
        => new(gl, width, height, rgba);

    /// <summary>Carrega PNG/JPG de um stream via StbImageSharp. Aceita streams não-seekáveis (assets Android).</summary>
    public static Texture2D FromStream(GL gl, Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var image = ImageResult.FromMemory(buffer.ToArray(), ColorComponents.RedGreenBlueAlpha);
        return new Texture2D(gl, image.Width, image.Height, image.Data);
    }

    /// <summary>Carrega PNG/JPG do disco.</summary>
    public static Texture2D FromFile(GL gl, string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Textura não encontrada: {path}", path);

        using var stream = File.OpenRead(path);
        return FromStream(gl, stream);
    }

    public void Bind(int slot = 0)
    {
        _gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + slot));
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
    }

    public void Dispose() => _gl.DeleteTexture(Handle);
}
