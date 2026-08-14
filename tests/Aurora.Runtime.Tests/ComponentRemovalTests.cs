using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;

namespace Aurora.Runtime.Tests;

/// <summary>Remoção de componente em runtime (<c>World.Remove&lt;T&gt;</c> / <c>Entity.Remove&lt;T&gt;</c>).</summary>
public class ComponentRemovalTests
{
    [Fact]
    public void RemoveTiraOComponenteEDevolveTrue()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        entity.Add(new Transform(1f, 1f));

        Assert.True(entity.Remove<Transform>());
        Assert.Null(entity.Get<Transform>());
        Assert.False(entity.Has<Transform>());
    }

    [Fact]
    public void RemoveDeComponenteAusenteDevolveFalse()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");

        Assert.False(entity.Remove<Transform>());
    }

    [Fact]
    public void RemoveDuasVezesDevolveFalseNaSegunda()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        entity.Add(new Collider());

        Assert.True(entity.Remove<Collider>());
        Assert.False(entity.Remove<Collider>());
    }

    [Fact]
    public void RemoveNaoMexeNosOutrosComponentes()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        entity.Add(new Transform(3f, 4f));
        entity.Add(new Collider());

        entity.Remove<Collider>();

        Assert.Equal(new Vector2(3f, 4f), entity.Get<Transform>()!.Position);
    }

    [Fact]
    public void RemoveNaoAfetaOutrasEntidades()
    {
        var world = new World();
        var a = world.CreateEntity("A");
        a.Add(new Transform(1f, 1f));
        var b = world.CreateEntity("B");
        b.Add(new Transform(2f, 2f));

        a.Remove<Transform>();

        Assert.Null(a.Get<Transform>());
        Assert.Equal(new Vector2(2f, 2f), b.Get<Transform>()!.Position);
    }

    [Fact]
    public void RemoveResolvePeloTipoConcretoIgualAoGet()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        entity.Add(new ScriptDeTeste());

        // Remove<Behavior>() não acha nada: o store é indexado pelo tipo concreto, mesma
        // semântica de Get<T>().
        Assert.False(entity.Remove<Behavior>());
        Assert.True(entity.Remove<ScriptDeTeste>());
    }

    [Fact]
    public void RemoverBehaviorDisparaOnDestroy()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        var recorder = entity.Add(new RecordingBehavior());

        entity.Remove<RecordingBehavior>();

        Assert.Equal(1, recorder.DestroyCount);
    }

    [Fact]
    public void BehaviorRemovidoParaDeRodar()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        var recorder = entity.Add(new RecordingBehavior());

        world.Update(0.016f);
        Assert.Equal(1, recorder.UpdateCount);

        entity.Remove<RecordingBehavior>();
        world.Update(0.016f);
        world.Update(0.016f);

        Assert.Equal(1, recorder.UpdateCount);
    }

    [Fact]
    public void BehaviorRemovidoParaDeReceberCallbackDeColisao()
    {
        var world = new World();

        var parede = world.CreateEntity("Parede");
        parede.Add(new Transform(Vector2.Zero));
        parede.Add(new Collider { Width = 16f, Height = 16f, IsKinematic = true });

        var player = world.CreateEntity("Player");
        player.Add(new Transform(new Vector2(10f, 0f)));
        player.Add(new Collider { Width = 16f, Height = 16f });
        var recorder = player.Add(new RecordingBehavior());

        world.Update(0.016f);
        Assert.Single(recorder.CollisionsWith);

        player.Remove<RecordingBehavior>();
        player.Get<Transform>()!.Position = new Vector2(10f, 0f); // sobrepõe de novo
        world.Update(0.016f);

        Assert.Single(recorder.CollisionsWith);
    }

    [Fact]
    public void RemoverColliderTiraAEntidadeDaColisao()
    {
        var world = new World();

        var parede = world.CreateEntity("Parede");
        parede.Add(new Transform(Vector2.Zero));
        parede.Add(new Collider { Width = 16f, Height = 16f, IsKinematic = true });

        var player = world.CreateEntity("Player");
        var transform = player.Add(new Transform(new Vector2(10f, 0f)));
        player.Add(new Collider { Width = 16f, Height = 16f });

        player.Remove<Collider>();
        world.Update(0.016f);

        Assert.Equal(new Vector2(10f, 0f), transform.Position);
    }

    [Fact]
    public void RemoverBehaviorDeDentroDoUpdateNaoFazVizinhoPerderOFrame()
    {
        // Regressão: tirar o behavior da lista global no meio do laço por índice desloca
        // todo mundo pra esquerda e o próximo da fila é pulado nesse frame.
        var world = new World();

        var alvo = world.CreateEntity("Alvo");
        var alvoRecorder = alvo.Add(new RecordingBehavior());

        var carrasco = world.CreateEntity("Carrasco");
        carrasco.Add(new RemovedorNoUpdate { Alvo = alvo });

        var vizinho = world.CreateEntity("Vizinho");
        var vizinhoRecorder = vizinho.Add(new RecordingBehavior());

        world.Update(0.016f);

        Assert.Equal(1, alvoRecorder.UpdateCount);    // rodou antes de ser removido
        Assert.Equal(1, vizinhoRecorder.UpdateCount); // não foi pulado
    }

    [Fact]
    public void BehaviorRemovidoDeDentroDoUpdateNaoRodaNosFramesSeguintes()
    {
        var world = new World();
        var alvo = world.CreateEntity("Alvo");
        var alvoRecorder = alvo.Add(new RecordingBehavior());

        var carrasco = world.CreateEntity("Carrasco");
        carrasco.Add(new RemovedorNoUpdate { Alvo = alvo });

        world.Update(0.016f);
        world.Update(0.016f);
        world.Update(0.016f);

        Assert.Equal(1, alvoRecorder.UpdateCount);
        Assert.Equal(1, alvoRecorder.DestroyCount);
    }

    // ---- Substituição por Add ----

    [Fact]
    public void SubstituirBehaviorDesligaOAntigo()
    {
        // Regressão: Add sobrescrevia o store mas deixava o behavior antigo na lista de
        // execução — ele seguia rodando pra sempre, invisível pra Get<T>().
        var world = new World();
        var entity = world.CreateEntity("Player");
        var antigo = entity.Add(new RecordingBehavior());
        var novo = entity.Add(new RecordingBehavior());

        world.Update(0.016f);

        Assert.Equal(0, antigo.UpdateCount);
        Assert.Equal(1, novo.UpdateCount);
        Assert.Same(novo, entity.Get<RecordingBehavior>());
    }

    [Fact]
    public void SubstituirBehaviorDisparaOnDestroyNoAntigo()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        var antigo = entity.Add(new RecordingBehavior());
        entity.Add(new RecordingBehavior());

        Assert.Equal(1, antigo.DestroyCount);
    }

    [Fact]
    public void SubstituirBehaviorTiraOAntigoDosCallbacks()
    {
        var world = new World();

        var parede = world.CreateEntity("Parede");
        parede.Add(new Transform(Vector2.Zero));
        parede.Add(new Collider { Width = 16f, Height = 16f, IsKinematic = true });

        var player = world.CreateEntity("Player");
        player.Add(new Transform(new Vector2(10f, 0f)));
        player.Add(new Collider { Width = 16f, Height = 16f });
        var antigo = player.Add(new RecordingBehavior());
        var novo = player.Add(new RecordingBehavior());

        world.Update(0.016f);

        Assert.Empty(antigo.CollisionsWith);
        Assert.Single(novo.CollisionsWith);
    }

    [Fact]
    public void AdicionarAMesmaInstanciaDeNovoNaoADesliga()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        var recorder = new RecordingBehavior();

        entity.Add(recorder);
        entity.Add(recorder);

        world.Update(0.016f);

        Assert.Equal(0, recorder.DestroyCount);
        Assert.Equal(1, recorder.UpdateCount); // uma vez só, não duas
    }

    [Fact]
    public void InstanciaRemovidaPodeSerReaproveitada()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        var recorder = entity.Add(new RecordingBehavior());

        entity.Remove<RecordingBehavior>();
        entity.Add(recorder);

        world.Update(0.016f);
        world.Update(0.016f);

        Assert.Equal(2, recorder.UpdateCount);
    }

    /// <summary>Remove o RecordingBehavior de outra entidade de dentro do próprio Update.</summary>
    private sealed class RemovedorNoUpdate : Behavior
    {
        public Entity Alvo;
        private bool _feito;

        public override void Update(float deltaTime)
        {
            if (_feito)
                return;

            _feito = true;
            Alvo.Remove<RecordingBehavior>();
        }
    }
}
