using Aurora.Runtime.Assets;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Events;
using Aurora.Runtime.Saves;
using Aurora.Runtime.Scenes;
using Aurora.Runtime.UI;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Estado por entidade: o inimigo morto continua morto, o baú aberto continua aberto.
///
/// Duas travessias diferentes e igualmente importantes: sair da sala e voltar DENTRO da mesma
/// partida (o mundo é recriado do arquivo a cada carga), e fechar o jogo e carregar o save. Um
/// jogo que só acerta a segunda ainda ressuscita o chefe quando o jogador atravessa a porta.
///
/// A memória é opt-in via <see cref="Persistent"/>: bicho comum deve respawnar (voltar num mapa
/// e achá-lo vazio é pior), chefe e baú não.
/// </summary>
public class SceneStateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aurora-state-" + Guid.NewGuid().ToString("N"));
    private readonly World _world = new();
    private readonly GameState _state = new();
    private readonly SceneStateStore _store = new();
    private readonly SceneManager _scenes;
    private readonly EventSystem _events;

    public SceneStateTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "scenes"));
        _world.SceneState = _store;

        var assets = new AssetManager(null!, new FileAssetSource(_root));
        _events = new EventSystem(_world, _state);
        _scenes = new SceneManager(_world, new SceneSerializer(), _events, new DialogueSystem(), assets);
    }

    private void WriteScene(string name, string json)
        => File.WriteAllText(Path.Combine(_root, "scenes", name), json);

    /// <summary>Sala com um chefe persistente e um slime comum.</summary>
    private void WriteRoom(string name = "sala.json") => WriteScene(name, """
        {
          "Scene": "sala",
          "Objects": [
            {
              "Name": "Chefe",
              "Components": [
                { "Type": "Transform", "X": 0, "Y": 0 },
                { "Type": "Health", "Max": 50, "Current": 50 },
                { "Type": "Persistent" }
              ]
            },
            {
              "Name": "Slime",
              "Components": [
                { "Type": "Transform", "X": 40, "Y": 0 },
                { "Type": "Health", "Max": 10, "Current": 10 }
              ]
            }
          ]
        }
        """);

    private void Kill(string name)
    {
        Assert.True(_world.TryFind(name, out var entity), $"'{name}' não está na cena.");
        _world.Destroy(entity);
        _world.Update(1f / 60f);   // drena a fila de destruição
    }

    // ---- Travessia 1: mesma partida, saindo e voltando na sala ----

    [Fact]
    public void PersistentEnemy_StaysDead_WhenComingBackToTheRoom()
    {
        WriteRoom();
        _scenes.Load("scenes/sala.json");
        Kill("Chefe");

        _scenes.Load("scenes/sala.json");
        _world.Update(1f / 60f);

        Assert.False(_world.TryFind("Chefe", out _), "O chefe voltou à vida ao reentrar na sala.");
    }

    [Fact]
    public void OrdinaryEnemy_ComesBack_BecauseMemoryIsOptIn()
    {
        WriteRoom();
        _scenes.Load("scenes/sala.json");
        Kill("Slime");

        _scenes.Load("scenes/sala.json");

        Assert.True(_world.TryFind("Slime", out _), "Bicho comum sem Persistent deveria respawnar.");
    }

    [Fact]
    public void DeadEnemy_IsGoneBeforeTheFirstFrame_NotAfterIt()
    {
        // Aplicar depois do primeiro update deixaria o chefe aparecer por um frame — visível.
        WriteRoom();
        _scenes.Load("scenes/sala.json");
        Kill("Chefe");

        _scenes.Load("scenes/sala.json");

        Assert.False(_world.TryFind("Chefe", out _));
    }

    [Fact]
    public void EachScene_HasItsOwnMemory()
    {
        WriteRoom("sala.json");
        WriteRoom("sala2.json");

        _scenes.Load("scenes/sala.json");
        Kill("Chefe");

        _scenes.Load("scenes/sala2.json");

        Assert.True(_world.TryFind("Chefe", out _), "Matar na sala 1 não pode apagar o da sala 2.");
    }

    // ---- Gatilho Once (baú, alavanca) ----

    [Fact]
    public void PersistentChest_StaysOpen_WhenComingBackToTheRoom()
    {
        WriteScene("bau.json", """
            {
              "Scene": "bau",
              "Objects": [{
                "Name": "Bau",
                "Components": [
                  { "Type": "Transform", "X": 0, "Y": 0 },
                  { "Type": "Persistent" },
                  {
                    "Type": "EventTrigger",
                    "Trigger": "Timer",
                    "Interval": 0,
                    "Once": true,
                    "Actions": [{ "Action": "SetVariable", "Name": "ouro", "Op": "Add", "Value": 100 }]
                  }
                ]
              }]
            }
            """);

        _scenes.Load("scenes/bau.json");
        _events.Update(1f / 60f);
        Assert.Equal(100, _state.GetVariable("ouro"), 0.001);

        _scenes.Load("scenes/bau.json");
        _events.Update(1f / 60f);

        Assert.Equal(100, _state.GetVariable("ouro"), 0.001);
    }

    [Fact]
    public void ChestWithoutPersistent_GivesTheRewardAgain()
    {
        WriteScene("bau.json", """
            {
              "Scene": "bau",
              "Objects": [{
                "Name": "Bau",
                "Components": [
                  { "Type": "Transform", "X": 0, "Y": 0 },
                  {
                    "Type": "EventTrigger",
                    "Trigger": "Timer",
                    "Interval": 0,
                    "Once": true,
                    "Actions": [{ "Action": "SetVariable", "Name": "ouro", "Op": "Add", "Value": 100 }]
                  }
                ]
              }]
            }
            """);

        _scenes.Load("scenes/bau.json");
        _events.Update(1f / 60f);
        _scenes.Load("scenes/bau.json");
        _events.Update(1f / 60f);

        Assert.Equal(200, _state.GetVariable("ouro"), 0.001);
    }

    // ---- Registro em si ----

    [Fact]
    public void Snapshot_LeavesOutScenesWithNothingToRemember()
    {
        // Jogo que nunca usou Persistent não pode carregar um bloco vazio em todo save.
        WriteRoom();
        _scenes.Load("scenes/sala.json");
        Kill("Slime");

        Assert.Empty(_store.ToSnapshot());
        Assert.False(_store.HasAnything);
    }

    [Fact]
    public void LoadSnapshot_Replaces_DoesNotMerge()
    {
        WriteRoom();
        _scenes.Load("scenes/sala.json");
        Kill("Chefe");
        Assert.True(_store.HasAnything);

        _store.LoadSnapshot(null);

        Assert.False(_store.HasAnything);
    }

    [Fact]
    public void WorldWithoutAStore_BehavesLikeBefore()
    {
        // World montado à mão (teste, ferramenta) não tem registro — não pode estourar.
        var world = new World();
        var entity = world.CreateEntity("Coisa");
        entity.Add(new Transform());
        entity.Add(new Persistent());

        world.Destroy(entity);
        world.Update(1f / 60f);

        Assert.False(world.TryFind("Coisa", out _));
    }

    [Fact]
    public void PersistentComponent_SurvivesTheSceneRoundtrip()
    {
        var origem = new World();
        origem.CreateEntity("Chefe").Add(new Persistent());

        var serializer = new SceneSerializer();
        string json = serializer.Save("t", new SceneContext { World = origem });

        var destino = new World();
        serializer.Load(json, new SceneContext { World = destino });

        Assert.True(destino.TryFind("Chefe", out var chefe));
        Assert.NotNull(chefe.Get<Persistent>());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* melhor esforço */ }
    }
}
