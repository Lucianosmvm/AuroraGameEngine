using Avalonia.Media;

namespace Aurora.Editor.Models;

/// <summary>Uma cor da paleta: nome em português, hex do engine e o pincel do quadradinho.</summary>
public sealed class ColorSwatch
{
    public ColorSwatch(string name, string hex)
    {
        Name = name;
        Hex = hex.StartsWith('#') ? hex : "#" + hex;
        Color = EngineColor.Parse(Hex, Colors.Magenta);

        // Um swatch pode trazer a própria opacidade (a "Sombra", por exemplo). Quando traz só
        // 6 dígitos, escolher a cor preserva a opacidade que o campo já tinha.
        CarriesAlpha = Hex.Length == 9;
        Brush = new SolidColorBrush(Color);
    }

    public string Name { get; }
    public string Hex { get; }
    public Color Color { get; }
    public bool CarriesAlpha { get; }
    public IBrush Brush { get; }

    /// <summary>Texto da dica ao passar o mouse.</summary>
    public string Label => $"{Name}   {EngineColor.ToHex(Color)}";
}

/// <summary>
/// Paleta fixa do editor: o jeito de escolher cor sem saber hexadecimal. São 40 cores
/// nomeadas em 5 faixas (neutros, quentes, verdes, frios, cores de cena), suficientes para
/// montar HUD e cenário sem sair do editor — quem já sabe o hex continua digitando no campo
/// ao lado.
/// </summary>
public static class ColorPalette
{
    public static readonly IReadOnlyList<ColorSwatch> Swatches =
    [
        // Neutros
        new("Branco", "#FFFFFF"),
        new("Cinza claro", "#C8CDD4"),
        new("Cinza", "#8A9099"),
        new("Cinza escuro", "#4A4F57"),
        new("Grafite", "#22262C"),
        new("Preto", "#000000"),
        new("Bege", "#E8DCC0"),
        new("Creme", "#FFF3D6"),

        // Quentes
        new("Vermelho", "#E23B3B"),
        new("Vermelho escuro", "#A11E1E"),
        new("Coral", "#FF7F62"),
        new("Laranja", "#F2871E"),
        new("Âmbar", "#FFB300"),
        new("Amarelo", "#FFE94A"),
        new("Dourado", "#E8C44A"),
        new("Rosa", "#FF7BA9"),

        // Verdes
        new("Lima", "#B4E24B"),
        new("Verde", "#46B04A"),
        new("Verde escuro", "#237A2C"),
        new("Esmeralda", "#24B98A"),
        new("Musgo", "#6E8B3D"),
        new("Turquesa", "#26C6C6"),
        new("Ciano", "#4DD0E1"),
        new("Menta", "#A8E6CF"),

        // Frios
        new("Azul claro", "#64B5F6"),
        new("Azul", "#2D6CDF"),
        new("Azul escuro", "#1A3E8C"),
        new("Anil", "#4C4FC4"),
        new("Roxo", "#8E44D0"),
        new("Violeta", "#B06BE8"),
        new("Magenta", "#E040A8"),
        new("Vinho", "#7B1E3C"),

        // Cores de cena
        new("Pele", "#F2C9A0"),
        new("Marrom", "#7C5436"),
        new("Terra", "#8B5A2B"),
        new("Pedra", "#9AA0A6"),
        new("Céu", "#7EC8F2"),
        new("Água", "#2E86DE"),
        new("Fogo", "#FF6B2C"),
        new("Sombra", "#00000080"),
    ];

    /// <summary>Nome da cor da paleta igual a esta (ignorando opacidade), ou null.</summary>
    public static string? NameOf(Color color)
    {
        foreach (var swatch in Swatches)
        {
            if (swatch.Color.R == color.R && swatch.Color.G == color.G && swatch.Color.B == color.B)
                return swatch.Name;
        }

        return null;
    }
}
