namespace Aurora.Runtime.Graphics;

/// <summary>Cor RGBA com componentes normalizados (0..1).</summary>
public readonly struct Color
{
    public readonly float R;
    public readonly float G;
    public readonly float B;
    public readonly float A;

    public Color(float r, float g, float b, float a = 1f)
    {
        R = r; G = g; B = b; A = a;
    }

    public static Color FromBytes(byte r, byte g, byte b, byte a = 255)
        => new(r / 255f, g / 255f, b / 255f, a / 255f);

    public Color WithAlpha(float alpha) => new(R, G, B, alpha);

    /// <summary>Aceita "#RRGGBB" ou "#RRGGBBAA" (usado no JSON de cenas).</summary>
    public static Color FromHex(string hex)
    {
        string value = hex.TrimStart('#');
        if (value.Length != 6 && value.Length != 8)
            throw new FormatException($"Cor hex inválida: '{hex}'. Use #RRGGBB ou #RRGGBBAA.");

        byte r = Convert.ToByte(value[..2], 16);
        byte g = Convert.ToByte(value[2..4], 16);
        byte b = Convert.ToByte(value[4..6], 16);
        byte a = value.Length == 8 ? Convert.ToByte(value[6..8], 16) : (byte)255;
        return FromBytes(r, g, b, a);
    }

    public string ToHex()
        => $"#{(byte)(R * 255):X2}{(byte)(G * 255):X2}{(byte)(B * 255):X2}{(byte)(A * 255):X2}";

    public static readonly Color White = new(1f, 1f, 1f);
    public static readonly Color Black = new(0f, 0f, 0f);
    public static readonly Color Transparent = new(0f, 0f, 0f, 0f);
    public static readonly Color Red = new(1f, 0f, 0f);
    public static readonly Color Green = new(0f, 1f, 0f);
    public static readonly Color Blue = new(0f, 0f, 1f);
    public static readonly Color Yellow = new(1f, 1f, 0f);
    public static readonly Color CornflowerBlue = FromBytes(100, 149, 237);
}
