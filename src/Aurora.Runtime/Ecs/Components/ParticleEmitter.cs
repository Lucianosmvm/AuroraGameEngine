using System.Numerics;
using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>Uma partícula individual — estado de simulação, não serializado (o emissor é o componente da cena).</summary>
internal struct Particle
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float Age;
    public float LifeTime;
}

/// <summary>
/// Emissor de partículas: fumaça, faíscas, folhas caindo — qualquer efeito de "várias
/// sprites pequenas nascendo, se movendo e desaparecendo". Sem textura, desenha um quad
/// colorido (mesmo pixel branco do SpriteBatch.DrawRect); com textura, desenha a sprite.
/// Cor e tamanho interpolam de Start pra End ao longo da vida da partícula.
/// </summary>
public sealed class ParticleEmitter : IComponent
{
    public Texture2D? Texture;

    /// <summary>Partículas emitidas por segundo.</summary>
    public float Rate = 10f;

    /// <summary>Liga/desliga a emissão em runtime sem remover o componente. Partículas já vivas continuam até morrer.</summary>
    public bool Emitting = true;

    public float LifeMin = 0.6f;
    public float LifeMax = 1.2f;

    public float SpeedMin = 20f;
    public float SpeedMax = 60f;

    /// <summary>Ângulo de emissão em graus (0 = direita, 90 = baixo — mesma convenção de Transform.Rotation).</summary>
    public float AngleMin = 0f;
    public float AngleMax = 360f;

    public float SizeStart = 8f;
    public float SizeEnd = 0f;

    public Color ColorStart = Color.White;
    public Color ColorEnd = Color.White.WithAlpha(0f);

    public Vector2 Gravity;

    public int Layer;

    /// <summary>Limite de partículas vivas ao mesmo tempo — protege contra emissor mal configurado.</summary>
    public int MaxParticles = 200;

    // Estado de simulação (não serializado).
    internal readonly List<Particle> Particles = [];
    internal float SpawnAccumulator;
}
