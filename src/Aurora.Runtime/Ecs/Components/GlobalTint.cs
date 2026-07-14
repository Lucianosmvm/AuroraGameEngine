using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Tinta multiplicativa sobre a cena inteira (não afetada pela câmera) — dia/noite,
/// tempestade escurecendo a tela, filtro subaquático etc. Só o Color/Intensity de uma
/// entidade "ativa" por vez importam; a Transform da entidade é ignorada.
/// Desenhado depois dos sprites/partículas/luzes, antes do overlay de fade de cena.
/// </summary>
public sealed class GlobalTint : IComponent
{
    public Color Color = Color.FromBytes(0, 0, 40);

    /// <summary>0 = sem efeito, 1 = cor sólida.</summary>
    public float Intensity = 0.3f;

    public bool Enabled = true;
}
