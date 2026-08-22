using Avalonia.Media;

namespace Aurora.Editor.Models;

/// <summary>
/// Conversão entre o hex do engine e <see cref="Color"/> do Avalonia.
///
/// <para>Atenção à ordem: o engine grava <c>#RRGGBBAA</c> (alpha por último, como no
/// <c>Color.FromHex</c> do runtime) e o Avalonia lê <c>#AARRGGBB</c>. Passar a string de um
/// para o outro sem converter troca canal e pinta errado — por isso tudo que mexe em cor no
/// editor passa por aqui.</para>
/// </summary>
public static class EngineColor
{
    /// <summary>Lê "#RRGGBB" ou "#RRGGBBAA" (com ou sem "#"). Devolve o fallback se não der.</summary>
    public static Color Parse(string? hex, Color fallback)
    {
        if (!TryParse(hex, out var color))
            return fallback;

        return color;
    }

    public static bool TryParse(string? hex, out Color color)
    {
        color = Colors.White;

        if (string.IsNullOrWhiteSpace(hex))
            return false;

        string value = hex.Trim().TrimStart('#');
        if (value.Length != 6 && value.Length != 8)
            return false;

        try
        {
            byte r = Convert.ToByte(value[..2], 16);
            byte g = Convert.ToByte(value[2..4], 16);
            byte b = Convert.ToByte(value[4..6], 16);
            byte a = value.Length == 8 ? Convert.ToByte(value[6..8], 16) : (byte)255;
            color = Color.FromArgb(a, r, g, b);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Formata no padrão do engine: "#RRGGBBAA", sempre com os 8 dígitos.</summary>
    public static string ToHex(Color color)
        => $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";

    /// <summary>Mesma cor com outra opacidade.</summary>
    public static string WithAlpha(Color color, byte alpha)
        => ToHex(Color.FromArgb(alpha, color.R, color.G, color.B));

    public static IBrush Brush(Color color) => new SolidColorBrush(color);
}
