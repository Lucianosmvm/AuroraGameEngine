using System.Numerics;
using Aurora.Runtime.Ecs.Components;

namespace Aurora.SlandSurvivor.Worlds;

/// <summary>
/// Mundo em tiles em tempo de execução: embrulha os dois <see cref="Tilemap"/> da engine
/// (frente sólida + parede de fundo) e centraliza tudo que o jogo pergunta ao mapa —
/// converter pixel em tile, saber se é sólido, quebrar e colocar bloco.
///
/// <para>Mantém em cache <see cref="SkyTop"/>: a primeira linha que bloqueia luz em cada
/// coluna. É o que o mapa de luz usa como fonte do céu, e por isso é atualizado a cada
/// bloco quebrado/colocado em vez de ser recalculado por frame (varrer 1200 colunas de 300
/// linhas todo frame custaria mais que o resto do jogo inteiro).</para>
/// </summary>
public sealed class TileWorld
{
    public const float TileSize = 16f;

    private readonly int[] _skyTop;
    private readonly Dictionary<int, float> _damage = new();

    public TileWorld(GeneratedWorld generated, Tilemap foreground, Tilemap background)
    {
        Seed = generated.Seed;
        Width = generated.Width;
        Height = generated.Height;
        Biomes = generated.Biomes;
        SpawnTile = new Vector2(generated.SpawnX, generated.SpawnY);

        Foreground = foreground;
        Background = background;

        Foreground.Width = Background.Width = Width;
        Foreground.Height = Background.Height = Height;
        Foreground.Tiles = generated.Foreground;
        Background.Tiles = generated.Background;
        Foreground.TileWidth = Foreground.TileHeight = (int)TileSize;
        Background.TileWidth = Background.TileHeight = (int)TileSize;

        Foreground.SolidTiles = [.. TileDb.SolidIds()];
        Background.SolidTiles = [];                       // parede nunca bloqueia movimento

        _skyTop = new int[Width];
        RebuildSkyTop();
    }

    public int Seed { get; }
    public int Width { get; }
    public int Height { get; }
    public Biome[] Biomes { get; }
    public Tilemap Foreground { get; }
    public Tilemap Background { get; }

    /// <summary>Nascimento, em coordenadas de tile.</summary>
    public Vector2 SpawnTile { get; }

    public float WorldWidth => Width * TileSize;
    public float WorldHeight => Height * TileSize;

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public int Get(int x, int y) => InBounds(x, y) ? Foreground.Tiles[y * Width + x] : TileId.Empty;

    public int GetWall(int x, int y) => InBounds(x, y) ? Background.Tiles[y * Width + x] : TileId.Empty;

    public bool IsSolid(int x, int y) => TileDb.IsSolid(Get(x, y));

    public bool BlocksLight(int x, int y) => TileDb.BlocksLight(Get(x, y));

    public bool IsLiquid(int x, int y) => Get(x, y) is TileId.Water or TileId.Lava;

    /// <summary>Primeira linha da coluna que bloqueia a luz do céu (Height se a coluna é toda aberta).</summary>
    public int SkyTop(int x) => x >= 0 && x < Width ? _skyTop[x] : 0;

    /// <summary>Altura do terreno para spawn/HUD: a primeira linha sólida da coluna.</summary>
    public int SurfaceY(int x)
    {
        for (int y = 0; y < Height; y++)
        {
            if (IsSolid(x, y))
                return y;
        }

        return Height - 1;
    }

    public void SetTile(int x, int y, int tile)
    {
        if (!InBounds(x, y))
            return;

        int index = y * Width + x;
        int previous = Foreground.Tiles[index];
        if (previous == tile)
            return;

        Foreground.Tiles[index] = tile;
        _damage.Remove(index);
        UpdateSkyTop(x, y, previous, tile);
    }

    public void SetWall(int x, int y, int tile)
    {
        if (InBounds(x, y))
            Background.Tiles[y * Width + x] = tile;
    }

