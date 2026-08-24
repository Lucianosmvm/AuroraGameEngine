using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Events;
using Aurora.Runtime.Scenes;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Gatilhos e ações que fecham as lacunas de autoria sem código: reagir à morte, reagir ao
/// collider (e não à distância), sortear chance, e nascer com teto.
/// </summary>
public class EventTriggerExtrasTests
{
    private const float Tolerance = 0.01f;

    private const string LootPrefab = """{ "Name": "Gel", "Components": [ { "Type": "Transform" } ] }""";

    private static (World World, EventSystem Events, GameState State) Build(
        params (string Path, string Json)[] prefabs)
    {
        var serializer = new SceneSerializer();
        var files = prefabs.ToDictionary(p => p.Path, p => p.Json);
        var world = new World();
        world.PrefabFactory = (path, position) => files.TryGetValue(path, out string? json)
            ? serializer.LoadEntity(json, new SceneContext { World = world }, position)
            : null;

        var state = new GameState();
        return (world, new EventSystem(world, state), state);
    }

    // ---------- Gatilho Death ----------

    [Fact]
    public void MorteLargaOLootNaPosicaoDeQuemMorreu()
    {
        // A janela é estreita: o Health destrói a entidade no mesmo Damage. Se o evento rodasse
        // depois, não haveria posição pra largar o item.
        var (world, _, _) = Build(("prefabs/gel.json", LootPrefab));

        var slime = world.CreateEntity("Slime");
        slime.Add(new Transform(new Vector2(80f, 25f)));
        slime.Add(new Health { Max = 10f, Current = 10f });
        slime.Add(new EventTrigger
        {
            Trigger = "Death",
            Actions = [new EventAction { Type = "Spawn", Name = "prefabs/gel.json" }],
        });

        world.Damage(slime, 10f);

        var loot = world.Entities.Single(e => e.Name == "Gel");
        Assert.Equal(80f, loot.Get<Transform>()!.Position.X, Tolerance);
        Assert.Equal(25f, loot.Get<Transform>()!.Position.Y, Tolerance);
    }

    [Fact]
    public void MorteTambemDaXpEAvancaQuest()
    {
        // O ponto do gatilho Death é que as 25 ações passam a valer na morte, não só o loot.
        var (world, _, state) = Build();

        var boss = world.CreateEntity("Chefe");
        boss.Add(new Transform());
        boss.Add(new Health { Max = 10f, Current = 10f });
        boss.Add(new EventTrigger
        {
            Trigger = "Death",
            Actions =
            [
                new EventAction { Type = "SetVariable", Name = "XP", Op = "Add", Value = 50f },
                new EventAction { Type = "SetSwitch", Name = "ChefeMorto", On = true },
            ],
        });

        world.Damage(boss, 99f);

        Assert.Equal(50f, state.GetVariable("XP"), Tolerance);
        Assert.True(state.GetSwitch("ChefeMorto"));
    }

    [Fact]
    public void MorteNaoDisparaEmDanoQueNaoMata()
    {
        var (world, _, state) = Build();

        var slime = world.CreateEntity("Slime");
        slime.Add(new Transform());
        slime.Add(new Health { Max = 100f, Current = 100f });
        slime.Add(new EventTrigger
        {
            Trigger = "Death",
            Actions = [new EventAction { Type = "SetVariable", Name = "XP", Op = "Add", Value = 50f }],
        });

        world.Damage(slime, 30f);

        Assert.Equal(0f, state.GetVariable("XP"), Tolerance);
    }

    // ---------- Gatilho Touch ----------

