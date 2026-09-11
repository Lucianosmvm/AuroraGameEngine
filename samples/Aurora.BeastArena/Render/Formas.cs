using System.Numerics;
using Aurora.Runtime.Graphics;
using Silk.NET.OpenGL;

namespace BeastArena.Render;

/// <summary>
/// Texturas geradas em código pra desenhar o protótipo sem nenhuma arte: disco e anel brancos,
/// tingidos na hora pela cor do <c>SpriteBatch.Draw</c>. Trocar por sprites depois não mexe na
/// simulação — só em <see cref="VisaoArena"/>.
/// </summary>
public sealed class Formas : IDisposable
{
    private const int Lado = 128;

    public Texture2D Disco { get; }
    public Texture2D Anel { get; }

    public Formas(GL gl)
    {
        // Borda de ~2 px de degradê: com o filtro Nearest da engine, disco de borda dura vira
        // escadinha visível em qualquer tamanho que não seja o original.
        Disco = Gerar(gl, d => Math.Clamp((1f - d) * Lado / 4f, 0f, 1f));
        Anel = Gerar(gl, d => Math.Clamp((1f - d) * Lado / 4f, 0f, 1f) * Math.Clamp((d - 0.82f) * Lado / 4f, 0f, 1f));
    }

    public void DesenharDisco(SpriteBatch batch, Vector2 centro, float raio, Color cor)
        => batch.Draw(Disco, centro, new Vector2(raio * 2f), new Vector2(0.5f), 0f, cor);

    public void DesenharAnel(SpriteBatch batch, Vector2 centro, float raio, Color cor)
        => batch.Draw(Anel, centro, new Vector2(raio * 2f), new Vector2(0.5f), 0f, cor);

    /// <param name="alfa">Distância normalizada ao centro (0 = centro, 1 = borda) → opacidade.</param>
    private static Texture2D Gerar(GL gl, Func<float, float> alfa)
    {
        var pixels = new byte[Lado * Lado * 4];
        float centro = (Lado - 1) / 2f;

        for (int y = 0; y < Lado; y++)
        {
            for (int x = 0; x < Lado; x++)
            {
                float distancia = MathF.Sqrt((x - centro) * (x - centro) + (y - centro) * (y - centro)) / (Lado / 2f);
                int i = (y * Lado + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = 255;
                pixels[i + 3] = (byte)(alfa(distancia) * 255f);
            }
        }

        return Texture2D.FromPixels(gl, Lado, Lado, pixels);
    }

    public void Dispose()
    {
        Disco.Dispose();
        Anel.Dispose();
    }
}
