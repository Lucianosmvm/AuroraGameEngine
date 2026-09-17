using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace CaminhosDaFe;

/// <summary>
/// Área retangular do curral, centrada no Transform. Ovelha que entra aqui conta como recolhida
/// e passa a pastar só dentro dela. gerar_mapa.py calcula o tamanho pelos tiles de palha.
/// </summary>
[SceneScript]
public sealed class Curral : Behavior
{
    public float Largura = 144f;
    public float Altura = 96f;

    public Vector2 Centro => Get<Transform>()?.Position ?? Vector2.Zero;

    public bool Contem(Vector2 ponto, float margem = 0f)
    {
        var c = Centro;
        return MathF.Abs(ponto.X - c.X) <= Largura / 2f - margem
            && MathF.Abs(ponto.Y - c.Y) <= Altura / 2f - margem;
    }

    /// <summary>Distância do ponto até a cerca, por fora (0 se estiver dentro).</summary>
    public float DistanciaAte(Vector2 ponto)
    {
        var c = Centro;
        float dx = MathF.Max(0f, MathF.Abs(ponto.X - c.X) - Largura / 2f);
        float dy = MathF.Max(0f, MathF.Abs(ponto.Y - c.Y) - Altura / 2f);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public Vector2 PontoAleatorio(float margem)
    {
        var c = Centro;
        float meiaL = MathF.Max(0f, Largura / 2f - margem);
        float meiaA = MathF.Max(0f, Altura / 2f - margem);
        return c + new Vector2(
            (Random.Shared.NextSingle() * 2f - 1f) * meiaL,
            (Random.Shared.NextSingle() * 2f - 1f) * meiaA);
    }

    /// <summary>Atalho pros scripts: o curral da cena, se houver.</summary>
    public static Curral? Da(World? world)
    {
        if (world is null)
            return null;
        foreach (var (_, curral) in world.Query<Curral>())
            return curral;
        return null;
    }
}
