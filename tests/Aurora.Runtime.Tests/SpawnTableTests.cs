using Aurora.Runtime.Database;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Events;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Tabelas de spawn: referir um GRUPO por id em vez de um arquivo, sortear por peso e filtrar por
/// condição. É o que permite slime, zumbi e chefe saindo do mesmo ninho, cada um na sua
/// frequência e na sua hora, sem um spawner por tipo.
/// </summary>
public class SpawnTableTests
{
    private const string TablesJson = """
        {
          "Tables": [
            {
              "Id": "inimigos_floresta",
              "Entries": [
                { "Prefab": "prefabs/slime.json", "Weight": 3 },
                { "Prefab": "prefabs/zumbi.json", "Weight": 1,
                  "Condition": { "Action": "If", "Text": "Switch", "Name": "Noite", "On": true } }
              ]
            },
            {
              "Id": "so_com_condicao",
              "Entries": [
                { "Prefab": "prefabs/chefe.json",
                  "Condition": { "Action": "If", "Text": "Switch", "Name": "ChefeLiberado", "On": true } }
              ]
            }
          ]
        }
        """;

    private static (SpawnTableDatabase Tables, EventSystem Events, GameState State) Build()
    {
        var tables = new SpawnTableDatabase();
        tables.Load(TablesJson);

        var state = new GameState();
        var events = new EventSystem(new World(), state);
        return (tables, events, state);
    }

    [Fact]
    public void CaminhoDePrefabContinuaFuncionandoComoSempre()
    {
        // Compatibilidade: quem já escreveu o caminho direto na cena não pode quebrar por causa
        // do recurso novo.
        var (tables, events, _) = Build();

        Assert.Equal("prefabs/slime.json",
            tables.Resolve("prefabs/slime.json", events.TestCondition));
    }

    [Fact]
    public void IdDeTabelaSorteiaEntreAsEntradasElegiveis()
    {
        var (tables, events, state) = Build();
        state.SetSwitch("Noite", true);

        var sorteados = new HashSet<string>();
        for (int i = 0; i < 300; i++)
        {
            if (tables.Resolve("inimigos_floresta", events.TestCondition) is { } prefab)
                sorteados.Add(prefab);
        }

        Assert.Contains("prefabs/slime.json", sorteados);
        Assert.Contains("prefabs/zumbi.json", sorteados);
    }

    [Fact]
    public void OPesoMudaAFrequencia()
    {
        // Peso 3 contra 1: sem a roleta por peso, o chefe sairia tanto quanto o slime.
        var (tables, events, state) = Build();
        state.SetSwitch("Noite", true);

        int slimes = 0;
        const int rodadas = 1200;

        for (int i = 0; i < rodadas; i++)
        {
            if (tables.Resolve("inimigos_floresta", events.TestCondition) == "prefabs/slime.json")
                slimes++;
        }

        // Esperado 75% (3 de 4). Faixa larga pra não virar teste instável.
        Assert.InRange(slimes, (int)(rodadas * 0.68), (int)(rodadas * 0.82));
    }

    [Fact]
    public void CondicaoFalsaTiraAEntradaDoSorteio()
    {
        var (tables, events, _) = Build();   // Noite desligado

        for (int i = 0; i < 200; i++)
            Assert.NotEqual("prefabs/zumbi.json", tables.Resolve("inimigos_floresta", events.TestCondition));
    }

    [Fact]
    public void TabelaSemNenhumaEntradaElegivelNaoNasceNada()
    {
        // Null, e não "cai na primeira entrada": o contrário faria o chefe nascer antes da hora,
        // que é o oposto do que a condição pede.
        var (tables, events, _) = Build();

        Assert.Null(tables.Resolve("so_com_condicao", events.TestCondition));
    }

    [Fact]
    public void TabelaSemIdEIgnorada()
    {
        var tables = new SpawnTableDatabase();

        tables.Load("""{ "Tables": [ { "Entries": [] }, { "Id": "ok", "Entries": [] } ] }""");

        Assert.Equal(1, tables.Count);
        Assert.NotNull(tables.Get("ok"));
    }

    [Fact]
    public void PesoZeroDesligaAEntradaSemApagarLa()
    {
        var tables = new SpawnTableDatabase();
        tables.Load("""
            {
              "Tables": [ { "Id": "t", "Entries": [
                { "Prefab": "a.json", "Weight": 0 },
                { "Prefab": "b.json", "Weight": 1 } ] } ]
            }
            """);

        var events = new EventSystem(new World(), new GameState());

        for (int i = 0; i < 100; i++)
            Assert.Equal("b.json", tables.Resolve("t", events.TestCondition));

        Assert.Equal(2, tables.Get("t")!.Entries.Count);
    }

    // ---------- Integração com o Spawner ----------

    [Fact]
    public void SpawnerSoNasceComORequiredSwitchNoEstadoPedido()
    {
        var world = new World();
        var state = new GameState();
        world.State = state;

        int spawns = 0;
        world.PrefabFactory = (_, _) => { spawns++; return world.CreateEntity("Bicho"); };

        var nest = world.CreateEntity("Ninho");
        nest.Add(new Transform());
        nest.Add(new Spawner
        {
            Prefab = "prefabs/slime.json",
            Interval = 0.1f,
            MaxAlive = 0,
            RequiredSwitch = "Noite",
        });

        for (int i = 0; i < 100; i++)
            world.Update(0.02f);   // 2s de dia

        Assert.Equal(0, spawns);

        state.SetSwitch("Noite", true);
        for (int i = 0; i < 100; i++)
            world.Update(0.02f);

        Assert.True(spawns > 0, "Não nasceu nada mesmo com o switch ligado.");
    }

    [Fact]
    public void SpawnerDesligadoNaoAcumulaLevaPraDespejarDeUmaVez()
    {
        // Se o relógio corresse desligado, meia hora de dia viraria uma horda no primeiro frame
        // da noite.
        var world = new World();
        var state = new GameState();
        world.State = state;

        int spawns = 0;
        world.PrefabFactory = (_, _) => { spawns++; return world.CreateEntity("Bicho"); };

        var nest = world.CreateEntity("Ninho");
        nest.Add(new Transform());
        nest.Add(new Spawner
        {
            Prefab = "prefabs/slime.json",
            Interval = 1f,
            MaxAlive = 0,
            RequiredSwitch = "Noite",
        });

        for (int i = 0; i < 500; i++)
            world.Update(0.02f);   // 10s desligado

        state.SetSwitch("Noite", true);
        world.Update(0.02f);       // primeiro frame ligado

        Assert.Equal(0, spawns);
    }
}
