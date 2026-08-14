using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;

namespace Aurora.Runtime.Tests;

/// <summary>Ciclo de vida de entidades, componentes e behaviors.</summary>
public class WorldTests
{
    [Fact]
    public void EntidadeCriadaEstaVivaEComNome()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");

        Assert.True(entity.IsAlive);
        Assert.Equal("Player", entity.Name);
        Assert.Equal(1, world.EntityCount);
    }

    [Fact]
    public void ComponentesSaoGuardadosPeloTipoConcreto()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        entity.Add(new Transform(1f, 2f));
        entity.Add(new ScriptDeTeste { Vidas = 9 });

        // Get pelo tipo da subclasse funciona; pela base Behavior, não — é essa a semântica
        // que faz Get<PlayerController>() achar o script certo.
        Assert.Equal(9, entity.Get<ScriptDeTeste>()!.Vidas);
        Assert.True(entity.Has<Transform>());
    }

    [Fact]
    public void GetDeComponenteAusenteVoltaNull()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");

        Assert.Null(entity.Get<Transform>());
        Assert.False(entity.Has<Transform>());
    }

    [Fact]
    public void AddSubstituiComponenteDoMesmoTipo()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        entity.Add(new Transform(1f, 1f));
        entity.Add(new Transform(5f, 5f));

        Assert.Equal(new Vector2(5f, 5f), entity.Get<Transform>()!.Position);
    }

    [Fact]
    public void AddEmEntidadeDestruidaLancaErro()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        entity.Destroy();

        Assert.Throws<InvalidOperationException>(() => entity.Add(new Transform(0f, 0f)));
    }

    [Fact]
    public void BehaviorRecebeEntityEWorldInjetados()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        var recorder = entity.Add(new RecordingBehavior());

        Assert.Equal(entity, recorder.Entity);
        Assert.Same(world, recorder.World);
    }

    [Fact]
    public void StartRodaUmaVezSoEUpdateTodoFrame()
    {
        var world = new World();
        var recorder = world.CreateEntity("Player").Add(new RecordingBehavior());

        world.Update(0.016f);
        world.Update(0.016f);
        world.Update(0.016f);

        Assert.Equal(1, recorder.StartCount);
        Assert.Equal(3, recorder.UpdateCount);
    }

    [Fact]
    public void BehaviorDesabilitadoNaoRoda()
    {
        var world = new World();
        var recorder = world.CreateEntity("Player").Add(new RecordingBehavior());
        recorder.Enabled = false;

        world.Update(0.016f);

        Assert.Equal(0, recorder.StartCount);
        Assert.Equal(0, recorder.UpdateCount);
    }

    [Fact]
    public void BehaviorQueLancaExcecaoEDesativadoSemDerrubarOResto()
    {
        var world = new World();
        world.CreateEntity("Bugado").Add(new ThrowingBehavior());
        var saudavel = world.CreateEntity("Ok").Add(new RecordingBehavior());

        world.Update(0.016f);
        world.Update(0.016f);

        // O mundo continua rodando e o behavior saudável não perdeu nenhum frame.
        Assert.Equal(2, saudavel.UpdateCount);
        Assert.False(world.Entities.Single(e => e.Name == "Bugado").Get<ThrowingBehavior>()!.Enabled);
    }

    [Fact]
    public void DestroyForaDoUpdateEImediato()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");

        entity.Destroy();

        Assert.False(entity.IsAlive);
        Assert.Equal(0, world.EntityCount);
    }

    [Fact]
    public void DestroyDuranteOUpdateSoAconteceNoFimDoFrame()
    {
        var world = new World();
        var alvo = world.CreateEntity("Alvo");
        alvo.Add(new RecordingBehavior());

        var carrasco = world.CreateEntity("Carrasco");
        carrasco.Add(new DestruidorNoUpdate { Alvo = alvo });

        Assert.Equal(2, world.EntityCount);
        world.Update(0.016f);
        Assert.Equal(1, world.EntityCount);
        Assert.False(alvo.IsAlive);
    }

    [Fact]
    public void OnDestroyEChamadoAoDestruir()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        var recorder = entity.Add(new RecordingBehavior());

        entity.Destroy();

        Assert.Equal(1, recorder.DestroyCount);
    }

    [Fact]
    public void DestruirRemoveOsComponentes()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        entity.Add(new Transform(1f, 1f));

        entity.Destroy();

        Assert.Null(entity.Get<Transform>());
        Assert.Equal("<destruída>", entity.Name);
    }

    [Fact]
    public void DestroyDaMesmaEntidadeDuasVezesNaoQuebra()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        var recorder = entity.Add(new RecordingBehavior());

        entity.Destroy();
        entity.Destroy();

        Assert.Equal(1, recorder.DestroyCount);
    }

    [Fact]
    public void DestroyFuncionaComOMundoPausado()
    {
        // Um botão do menu de pausa que destrói algo não pode ficar preso até despausar —
        // nem na destruição imediata, nem no dreno da fila que o Update faz mesmo pausado.
        var world = new World();
        var entity = world.CreateEntity("Player");
        world.Paused = true;

        entity.Destroy();
        world.Update(0.016f);

        Assert.False(entity.IsAlive);
        Assert.Equal(0, world.EntityCount);
    }

    [Fact]
    public void TryFindAchaAPrimeiraEntidadeComONome()
    {
        var world = new World();
        var primeira = world.CreateEntity("Inimigo");
        world.CreateEntity("Inimigo");

        Assert.True(world.TryFind("Inimigo", out var achada));
        Assert.Equal(primeira, achada);
    }

    [Fact]
    public void TryFindNaoAchaEntidadeDestruida()
    {
        var world = new World();
        world.CreateEntity("Inimigo").Destroy();

        Assert.False(world.TryFind("Inimigo", out _));
    }

    [Fact]
    public void QueryDeUmComponenteRetornaSoQuemTem()
    {
        var world = new World();
        world.CreateEntity("ComTransform").Add(new Transform(1f, 1f));
        world.CreateEntity("SemTransform");

        Assert.Single(world.Query<Transform>());
    }

    [Fact]
    public void QueryDeDoisComponentesExigeOsDois()
    {
        var world = new World();
        var completa = world.CreateEntity("Completa");
        completa.Add(new Transform(1f, 1f));
        completa.Add(new Collider());

        world.CreateEntity("SoTransform").Add(new Transform(2f, 2f));
        world.CreateEntity("SoCollider").Add(new Collider());

        var resultado = world.Query<Transform, Collider>().ToList();

        Assert.Equal(completa, Assert.Single(resultado).Entity);
    }

    [Fact]
    public void ClearApagaTudo()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        entity.Add(new Transform(1f, 1f));
        entity.Add(new RecordingBehavior());

        world.Clear();

        Assert.Equal(0, world.EntityCount);
        Assert.Empty(world.Entities);
        Assert.Empty(world.Query<Transform>());
    }

    [Fact]
    public void IdsRecomecamDepoisDoClear()
    {
        // Cena nova reinicia a numeração — importante pra referências por id em save/cena
        // não apontarem pra entidade errada de uma cena anterior.
        var world = new World();
        world.CreateEntity("A");
        world.CreateEntity("B");

        world.Clear();

        Assert.Equal(1, world.CreateEntity("Novo").Id);
    }

    [Fact]
    public void GetNameDeEntidadeInexistenteNaoLanca()
    {
        Assert.Equal("<destruída>", new World().GetName(999));
    }

    /// <summary>Destrói outra entidade de dentro do Update — o caso que a fila de destruição
    /// adiada existe pra proteger.</summary>
    private sealed class DestruidorNoUpdate : Behavior
    {
        public Entity Alvo;

        public override void Update(float deltaTime) => World?.Destroy(Alvo);
    }
}
