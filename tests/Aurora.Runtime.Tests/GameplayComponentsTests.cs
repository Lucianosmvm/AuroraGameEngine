using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Componentes de jogo prontos — os que existem pra um jogo comum não precisar de script.
/// O que se prende aqui é o CONTRATO que o autor de cena vê no inspector: o que cada campo faz
/// e o que acontece nos limites (zero, alvo morto, contato que dura).
/// </summary>
public class GameplayComponentsTests
{
    private const float Tolerance = 0.01f;

    /// <summary>Roda N frames de <paramref name="step"/> segundos — behaviors só ganham Start e
    /// Update por dentro do World.Update, e destruições só são drenadas no fim dele.</summary>
    private static void Advance(World world, int frames = 1, float step = 1f / 60f)
    {
        for (int i = 0; i < frames; i++)
            world.Update(step);
    }

    // ---------- Lifetime ----------

    [Fact]
    public void LifetimeDestroiPorTempo()
    {
        var world = new World();
        var entity = world.CreateEntity("Efeito");
        entity.Add(new Transform());
        entity.Add(new Lifetime { Seconds = 0.5f });

        Advance(world, frames: 20, step: 0.02f);   // 0.4s
        Assert.True(entity.IsAlive);

        Advance(world, frames: 10, step: 0.02f);   // 0.6s
        Assert.False(entity.IsAlive);
    }

    [Fact]
    public void LifetimeComSecondsZeroNaoMorreSozinho()
    {
        // 0 = "sem limite de tempo". Se virasse "morre já", todo efeito que depende só do fim da
        // animação sumiria no primeiro frame.
        var world = new World();
        var entity = world.CreateEntity("Efeito");
        entity.Add(new Transform());
        entity.Add(new Lifetime { Seconds = 0f, DestroyOnAnimationEnd = true });

        Advance(world, frames: 120);

        Assert.True(entity.IsAlive);
    }

    // ---------- FollowTarget ----------

    [Fact]
    public void FollowTargetGrudaNoAlvoComOffset()
    {
        var world = new World();
        world.CreateEntity("Player").Add(new Transform(new Vector2(100f, 50f)));

        var effect = world.CreateEntity("Corte");
        effect.Add(new Transform());
        effect.Add(new FollowTarget { TargetName = "Player", OffsetX = 10f, OffsetY = -4f });

        Advance(world);

        var position = effect.Get<Transform>()!.Position;
        Assert.Equal(110f, position.X, Tolerance);
        Assert.Equal(46f, position.Y, Tolerance);
    }

    [Fact]
    public void FollowTargetComVelocidadeNaoPassaDoAlvo()
    {
        // Sem limitar o passo pela distância que falta, FollowSpeed alto ultrapassa o alvo e a
        // entidade fica tremendo em volta dele em vez de parar.
        var world = new World();
        world.CreateEntity("Player").Add(new Transform(new Vector2(10f, 0f)));

        var pet = world.CreateEntity("Pet");
        pet.Add(new Transform());
        pet.Add(new FollowTarget { TargetName = "Player", FollowSpeed = 10_000f });

        Advance(world);

        Assert.Equal(10f, pet.Get<Transform>()!.Position.X, Tolerance);
    }

    [Fact]
    public void FollowTargetDestroiJuntoQuandoOAlvoSome()
    {
        var world = new World();
        var player = world.CreateEntity("Player");
        player.Add(new Transform());

        var effect = world.CreateEntity("Corte");
        effect.Add(new Transform());
        effect.Add(new FollowTarget { TargetName = "Player", DestroyWhenTargetGone = true });

        Advance(world);
        Assert.True(effect.IsAlive);

        player.Destroy();
        Advance(world);

        Assert.False(effect.IsAlive);
    }

    // ---------- AutoMotion ----------

    [Fact]
    public void AutoMotionGiraEmGrausPorSegundo()
    {
        // O campo é em graus porque é o que se autora à mão; Transform.Rotation é radianos.
        var world = new World();
        var coin = world.CreateEntity("Moeda");
        coin.Add(new Transform());
        coin.Add(new AutoMotion { RotateSpeedDegrees = 180f });

        Advance(world, frames: 100, step: 0.01f);   // 1s

        Assert.Equal(MathF.PI, coin.Get<Transform>()!.Rotation, 0.05f);
    }

