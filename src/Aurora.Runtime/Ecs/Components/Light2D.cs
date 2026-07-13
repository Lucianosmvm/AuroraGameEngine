using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Fonte de luz 2D — desenha um glow (brilho aditivo) centrado no Transform da entidade.
/// Não é oclusão/sombra dinâmica (isso exigiria um passe de framebuffer separado); é um
/// efeito de brilho somado à cena, suficiente pra tocha, lanterna, brasa, luz mágica etc.
/// </summary>
public sealed class Light2D : IComponent
{
    /// <summary>Raio do glow em pixels.</summary>
    public float Radius = 100f;

    public Color Color = Color.FromBytes(255, 220, 150);

    /// <summary>Multiplicador de brilho — acima de 1 satura mais rápido ao centro.</summary>
    public float Intensity = 1f;

    public bool Enabled = true;
}
