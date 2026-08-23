namespace Aurora.Runtime.Graphics;

/// <summary>
/// Receita visual de um líquido para o <see cref="LiquidTileset"/>: as quatro cores
/// (fundo, superfície, brilho de crista e borda) mais a forma da onda e da margem.
/// Os presets (<see cref="Water"/>, <see cref="Lava"/>, <see cref="Blood"/>,
/// <see cref="Swamp"/>) já vêm calibrados — mexa nas cores pra variar o bioma
/// (água tropical, pântano roxo de veneno) sem tocar no algoritmo.
/// </summary>
public sealed class LiquidStyle
{
    /// <summary>Lado do tile em pixels. Use o mesmo TileWidth/TileHeight do Tilemap.</summary>
    public int TileSize = 32;

    /// <summary>Quantos frames o ciclo tem. 1 = tileset estático. 4–8 é o normal.</summary>
    public int Frames = 4;

    /// <summary>Cor do fundo do líquido (vales da onda).</summary>
    public Color Deep = Color.FromBytes(26, 82, 150);

    /// <summary>Cor da superfície (cristas da onda).</summary>
    public Color Shallow = Color.FromBytes(58, 138, 206);

    /// <summary>Brilho no topo da crista — o "reflexo" que faz a água parecer molhada.</summary>
    public Color Crest = Color.FromBytes(150, 214, 240);

    /// <summary>Borda contra a terra: espuma na água, crosta escura na lava.</summary>
    public Color Edge = Color.FromBytes(226, 245, 255);

    /// <summary>Largura da borda como fração do tile (0.18 = ~6px num tile de 32).</summary>
    public float EdgeWidth = 0.14f;

    /// <summary>Multiplicador do contraste da onda. &lt;1 achata, &gt;1 marca mais.</summary>
    public float Contrast = 1f;

    /// <summary>Opacidade do corpo do líquido (0–1). Abaixo de 1 deixa ver a camada de baixo.</summary>
    public float Opacity = 1f;

    /// <summary>
    /// Raio do arredondamento do canto, em fração do tile — só nos cantos onde os dois lados
    /// vizinhos são terra. O que sobra fora do arco fica transparente, então a lagoa lê como
    /// mancha orgânica em cima do terreno. 0 = canto quadrado.
    /// </summary>
    public float CornerRadius = 0.28f;

    /// <summary>Frequências da onda no tile (inteiras de propósito: garantem que tile vizinho
    /// costure sem emenda).</summary>
    public int WavesX = 2;
    public int WavesY = 3;

    /// <summary>Semente do granulado fino sobreposto à onda.</summary>
    public int Seed = 1;

    /// <summary>Água de rio/lagoa/mar — azul com espuma clara na margem.</summary>
    public static LiquidStyle Water() => new();

    /// <summary>Água rasa/tropical: mais clara e translúcida, pra desenhar por cima da areia.</summary>
    public static LiquidStyle ShallowWater() => new()
    {
        Deep = Color.FromBytes(52, 146, 190),
        Shallow = Color.FromBytes(104, 196, 224),
        Crest = Color.FromBytes(196, 240, 250),
        Edge = Color.FromBytes(236, 250, 255),
        Opacity = 0.72f,
        Contrast = 0.85f,
    };

    /// <summary>Lava — a "espuma" vira crosta de rocha escura e a crista brilha em amarelo.</summary>
    public static LiquidStyle Lava() => new()
    {
        Deep = Color.FromBytes(122, 26, 8),
        Shallow = Color.FromBytes(214, 84, 18),
        Crest = Color.FromBytes(255, 202, 78),
        Edge = Color.FromBytes(48, 26, 24),
        EdgeWidth = 0.20f,
        Contrast = 1.25f,
        WavesX = 1,
        WavesY = 2,
        Seed = 7,
    };

    /// <summary>Poça de sangue — escura, quase sem crista, borda mais escura que o miolo.</summary>
    public static LiquidStyle Blood() => new()
    {
        Deep = Color.FromBytes(72, 8, 14),
        Shallow = Color.FromBytes(132, 18, 26),
        Crest = Color.FromBytes(176, 38, 44),
        Edge = Color.FromBytes(46, 6, 10),
        EdgeWidth = 0.14f,
        Contrast = 0.7f,
        CornerRadius = 0.40f,
        WavesX = 1,
        WavesY = 1,
        Seed = 13,
    };

    /// <summary>Pântano/veneno — verde turvo, borda de limo.</summary>
    public static LiquidStyle Swamp() => new()
    {
        Deep = Color.FromBytes(30, 60, 34),
        Shallow = Color.FromBytes(78, 118, 52),
        Crest = Color.FromBytes(142, 178, 80),
        Edge = Color.FromBytes(56, 84, 44),
        Contrast = 0.8f,
        Seed = 21,
    };
}
