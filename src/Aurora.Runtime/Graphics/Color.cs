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

    public static readonly Color White = new(1f, 1f, 1f);
    public static readonly Color Black = new(0f, 0f, 0f);
    public static readonly Color Transparent = new(0f, 0f, 0f, 0f);
    public static readonly Color Red = new(1f, 0f, 0f);
    public static readonly Color Green = new(0f, 1f, 0f);
    public static readonly Color Blue = new(0f, 0f, 1f);
    public static readonly Color Yellow = new(1f, 1f, 0f);
    public static readonly Color CornflowerBlue = FromBytes(100, 149, 237);
}
