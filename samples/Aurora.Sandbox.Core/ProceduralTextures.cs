using Aurora.Runtime.Graphics;
using Silk.NET.OpenGL;

namespace Aurora.Sandbox;

/// <summary>
/// Texturas geradas em código para a demo não depender de arquivos de imagem.
/// Assets reais entram via AssetManager.LoadTexture.
/// </summary>
public static class ProceduralTextures
{
    /// <summary>Retângulo preenchido com borda de 2px.</summary>
    public static Texture2D Bordered(GL gl, int width, int height, Color fill, Color border)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool isBorder = x < 2 || y < 2 || x >= width - 2 || y >= height - 2;
                WritePixel(pixels, (y * width + x) * 4, isBorder ? border : fill);
            }
        }

        return Texture2D.FromPixels(gl, width, height, pixels);
    }

    /// <summary>Círculo preenchido em fundo transparente.</summary>
    public static Texture2D Circle(GL gl, int diameter, Color color)
    {
        var pixels = new byte[diameter * diameter * 4];
        float radius = diameter / 2f;

        for (int y = 0; y < diameter; y++)
        {
            for (int x = 0; x < diameter; x++)
            {
                float dx = x + 0.5f - radius;
                float dy = y + 0.5f - radius;
                if (dx * dx + dy * dy <= radius * radius)
                    WritePixel(pixels, (y * diameter + x) * 4, color);
            }
        }

        return Texture2D.FromPixels(gl, diameter, diameter, pixels);
    }

    private static void WritePixel(byte[] pixels, int offset, Color color)
    {
        pixels[offset + 0] = (byte)(color.R * 255);
        pixels[offset + 1] = (byte)(color.G * 255);
        pixels[offset + 2] = (byte)(color.B * 255);
        pixels[offset + 3] = (byte)(color.A * 255);
    }
}