    [Fact]
    public void AutoMotionBalancaEmTornoDaPosicaoOriginal()
    {
        // Um ciclo completo tem que voltar ao ponto de partida. Somando deslocamento por frame
        // em vez de calcular a partir da origem, o erro acumula e o item vai derivando.
        var world = new World();
        var item = world.CreateEntity("Item");
        item.Add(new Transform(new Vector2(7f, 3f)));
        item.Add(new AutoMotion { BobAmplitude = 20f, BobSpeed = 1f });

        Advance(world, frames: 1000, step: 0.001f);  // 1s = 1 ciclo

        var position = item.Get<Transform>()!.Position;
        Assert.Equal(7f, position.X, 0.1f);
        Assert.Equal(3f, position.Y, 0.1f);
    }

    // ---------- ContactDamage ----------

    /// <summary>Dois colisores sobrepostos na origem: um agressor e um alvo com Health.</summary>
    private static (World World, Entity Attacker, Entity Target) BuildContact(
        ContactDamage damage, string targetName = "Player", bool solid = false)
    {
        var world = new World();

        var attacker = world.CreateEntity("Espinho");
        attacker.Add(new Transform());
        attacker.Add(new Collider { Width = 16f, Height = 16f, IsSolid = solid });
        attacker.Add(damage);

        var target = world.CreateEntity(targetName);
        target.Add(new Transform());
        target.Add(new Collider { Width = 16f, Height = 16f, IsSolid = solid });
        target.Add(new Health { Max = 100f, Current = 100f });

        return (world, attacker, target);
    }

    [Fact]
    public void ContactDamageMachucaNoContato()
    {
        var (world, _, target) = BuildContact(new ContactDamage { Damage = 25f, Interval = 1f });

        Advance(world);

        Assert.Equal(75f, target.Get<Health>()!.Current, Tolerance);
    }

    [Fact]
    public void ContactDamageRepeteNoIntervaloEnquantoEncostado()
    {
        // Este é o caso que OnTriggerEnter sozinho não cobre: o alvo nunca sai, então só o
        // acompanhamento por frame faz o segundo golpe sair.
        var (world, _, target) = BuildContact(new ContactDamage { Damage = 10f, Interval = 0.5f });

        // O intervalo conta a partir do golpe, não do início da cena: o primeiro sai no frame do
        // contato (t=0.05) e o segundo em t=0.55, então 0.6s cobre exatamente dois.
        Advance(world, frames: 12, step: 0.05f);

        Assert.Equal(80f, target.Get<Health>()!.Current, Tolerance);
    }

    [Fact]
    public void ContactDamageComIntervaloZeroSoMachucaUmaVez()
    {
        var (world, _, target) = BuildContact(new ContactDamage { Damage = 10f, Interval = 0f });

        Advance(world, frames: 60);

        Assert.Equal(90f, target.Get<Health>()!.Current, Tolerance);
    }

    [Fact]
    public void ContactDamageRespeitaOTargetPrefix()
    {
        // Sem o filtro, um inimigo com dano de corpo mataria os outros inimigos ao esbarrar.
        var (world, _, target) = BuildContact(
            new ContactDamage { Damage = 10f, TargetPrefix = "Player" }, targetName: "Slime");

        Advance(world, frames: 10);

        Assert.Equal(100f, target.Get<Health>()!.Current, Tolerance);
    }

    [Fact]
    public void ContactDamageEmpurraOAlvo()
    {
        var world = new World();

        var attacker = world.CreateEntity("Slime");
        attacker.Add(new Transform());
        attacker.Add(new Collider { Width = 16f, Height = 16f, IsSolid = false });
        attacker.Add(new ContactDamage { Damage = 1f, Knockback = 30f });

        var target = world.CreateEntity("Player");
        target.Add(new Transform(new Vector2(4f, 0f)));
        target.Add(new Collider { Width = 16f, Height = 16f, IsSolid = false });
        target.Add(new Health());

        Advance(world);

        Assert.Equal(34f, target.Get<Transform>()!.Position.X, Tolerance);
    }
}
