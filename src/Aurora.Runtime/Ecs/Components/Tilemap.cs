using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Grade de tiles desenhada a partir de um tileset (textura fatiada em células).
/// Índices correm da esquerda para a direita, de cima para baixo; -1 = vazio.
/// A posição do Transform é o canto superior esquerdo da grade.
/// </summary>
public sealed class Tilemap : IComponent
{
    public Texture2D? Tileset;
    public int TileWidth = 16;
    public int TileHeight = 16;

    /// <summary>Dimensões da grade, em tiles.</summary>
    public int Width;
    public int Height;

    /// <summary>Ordem de desenho, mesma escala dos SpriteRenderers.</summary>
    public int Layer;

    /// <summary>
    /// Tinta multiplicativa da camada inteira. Branco = tileset como está; alpha &lt; 1
    /// deixa a camada translúcida (água rasa deixando ver a areia embaixo), e uma cor
    /// escura serve pra afundar a camada no clima da cena sem repintar o PNG.
    /// </summary>
    public Color Color = Color.White;

    /// <summary>Width*Height índices; -1 = célula vazia.</summary>
    public int[] Tiles = [];

    /// <summary>
    /// Índices de tile que bloqueiam movimento (colisão sólida).
    /// Ex.: {1, 3} = tile 1 e tile 3 são paredes.
    /// Vazio = nenhuma célula bloqueia (tilemap decorativo).
    /// </summary>
    public HashSet<int> SolidTiles = [];

    // ---- Animação de tiles (água, lava, sangue, tochas) -------------------
    //
    // Modelo de linhas: a primeira linha do tileset guarda os tiles "de verdade" e cada
    // linha seguinte é o mesmo conjunto no frame seguinte. Assim o tile N vira
    // N + Columns*frame na hora de desenhar, e a cena só precisa guardar três números em
    // vez de uma tabela de frames por tile.

    /// <summary>
    /// Quantas linhas de frame o tileset tem. 1 (padrão) = tiles estáticos.
    /// Só os tiles da primeira linha animam — os índices das linhas de baixo são os frames
    /// deles e continuam desenháveis como tile fixo.
    /// </summary>
    public int AnimationFrames = 1;

    /// <summary>Duração de cada frame em segundos.</summary>
    public float AnimationFrameDuration = 0.15f;

    /// <summary>Largura de uma linha de frames, em tiles. 0 = <see cref="TilesPerRow"/> (linha cheia).</summary>
    public int AnimationColumns;

    /// <summary>Relógio da animação, avançado por <c>World.Update</c>. Público pra dar pra
    /// zerar/sincronizar (replay, multiplayer) sem depender do tempo de vida da cena.</summary>
    public float AnimationTime;

    public int TilesPerRow => Tileset is null || TileWidth <= 0
        ? 1
        : Math.Max(1, Tileset.Width / TileWidth);

    /// <summary>Colunas efetivas da animação (resolve o 0 = linha cheia).</summary>
    public int EffectiveAnimationColumns => AnimationColumns > 0 ? AnimationColumns : TilesPerRow;

    public int GetTile(int x, int y)
        => x >= 0 && y >= 0 && x < Width && y < Height && Tiles.Length == Width * Height
            ? Tiles[y * Width + x]
            : -1;

    public void SetTile(int x, int y, int index)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            return;

        EnsureSize();
        Tiles[y * Width + x] = index;
    }

    /// <summary>Garante Tiles com Width*Height células (novas nascem vazias).</summary>
    public void EnsureSize()
    {
        int expected = Math.Max(0, Width * Height);
        if (Tiles.Length == expected)
            return;

        var resized = new int[expected];
        Array.Fill(resized, -1);
        Array.Copy(Tiles, resized, Math.Min(Tiles.Length, expected));
        Tiles = resized;
    }

    /// <summary>
    /// Índice realmente desenhado para <paramref name="index"/> no instante atual: o próprio
    /// índice quando não há animação, ou o frame correspondente da linha de baixo quando o
    /// tile está na primeira linha do tileset.
    /// </summary>
    public int ResolveTile(int index)
    {
        if (AnimationFrames <= 1 || AnimationFrameDuration <= 0f || index < 0)
            return index;

        int columns = EffectiveAnimationColumns;
        if (index >= columns)
            return index; // já é um frame (linha de baixo) — desenha como tile fixo

        int frame = (int)(AnimationTime / AnimationFrameDuration) % AnimationFrames;
        return index + columns * frame;
    }

    /// <summary>
    /// Recalcula o índice de cada célula não-vazia a partir dos quatro vizinhos, no esquema
    /// de bitmask N=1, L=2, S=4, O=8 — bit ligado = o vizinho é da mesma camada (não tem
    /// borda daquele lado). Pinte a lagoa com qualquer índice ≥ 0 e chame isto: cada célula
    /// vira <paramref name="firstIndex"/> + máscara, que é exatamente o layout do
    /// <see cref="Graphics.LiquidTileset"/> (16 colunas de máscara).
    /// </summary>
    /// <param name="outsideIsFilled">True (padrão): fora do mapa conta como preenchido — um
    /// oceano que encosta na borda não ganha espuma ali. False: a borda do mapa vira margem.</param>
    public void Autotile(int firstIndex = 0, bool outsideIsFilled = true)
    {
        EnsureSize();
        if (Width <= 0 || Height <= 0)
            return;

        // A máscara tem que ser lida do estado ORIGINAL: escrever direto em Tiles faria a
        // célula já reindexada virar entrada da máscara da vizinha e a lagoa "vazaria".
        var source = (int[])Tiles.Clone();

        bool Filled(int x, int y)
            => x < 0 || y < 0 || x >= Width || y >= Height
                ? outsideIsFilled
                : source[y * Width + x] >= 0;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                if (source[i] < 0)
                    continue;

                int mask = 0;
                if (Filled(x, y - 1)) mask |= 1;
                if (Filled(x + 1, y)) mask |= 2;
                if (Filled(x, y + 1)) mask |= 4;
                if (Filled(x - 1, y)) mask |= 8;

                Tiles[i] = firstIndex + mask;
            }
        }
    }

    /// <summary>Preenche um retângulo de células com o mesmo índice (atalho pra montar lagoa/rio em código).</summary>
    public void Fill(int x, int y, int width, int height, int index)
    {
        EnsureSize();
        for (int cy = y; cy < y + height; cy++)
            for (int cx = x; cx < x + width; cx++)
                SetTile(cx, cy, index);
    }

    /// <summary>Recorte do tileset para um índice de tile.</summary>
    public RectF SourceRect(int index)
    {
        int perRow = TilesPerRow;
        return new RectF(index % perRow * TileWidth, index / perRow * TileHeight, TileWidth, TileHeight);
    }
}
