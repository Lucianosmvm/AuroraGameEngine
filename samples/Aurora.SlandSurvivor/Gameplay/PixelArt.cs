using Aurora.Runtime.Graphics;
using Aurora.SlandSurvivor.Worlds;
using Silk.NET.OpenGL;

namespace Aurora.SlandSurvivor.Gameplay;

/// <summary>
/// Sprites do jogo desenhados em código: cada personagem é um mapa de caracteres com uma
/// paleta, convertido em textura RGBA no carregamento. Mesma ideia do tileset — o jogo roda
/// sem nenhum arquivo de arte, e trocar uma cor é editar uma letra aqui.
/// </summary>
public static class PixelArt
{
    private static readonly string[] PlayerRows =
    [
        "....HHHH....",
        "...HHHHHH...",
        "..HHSSSSHH..",
        "..HSSSSSSH..",
        "..HSEISSESH.",
        "..HSSSSSSH..",
        "...SSSSSS...",
        "...CCCCCC...",
        "..WWWWWWWW..",
        ".SWWWWWWWWS.",
        ".SWWWWWWWWS.",
        ".SWWWWWWWWS.",
        "..WWWWWWWW..",
        "..WWWWWWWW..",
        "...LLLLLL...",
        "...LLLLLL...",
        "...LL..LL...",
        "...LL..LL...",
        "...LL..LL...",
        "...LL..LL...",
        "..TTT..TTT..",
        "..TTT..TTT..",
    ];

    private static readonly string[] SlimeRows =
    [
        "................",
        ".....GGGGGG.....",
        "...GGGGGGGGGG...",
        "..GGGGGGGGGGGG..",
        "..GGEEGGGGEEGG..",
        "..GGEEGGGGEEGG..",
        ".GGGGGGGGGGGGGG.",
        ".GGGGGGGGGGGGGG.",
        "GGGGGGGGGGGGGGGG",
        "GGGGGGGGGGGGGGGG",
        ".GGGGGGGGGGGGGG.",
        "..GGGGGGGGGGGG..",
    ];

    private static readonly string[] BatRows =
    [
        "................",
        "..W..........W..",
        ".WWW..BBBB..WWW.",
        "WWWWWBBBBBBWWWWW",
        ".WWWWBEBBEBWWWW.",
        "..WWWBBBBBBWWW..",
        "....W.BBBB.W....",
        "......BFFB......",
        "................",
        "................",
    ];

    private static readonly string[] ZombieRows =
    [
        "....HHHH....",
        "...HHHHHH...",
        "..HHGGGGHH..",
        "..HGGGGGGH..",
        "..HGEGGGEGH.",
        "..HGGGGGGH..",
        "...GGGGGG...",
        "...CCCCCC...",
        "..RRRRRRRR..",
        "GGRRRRRRRRGG",
        "GGRRRRRRRRGG",
        ".GRRRRRRRRG.",
        "..RRRRRRRR..",
        "..RRRRRRRR..",
        "...LLLLLL...",
        "...LLLLLL...",
        "...LL..LL...",
        "...LL..LL...",
        "...LL..LL...",
        "...LL..LL...",
        "..TT....TT..",
        "..TT....TT..",
    ];

    private static readonly Dictionary<char, Rgb> Palette = new()
    {
        ['H'] = new Rgb(88, 56, 34),      // cabelo
        ['S'] = new Rgb(232, 190, 152),   // pele
        ['E'] = new Rgb(24, 24, 32),      // olho
        ['I'] = new Rgb(240, 240, 250),   // brilho do olho
        ['C'] = new Rgb(190, 150, 118),   // gola
        ['W'] = new Rgb(64, 108, 178),    // camisa
        ['L'] = new Rgb(58, 62, 92),      // calça
        ['T'] = new Rgb(72, 52, 38),      // bota
        ['G'] = new Rgb(108, 190, 120),   // pele de zumbi / gosma
        ['R'] = new Rgb(96, 78, 56),      // roupa rasgada
        ['B'] = new Rgb(74, 60, 92),      // corpo de morcego
        ['F'] = new Rgb(226, 226, 236),   // presas
    };

    public static Texture2D Player(GL gl) => Build(gl, PlayerRows, new Rgb(112, 168, 240));

    public static Texture2D Slime(GL gl) => Build(gl, SlimeRows, new Rgb(96, 176, 232), alpha: 225);

    public static Texture2D Bat(GL gl) => Build(gl, BatRows, new Rgb(120, 96, 148));

    public static Texture2D Zombie(GL gl) => Build(gl, ZombieRows, new Rgb(108, 190, 120));

    /// <param name="tint">Cor usada para o caractere 'G' (gosma/pele), que muda por inimigo.</param>
    private static Texture2D Build(GL gl, string[] rows, Rgb tint, byte alpha = 255)
    {
        int height = rows.Length;
        int width = rows[0].Length;
        var pixels = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            string row = rows[y];

            for (int x = 0; x < width && x < row.Length; x++)
            {
                char c = row[x];
                if (c == '.')
                    continue;

                var color = c == 'G' ? tint : Palette.GetValueOrDefault(c, new Rgb(255, 0, 255));

                // Sombreado barato: a metade de baixo de cada sprite escurece um pouco,
                // o que dá volume sem precisar pintar mais um tom na tabela.
                if (y > height * 0.6f)
                    color = color.Scale(0.86f);

                int i = (y * width + x) * 4;
                pixels[i + 0] = color.R;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.B;
                pixels[i + 3] = alpha;
            }
        }

        return Texture2D.FromPixels(gl, width, height, pixels);
    }
}
