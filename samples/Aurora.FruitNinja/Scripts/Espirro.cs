using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Scenes;

namespace FruitNinja;

/// <summary>
/// O esguicho de suco do corte.
///
/// <para>O <c>ParticleEmitter</c> da engine é um emissor CONTÍNUO (partículas por segundo) e
/// não tem "solte N de uma vez". Este script é o adaptador: deixa emitir por um piscar de olhos,
/// desliga a emissão e só então destrói a entidade — desligar e destruir no mesmo instante
/// cortaria as gotas no ar, que é justamente o efeito que se quer ver.</para>
/// </summary>
[SceneScript]
public sealed class Espirro : Behavior
{
    /// <summary>Quanto tempo o emissor fica ligado. Curto de propósito: é um estouro, não uma
    /// fonte de água.</summary>
    public float TempoEmitindo = 0.05f;

    /// <summary>Quanto tempo a entidade existe. Precisa cobrir a vida da última gota.</summary>
    public float TempoTotal = 1.1f;

    private float _idade;

    public override void Update(float deltaTime)
    {
        _idade += deltaTime;

        if (_idade >= TempoEmitindo && Get<ParticleEmitter>() is { } emissor)
            emissor.Emitting = false;

        if (_idade >= TempoTotal)
            Entity.Destroy();
    }

    /// <summary>Solta um estouro de gotas na cor da fruta.</summary>
    public static void Criar(World world, Vector2 posicao, Color cor, int gotas, float forca = 1f)
    {
        if (world.Assets is null)
            return;

        var entidade = world.CreateEntity("Espirro");
        entidade.Add(new Transform(posicao));
        entidade.Add(new ParticleEmitter
        {
            Texture = world.Assets.LoadTexture("sprites/particula.png"),
            Rate = gotas / 0.05f,        // as `gotas` inteiras dentro do TempoEmitindo
            MaxParticles = gotas * 2,
            LifeMin = 0.30f,
            LifeMax = 0.75f,
            SpeedMin = 120f * forca,
            SpeedMax = 460f * forca,
            AngleMin = 0f,
            AngleMax = 360f,
            SizeStart = 16f * forca,
            SizeEnd = 2f,
            ColorStart = cor,
            ColorEnd = cor.WithAlpha(0f),
            Gravity = new Vector2(0f, Arena.Gravidade * 0.85f),
            Layer = 14,
        });
        entidade.Add(new Espirro());
    }
}