    [Fact]
    public void TouchUsaAFormaDoColliderENaoADistancia()
    {
        // PlayerTouch mede centro a centro e ignora o tamanho do collider: uma placa de pressão
        // larga dispararia longe da borda, ou nem dispararia. Touch usa a sobreposição real.
        var (world, events, state) = Build();

        var plate = world.CreateEntity("Placa");
        plate.Add(new Transform());
        plate.Add(new Collider { Width = 200f, Height = 16f, IsSolid = false });
        plate.Add(new EventTrigger
        {
            Trigger = "Touch",
            TargetPrefix = "Player",
            Actions = [new EventAction { Type = "SetSwitch", Name = "PortaAberta", On = true }],
        });

        // 80px do centro: muito além do Radius padrão de 20, mas dentro do collider de 200 largura.
        var player = world.CreateEntity("Player");
        player.Add(new Transform(new Vector2(80f, 0f)));
        player.Add(new Collider { Width = 16f, Height = 16f, IsSolid = false });

        world.Update(1f / 60f);
        events.Update(1f / 60f);

        Assert.True(state.GetSwitch("PortaAberta"));
    }

    [Fact]
    public void TouchIgnoraQuemNaoBateComOTargetPrefix()
    {
        var (world, events, state) = Build();

        var plate = world.CreateEntity("Placa");
        plate.Add(new Transform());
        plate.Add(new Collider { Width = 64f, Height = 64f, IsSolid = false });
        plate.Add(new EventTrigger
        {
            Trigger = "Touch",
            TargetPrefix = "Player",
            Actions = [new EventAction { Type = "SetSwitch", Name = "PortaAberta", On = true }],
        });

        var slime = world.CreateEntity("Slime");
        slime.Add(new Transform());
        slime.Add(new Collider { Width = 16f, Height = 16f, IsSolid = false });

        world.Update(1f / 60f);
        events.Update(1f / 60f);

        Assert.False(state.GetSwitch("PortaAberta"));
    }

    // ---------- Chance ----------

    [Fact]
    public void ChanceZeroNuncaRoda()
    {
        var (world, events, state) = Build();

        var trigger = world.CreateEntity("Bau");
        trigger.Add(new Transform());
        trigger.Add(new EventTrigger
        {
            Trigger = "SceneStart",
            Actions = [new EventAction { Type = "SetVariable", Name = "Ouro", Value = 100f, Chance = 0f }],
        });

        events.Update(1f / 60f);

        Assert.Equal(0f, state.GetVariable("Ouro"), Tolerance);
    }

    [Fact]
    public void ChanceUmSempreRoda()
    {
        // 1 é o default: quem nunca mexer no campo não pode ver ação sumindo.
        var (world, events, state) = Build();

        var trigger = world.CreateEntity("Bau");
        trigger.Add(new Transform());
        trigger.Add(new EventTrigger
        {
            Trigger = "SceneStart",
            Actions = [new EventAction { Type = "SetVariable", Name = "Ouro", Value = 100f }],
        });

        events.Update(1f / 60f);

        Assert.Equal(100f, state.GetVariable("Ouro"), Tolerance);
    }

    [Fact]
    public void ChanceIntermediariaCaiNaFaixaEsperada()
    {
        // Estatístico de propósito: o que importa é que o sorteio é por ação e mais ou menos
        // justo, não o valor exato. Faixa larga pra não virar teste instável.
        var (world, _, _) = Build(("prefabs/gel.json", LootPrefab));

        int drops = 0;
        for (int i = 0; i < 400; i++)
        {
            var slime = world.CreateEntity("Slime");
            slime.Add(new Transform());
            slime.Add(new Health { Max = 1f, Current = 1f, DestroyOnDeath = false });
            slime.Add(new EventTrigger
            {
                Trigger = "Death",
                Actions = [new EventAction { Type = "Spawn", Name = "prefabs/gel.json", Chance = 0.3f }],
            });

            int before = world.Entities.Count(e => e.Name == "Gel");
            world.Damage(slime, 1f);
            if (world.Entities.Count(e => e.Name == "Gel") > before)
                drops++;
        }

        Assert.InRange(drops, 80, 160);   // 30% de 400 = 120
    }

    // ---------- Spawner ----------

