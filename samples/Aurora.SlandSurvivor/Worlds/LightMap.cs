namespace Aurora.SlandSurvivor.Worlds;

/// <summary>
/// Iluminação por tile, no estilo do gênero: a luz nasce do céu (só onde a coluna está
/// aberta) e das tochas, e se espalha perdendo intensidade — mais rápido dentro da rocha
/// do que no ar. O resultado é o que faz caverna ser escura e tocha valer alguma coisa.
///
/// <para>Só a janela visível (mais uma margem) é calculada, uma vez por frame: uma busca em
/// largura por níveis (15 baldes, do mais claro ao mais escuro) sobre ~3 mil células. Custa
/// muito menos que desenhar os tiles dessa mesma janela.</para>
/// </summary>
public sealed class LightMap
{
    public const int MaxLevel = 15;

    private byte[] _light = [];
    private int _x0, _y0, _width, _height;

    private readonly List<int>[] _buckets = new List<int>[MaxLevel + 1];

    public LightMap()
    {
        for (int i = 0; i <= MaxLevel; i++)
            _buckets[i] = new List<int>(256);
    }

    /// <summary>
    /// Fontes de luz que não são tile: o brilho fraco que o jogador carrega, um projétil,
    /// um inimigo luminoso. Preenchida pelo jogo antes de <see cref="Compute"/>, em
    /// coordenadas de tile.
    /// </summary>
    public readonly List<(int X, int Y, int Level)> ExtraSources = [];

    /// <param name="skyFactor">0 (noite fechada) a 1 (meio-dia) — escurece a luz do céu.</param>
    public void Compute(TileWorld world, int x0, int y0, int width, int height, float skyFactor)
    {
        _x0 = x0;
        _y0 = y0;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);

        int cells = _width * _height;
        if (_light.Length < cells)
            _light = new byte[cells];

        Array.Clear(_light, 0, cells);
        foreach (var bucket in _buckets)
            bucket.Clear();

        byte sky = (byte)Math.Clamp((int)MathF.Round(MaxLevel * skyFactor), 0, MaxLevel);

        // --- fontes ---------------------------------------------------------
        for (int ly = 0; ly < _height; ly++)
        {
            int wy = _y0 + ly;

            for (int lx = 0; lx < _width; lx++)
            {
                int wx = _x0 + lx;
                if (wx < 0 || wx >= world.Width || wy < 0 || wy >= world.Height)
                    continue;

                byte level = 0;

                if (wy <= world.SkyTop(wx) && world.GetWall(wx, wy) < 0)
                    level = sky;

                int emission = TileDb.LightOf(world.Get(wx, wy));
                if (emission > level)
                    level = (byte)emission;

                if (level == 0)
                    continue;

                int index = ly * _width + lx;
                _light[index] = level;
                _buckets[level].Add(index);
            }
        }

        foreach (var (sx, sy, level) in ExtraSources)
        {
            int lx = sx - _x0, ly = sy - _y0;
            if (lx < 0 || ly < 0 || lx >= _width || ly >= _height || level <= 0)
                continue;

            int index = ly * _width + lx;
            if (_light[index] >= level)
                continue;

            _light[index] = (byte)Math.Min(level, MaxLevel);
            _buckets[_light[index]].Add(index);
        }

        // --- propagação ------------------------------------------------------
        for (int level = MaxLevel; level > 0; level--)
        {
            var bucket = _buckets[level];

            for (int i = 0; i < bucket.Count; i++)
            {
                int index = bucket[i];
                if (_light[index] != level)
                    continue;                              // já foi superado por uma fonte melhor

                int lx = index % _width;
                int ly = index / _width;

                Spread(world, lx - 1, ly, level);
                Spread(world, lx + 1, ly, level);
                Spread(world, lx, ly - 1, level);
                Spread(world, lx, ly + 1, level);
            }
        }
    }

    private void Spread(TileWorld world, int lx, int ly, int fromLevel)
    {
        if (lx < 0 || ly < 0 || lx >= _width || ly >= _height)
            return;

        int wx = _x0 + lx, wy = _y0 + ly;
        if (wx < 0 || wy < 0 || wx >= world.Width || wy >= world.Height)
            return;

        // Custo pelo material de destino: rocha apaga rápido, parede de fundo apaga médio,
        // ar aberto quase não apaga.
        int cost = world.BlocksLight(wx, wy) ? 3
            : world.GetWall(wx, wy) >= 0 ? 2
            : 1;

        int level = fromLevel - cost;
        if (level <= 0)
            return;

        int index = ly * _width + lx;
        if (_light[index] >= level)
            return;

        _light[index] = (byte)level;
        _buckets[level].Add(index);
    }

    /// <summary>Luz do tile em 0..1. Fora da janela calculada devolve 0 (não é desenhado).</summary>
    public float At(int tileX, int tileY)
    {
        int lx = tileX - _x0, ly = tileY - _y0;
        if (lx < 0 || ly < 0 || lx >= _width || ly >= _height)
            return 0f;

        return _light[ly * _width + lx] / (float)MaxLevel;
    }

    /// <summary>
    /// Luz no CANTO superior esquerdo do tile: média dos quatro tiles que se encontram ali.
    /// É o que permite desenhar a escuridão em degradê (interpolando os cantos) em vez de um
    /// quadradão por tile — sem isso a caverna vira um xadrez de blocos pretos.
    /// </summary>
    public float Corner(int tileX, int tileY)
        => (At(tileX - 1, tileY - 1) + At(tileX, tileY - 1)
            + At(tileX - 1, tileY) + At(tileX, tileY)) * 0.25f;

    /// <summary>Média dos 4 tiles ao redor de um ponto do mundo — usado por sprites (jogador,
    /// inimigos, itens caídos), que não estão alinhados à grade.</summary>
    public float AtWorld(System.Numerics.Vector2 position)
    {
        int tx = TileWorld.ToTile(position.X);
        int ty = TileWorld.ToTile(position.Y);

        float sum = At(tx, ty) + At(tx + 1, ty) + At(tx, ty + 1) + At(tx - 1, ty);
        return sum * 0.25f;
    }
}