    // ---------------------------------------------------------------------
    //  Conversões
    // ---------------------------------------------------------------------

    public static int ToTile(float world) => (int)MathF.Floor(world / TileSize);

    public static Vector2 TileToWorld(int x, int y) => new(x * TileSize, y * TileSize);

    public static Vector2 TileCenter(int x, int y) => new(x * TileSize + TileSize * 0.5f, y * TileSize + TileSize * 0.5f);

    public (int X, int Y) WorldToTile(Vector2 position) => (ToTile(position.X), ToTile(position.Y));

    /// <summary>Existe algum tile sólido dentro do retângulo (usado antes de colocar bloco).</summary>
    public bool RectOverlapsSolid(Vector2 center, Vector2 halfSize)
    {
        int minX = ToTile(center.X - halfSize.X);
        int maxX = ToTile(center.X + halfSize.X - 0.01f);
        int minY = ToTile(center.Y - halfSize.Y);
        int maxY = ToTile(center.Y + halfSize.Y - 0.01f);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (IsSolid(x, y))
                    return true;
            }
        }

        return false;
    }

    /// <summary>Tile em que o ponto cai, se houver algo lá (bloco ou líquido).</summary>
    public int TileAt(Vector2 world)
    {
        var (x, y) = WorldToTile(world);
        return Get(x, y);
    }

    // ---------------------------------------------------------------------
    //  Mineração
    // ---------------------------------------------------------------------

    /// <summary>Dano acumulado no tile, em segundos de picareta (0 = intacto).</summary>
    public float DamageAt(int x, int y) => _damage.GetValueOrDefault(y * Width + x, 0f);

    /// <summary>
    /// Aplica progresso de mineração. Devolve o item dropado quando o bloco quebra,
    /// ou <see cref="Items.ItemIds.None"/> enquanto ainda estiver inteiro.
    /// </summary>
    public int MineTile(int x, int y, float amount)
    {
        if (TileDb.Get(Get(x, y)) is not { } def || def.Hardness < 0f)
            return Items.ItemIds.None;

        int index = y * Width + x;
        float progress = _damage.GetValueOrDefault(index, 0f) + amount;

        if (progress < def.Hardness)
        {
            _damage[index] = progress;
            return Items.ItemIds.None;
        }

        SetTile(x, y, TileId.Empty);
        return def.Drop;
    }

    /// <summary>Esquece o progresso de mineração (soltou o botão, mirou outro bloco).</summary>
    public void ClearDamage(int x, int y) => _damage.Remove(y * Width + x);

    public void ClearAllDamage() => _damage.Clear();

    // ---------------------------------------------------------------------
    //  Cache do céu
    // ---------------------------------------------------------------------

    public void RebuildSkyTop()
    {
        for (int x = 0; x < Width; x++)
        {
            _skyTop[x] = Height;
            for (int y = 0; y < Height; y++)
            {
                if (BlocksLight(x, y))
                {
                    _skyTop[x] = y;
                    break;
                }
            }
        }
    }

    private void UpdateSkyTop(int x, int y, int previous, int current)
    {
        bool blockedBefore = TileDb.BlocksLight(previous);
        bool blocksNow = TileDb.BlocksLight(current);
        if (blockedBefore == blocksNow)
            return;

        if (blocksNow)
        {
            if (y < _skyTop[x])
                _skyTop[x] = y;                            // tapou o buraco mais alto
            return;
        }

        if (y != _skyTop[x])
            return;                                        // abriu um bloco que já estava na sombra

        // Cavou justamente o teto da coluna: procura o próximo bloqueio abaixo.
        _skyTop[x] = Height;
        for (int ny = y + 1; ny < Height; ny++)
        {
            if (BlocksLight(x, ny))
            {
                _skyTop[x] = ny;
                break;
            }
        }
    }
}