    [Fact]
    public void SpawnerRespeitaOTetoDeVivos()
    {
        // A razão de o componente existir: Timer + ação Spawn nasce pra sempre porque não sabe
        // quantos ainda estão de pé.
        var (world, _, _) = Build(("prefabs/gel.json", LootPrefab));

        var nest = world.CreateEntity("Ninho");
        nest.Add(new Transform());
        nest.Add(new Spawner { Prefab = "prefabs/gel.json", Interval = 0.1f, MaxAlive = 3 });

        for (int i = 0; i < 600; i++)
            world.Update(0.02f);   // 12s = 120 intervalos

        Assert.Equal(3, world.Entities.Count(e => e.Name == "Gel"));
    }

    [Fact]
    public void SpawnerVoltaANascerQuandoUmMorre()
    {
        var (world, _, _) = Build(("prefabs/gel.json", LootPrefab));

        var nest = world.CreateEntity("Ninho");
        nest.Add(new Transform());
        var spawner = new Spawner { Prefab = "prefabs/gel.json", Interval = 0.1f, MaxAlive = 2 };
        nest.Add(spawner);

        for (int i = 0; i < 30; i++)
            world.Update(0.02f);

        Assert.Equal(2, spawner.AliveCount);

        world.Entities.First(e => e.Name == "Gel").Destroy();

        for (int i = 0; i < 30; i++)
            world.Update(0.02f);

        Assert.Equal(2, spawner.AliveCount);
        Assert.Equal(3, spawner.TotalSpawned);
    }

    [Fact]
    public void SpawnerParaNoTotalLimit()
    {
        var (world, _, _) = Build(("prefabs/gel.json", LootPrefab));

        var nest = world.CreateEntity("Ninho");
        nest.Add(new Transform());
        var spawner = new Spawner
        {
            Prefab = "prefabs/gel.json",
            Interval = 0.05f,
            MaxAlive = 0,
            TotalLimit = 4,
        };
        nest.Add(spawner);

        for (int i = 0; i < 400; i++)
            world.Update(0.02f);

        Assert.Equal(4, spawner.TotalSpawned);
    }

    [Fact]
    public void SpawnerEsperaOStartDelay()
    {
        var (world, _, _) = Build(("prefabs/gel.json", LootPrefab));

        var nest = world.CreateEntity("Ninho");
        nest.Add(new Transform());
        var spawner = new Spawner { Prefab = "prefabs/gel.json", Interval = 0.1f, StartDelay = 1f };
        nest.Add(spawner);

        for (int i = 0; i < 25; i++)
            world.Update(0.02f);   // 0.5s

        Assert.Equal(0, spawner.TotalSpawned);

        for (int i = 0; i < 50; i++)
            world.Update(0.02f);   // 1.5s no total

        Assert.True(spawner.TotalSpawned > 0);
    }

    // ---------- PatrolPath ----------

    [Fact]
    public void PatrulhaVaiEVoltaEntrePontosRelativos()
    {
        // Relativo à posição inicial: o mesmo prefab de plataforma pode ser espalhado pela fase
        // e cada cópia patrulha em volta de onde foi colocada.
        var world = new World();
        var platform = world.CreateEntity("Plataforma");
        platform.Add(new Transform(new Vector2(500f, 200f)));
        platform.Add(new PatrolPath { Points = "0,0; 100,0", Speed = 100f, PingPong = true });

        for (int i = 0; i < 100; i++)
            world.Update(0.01f);   // 1s = chega no segundo ponto

        Assert.Equal(600f, platform.Get<Transform>()!.Position.X, 1f);

        for (int i = 0; i < 100; i++)
            world.Update(0.01f);   // volta

        Assert.Equal(500f, platform.Get<Transform>()!.Position.X, 1f);
    }

    [Fact]
    public void PatrulhaComTrajetoInvalidoNaoDerrubaACena()
    {
        var world = new World();
        var guard = world.CreateEntity("Guarda");
        guard.Add(new Transform());
        guard.Add(new PatrolPath { Points = "isso não é ponto nenhum", Speed = 100f });

        world.Update(1f / 60f);

        Assert.Equal(0f, guard.Get<Transform>()!.Position.X, Tolerance);
    }
}
