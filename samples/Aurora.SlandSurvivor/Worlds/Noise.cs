namespace Aurora.SlandSurvivor.Worlds;

/// <summary>
/// Ruído de valor determinístico (mesma seed = mesmo mundo, em qualquer máquina).
///
/// <para>Não usa <see cref="Random"/> em lugar nenhum: cada ponto do espaço é derivado por
/// hash das coordenadas, então dá para amostrar o mundo fora de ordem — o gerador de cavernas
/// pergunta "o que tem em (x,y)?" sem precisar ter gerado a coluna vizinha antes.</para>
/// </summary>
public static class Noise
{
    /// <summary>Hash inteiro → [0,1). Base de todo o resto.</summary>
    public static float Hash(int seed, int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFF_FFFF) / 16777216f;
        }
    }

    /// <summary>Hash inteiro → [0,1) para decisões pontuais (nasce árvore aqui? veio de minério?).</summary>
    public static float HashPoint(int seed, int x, int y, int salt) => Hash(seed + salt * 7919, x, y);

    /// <summary>Ruído de valor 2D com interpolação suave, em [0,1].</summary>
    public static float Value(int seed, float x, float y)
    {
        int xi = (int)MathF.Floor(x);
        int yi = (int)MathF.Floor(y);
        float fx = Smooth(x - xi);
        float fy = Smooth(y - yi);

        float top = Lerp(Hash(seed, xi, yi), Hash(seed, xi + 1, yi), fx);
        float bottom = Lerp(Hash(seed, xi, yi + 1), Hash(seed, xi + 1, yi + 1), fx);
        return Lerp(top, bottom, fy);
    }

    /// <summary>Soma de oitavas do <see cref="Value"/>, em [0,1]. Detalhe cresce com octaves.</summary>
    public static float Fbm(int seed, float x, float y, int octaves = 4,
        float lacunarity = 2f, float gain = 0.5f)
    {
        float sum = 0f, amplitude = 1f, total = 0f, frequency = 1f;

        for (int i = 0; i < octaves; i++)
        {
            sum += Value(seed + i * 131, x * frequency, y * frequency) * amplitude;
            total += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }

        return total > 0f ? sum / total : 0f;
    }

    /// <summary>Ruído 1D (relevo, temperatura por coluna), em [0,1].</summary>
    public static float Fbm1D(int seed, float x, int octaves = 4) => Fbm(seed, x, 0.5f, octaves);

    /// <summary>
    /// Variante "crista": vale perto de 1 onde o ruído passa pelo meio. É o que transforma
    /// campos suaves em túneis compridos de caverna em vez de bolhas isoladas.
    /// </summary>
    public static float Ridge(int seed, float x, float y, int octaves = 3)
        => 1f - MathF.Abs(Fbm(seed, x, y, octaves) * 2f - 1f);

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Smooth(float t) => t * t * (3f - 2f * t);
}
