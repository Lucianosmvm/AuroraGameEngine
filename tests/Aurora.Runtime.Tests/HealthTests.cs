using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;

namespace Aurora.Runtime.Tests;

/// <summary>Dano, cura, i-frames e morte — tudo passa por <c>World.Damage</c>/<c>World.Heal</c>,
/// que é o único caminho que dispara OnDamaged/OnDeath.</summary>
public class HealthTests
{
    private const float Tolerance = 0.001f;

    private static (World World, Entity Entity, Health Health, RecordingBehavior Recorder) Criar(
        Action<Health>? configurar = null)
    {
        var world = new World();
        var entity = world.CreateEntity("Inimigo");
        var health = new Health { Max = 100f, Current = 100f };
        configurar?.Invoke(health);
        entity.Add(health);
        var recorder = entity.Add(new RecordingBehavior());
        return (world, entity, health, recorder);
    }

    [Fact]
    public void DanoSubtraiEDevolveTrue()
    {
        var (world, entity, health, _) = Criar();

        Assert.True(world.Damage(entity, 30f));
        Assert.Equal(70f, health.Current, Tolerance);
    }

    [Fact]
    public void DanoNotificaOsBehaviorsDaEntidade()
    {
        var (world, entity, _, recorder) = Criar();

        world.Damage(entity, 30f);

        Assert.Equal(30f, Assert.Single(recorder.DamageTaken), Tolerance);
    }

    [Fact]
    public void EntidadeSemHealthIgnoraODano()
    {
        var world = new World();
        var entity = world.CreateEntity("Pedra");

        Assert.False(world.Damage(entity, 10f));
    }

    [Fact]
    public void DanoZeroOuNegativoEIgnorado()
    {
        var (world, entity, health, recorder) = Criar();

        Assert.False(world.Damage(entity, 0f));
        Assert.False(world.Damage(entity, -50f));
        Assert.Equal(100f, health.Current, Tolerance);
        Assert.Empty(recorder.DamageTaken);
    }

    [Fact]
    public void InvulneravelBloqueiaODano()
    {
        var (world, entity, health, recorder) = Criar(h => h.Invulnerable = true);

        Assert.False(world.Damage(entity, 30f));
        Assert.Equal(100f, health.Current, Tolerance);
        Assert.Empty(recorder.DamageTaken);
    }

    [Fact]
    public void VidaNuncaFicaNegativa()
    {
        var (world, entity, health, _) = Criar(h => h.DestroyOnDeath = false);

        world.Damage(entity, 500f);

        Assert.Equal(0f, health.Current, Tolerance);
    }

    [Fact]
    public void ZerarAVidaDisparaOnDeath()
    {
        var (world, entity, _, recorder) = Criar(h => h.DestroyOnDeath = false);

        world.Damage(entity, 100f);

        Assert.Equal(1, recorder.DeathCount);
    }

    [Fact]
    public void DestroyOnDeathDestroiAEntidade()
    {
        var (world, entity, _, _) = Criar(h => h.DestroyOnDeath = true);

        world.Damage(entity, 100f);

        Assert.False(entity.IsAlive);
    }

    [Fact]
    public void DestroyOnDeathDesligadoMantemAEntidadeViva()
    {
        var (world, entity, _, _) = Criar(h => h.DestroyOnDeath = false);

        world.Damage(entity, 100f);

        Assert.True(entity.IsAlive);
    }

    [Fact]
    public void MortoNaoTomaDanoDeNovo()
    {
        var (world, entity, _, recorder) = Criar(h => h.DestroyOnDeath = false);

        world.Damage(entity, 100f);
        Assert.False(world.Damage(entity, 10f));
        Assert.Equal(1, recorder.DeathCount);
    }

    // ---- I-frames ----

    [Fact]
    public void IFramesBloqueiamOSegundoGolpeNoMesmoInstante()
    {
        var (world, entity, health, _) = Criar(h => h.InvulnerabilityAfterHit = 0.5f);

        Assert.True(world.Damage(entity, 10f));
        Assert.False(world.Damage(entity, 10f));
        Assert.Equal(90f, health.Current, Tolerance);
    }

    [Fact]
    public void IFramesExpiramComOTempoDeUpdate()
    {
        var (world, entity, health, _) = Criar(h => h.InvulnerabilityAfterHit = 0.1f);

        world.Damage(entity, 10f);
        world.Update(0.04f);
        Assert.False(world.Damage(entity, 10f)); // ainda invencível

        world.Update(0.08f);
        Assert.True(world.Damage(entity, 10f)); // timer chegou a zero
        Assert.Equal(80f, health.Current, Tolerance);
    }

    [Fact]
    public void SemIFramesConfiguradosODanoEContinuo()
    {
        var (world, entity, health, _) = Criar();

        world.Damage(entity, 10f);
        world.Damage(entity, 10f);
        world.Damage(entity, 10f);

        Assert.Equal(70f, health.Current, Tolerance);
    }

    [Fact]
    public void TimerDeIFramesNuncaFicaNegativo()
    {
        var (world, entity, _, _) = Criar(h => h.InvulnerabilityAfterHit = 0.1f);

        world.Damage(entity, 10f);
        world.Update(10f); // muito além do timer

        Assert.True(world.Damage(entity, 10f));
    }

    [Fact]
    public void MundoPausadoNaoConsomeOsIFrames()
    {
        var (world, entity, _, _) = Criar(h => h.InvulnerabilityAfterHit = 0.1f);
        world.Damage(entity, 10f);

        world.Paused = true;
        world.Update(1f);

        Assert.False(world.Damage(entity, 10f));
    }

    // ---- Cura ----

    [Fact]
    public void CuraSomaSemPassarDoMax()
    {
        var (world, entity, health, _) = Criar();
        world.Damage(entity, 50f);

        world.Heal(entity, 20f);
        Assert.Equal(70f, health.Current, Tolerance);

        world.Heal(entity, 999f);
        Assert.Equal(100f, health.Current, Tolerance);
    }

    [Fact]
    public void CuraComValorZeroOuNegativoNaoFazNada()
    {
        var (world, entity, health, _) = Criar();
        world.Damage(entity, 50f);

        world.Heal(entity, 0f);
        world.Heal(entity, -10f);

        Assert.Equal(50f, health.Current, Tolerance);
    }

    [Fact]
    public void CurarEntidadeSemHealthNaoQuebra()
    {
        var world = new World();
        var entity = world.CreateEntity("Pedra");

        world.Heal(entity, 10f); // não deve lançar
    }

    [Fact]
    public void IsDeadRefleteAVidaAtual()
    {
        var (world, entity, health, _) = Criar(h => h.DestroyOnDeath = false);

        Assert.False(health.IsDead);
        world.Damage(entity, 100f);
        Assert.True(health.IsDead);
    }
}
