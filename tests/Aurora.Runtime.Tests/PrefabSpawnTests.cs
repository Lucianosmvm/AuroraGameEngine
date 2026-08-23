using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Instanciar prefab em jogo — a peça que faltava pra spawn de inimigo, onda, drop e efeito de
/// ataque existirem sem script. Prefab é o mesmo objeto de dentro de "Objects", salvo sozinho
/// num arquivo pelo painel PREFABS do editor.
/// </summary>
public class PrefabSpawnTests
{
    private const float Tolerance = 0.01f;

    private const string SlimePrefab = """
        {
          "Name": "Slime",
          "Components": [
            { "Type": "Transform", "X": 999, "Y": 999 },
            { "Type": "Health", "Max": 30, "Current": 30 },
            { "Type": "ContactDamage", "Damage": 7, "TargetPrefix": "Player" }
          ]
        }
        """;

    /// <summary>Liga o World a uma fábrica que resolve nomes de prefab num dicionário em memória
    /// — o mesmo caminho que o Game usa, sem precisar de arquivo nem de GL.</summary>
    private static World BuildWorld(params (string Path, string Json)[] prefabs)
    {
        var serializer = new SceneSerializer();
        var files = prefabs.ToDictionary(p => p.Path, p => p.Json);
        var world = new World();

        world.PrefabFactory = (path, position) => files.TryGetValue(path, out string? json)
            ? serializer.LoadEntity(json, new SceneContext { World = world }, position)
            : null;

        return world;
    }

    [Fact]
    public void SpawnCriaAEntidadeComOsComponentesDoArquivo()
    {
        var world = BuildWorld(("prefabs/slime.json", SlimePrefab));

        var slime = world.Spawn("prefabs/slime.json", new Vector2(40f, 10f));

        Assert.NotNull(slime);
        Assert.Equal("Slime", slime!.Value.Name);
        Assert.Equal(30f, slime.Value.Get<Health>()!.Max, Tolerance);
        Assert.Equal(7f, slime.Value.Get<ContactDamage>()!.Damage, Tolerance);
    }

    [Fact]
    public void PosicaoDoSpawnSobrescreveADoArquivo()
    {
        // O prefab guarda a posição de onde foi salvo no editor, que não tem nada a ver com onde
        // ele vai nascer em jogo. Sem sobrescrever, todo inimigo nasceria no mesmo canto.
        var world = BuildWorld(("prefabs/slime.json", SlimePrefab));

        var slime = world.Spawn("prefabs/slime.json", new Vector2(40f, 10f));

        var position = slime!.Value.Get<Transform>()!.Position;
        Assert.Equal(40f, position.X, Tolerance);
        Assert.Equal(10f, position.Y, Tolerance);
    }

    [Fact]
    public void SpawnSemPosicaoMantemOTransformDoArquivo()
    {
        var world = BuildWorld(("prefabs/slime.json", SlimePrefab));

        var slime = world.Spawn("prefabs/slime.json");

        Assert.Equal(999f, slime!.Value.Get<Transform>()!.Position.X, Tolerance);
    }

    [Fact]
    public void PrefabSemTransformGanhaUmNoSpawn()
    {
        // Entidade sem Transform não aparece na tela nem colide — e um prefab spawnado sempre
        // quer estar em algum lugar. Deixar sem seria um bug silencioso.
        const string json = """{ "Name": "Fumaca", "Components": [ { "Type": "Health" } ] }""";
        var world = BuildWorld(("prefabs/fumaca.json", json));

        var smoke = world.Spawn("prefabs/fumaca.json", new Vector2(5f, 6f));

        Assert.NotNull(smoke!.Value.Get<Transform>());
        Assert.Equal(5f, smoke.Value.Get<Transform>()!.Position.X, Tolerance);
    }

    [Fact]
    public void PrefabInexistenteDevolveNullSemDerrubarOJogo()
    {
        var world = BuildWorld(("prefabs/slime.json", SlimePrefab));

        Assert.Null(world.Spawn("prefabs/nao-existe.json", Vector2.Zero));
    }

    [Fact]
    public void SpawnSemFabricaLigadaDevolveNull()
    {
        // World solto (teste, ferramenta de linha de comando) não tem Game por trás. Tem que
        // avisar e seguir, não estourar.
        Assert.Null(new World().Spawn("prefabs/slime.json", Vector2.Zero));
    }

    // ---------- AttackSpawner ----------

    private const string SlashPrefab = """
        {
          "Name": "Corte",
          "Components": [
            { "Type": "Transform" },
            { "Type": "FollowTarget", "TargetName": "?", "DestroyWhenTargetGone": true },
            { "Type": "Lifetime", "Seconds": 1.5, "DestroyOnAnimationEnd": true }
          ]
        }
        """;

    private const string ArrowPrefab = """
        {
          "Name": "Flecha",
          "Components": [
            { "Type": "Transform" },
            { "Type": "Projectile", "Damage": 12 }
          ]
        }
        """;

    [Fact]
    public void AttackSpawnerPoeOPrefabNaDistanciaEDirecaoDaMira()
    {
        var world = BuildWorld(("prefabs/corte.json", SlashPrefab));
        var player = world.CreateEntity("Player");
        player.Add(new Transform(new Vector2(100f, 100f)));
        var attack = new AttackSpawner { Prefab = "prefabs/corte.json", Distance = 20f };
        player.Add(attack);

        // Sem TopDownController na entidade, Facing cai pra direita — é o default documentado.
        Assert.True(attack.Attack());

        var slash = world.Entities.Single(e => e.Name == "Corte");
        Assert.Equal(120f, slash.Get<Transform>()!.Position.X, Tolerance);
        Assert.Equal(100f, slash.Get<Transform>()!.Position.Y, Tolerance);
    }

    [Fact]
    public void AttackSpawnerLigaOEfeitoEmQuemAtacou()
    {
        // O offset do golpe muda a cada ataque (depende da mira), então não cabe no arquivo do
        // prefab — quem preenche é o spawner, no spawn.
        var world = BuildWorld(("prefabs/corte.json", SlashPrefab));
        var player = world.CreateEntity("Player");
        player.Add(new Transform());
        var attack = new AttackSpawner { Prefab = "prefabs/corte.json", Distance = 20f };
        player.Add(attack);

        attack.Attack();

        var follow = world.Entities.Single(e => e.Name == "Corte").Get<FollowTarget>()!;
        Assert.Equal("Player", follow.TargetName);
        Assert.Equal(20f, follow.Offset.X, Tolerance);
    }

    [Fact]
    public void AttackSpawnerArmaOProjetilComVelocidadeEDono()
    {
        // Velocity e Source são exatamente os dois campos que não fazem sentido numa cena
        // estática; sem preenchê-los no spawn, a flecha nasceria parada e acertaria quem atirou.
        var world = BuildWorld(("prefabs/flecha.json", ArrowPrefab));
        var archer = world.CreateEntity("Player");
        archer.Add(new Transform());
        var attack = new AttackSpawner
        {
            Prefab = "prefabs/flecha.json",
            Distance = 10f,
            ProjectileSpeed = 300f,
        };
        archer.Add(attack);

        attack.Attack();

        var projectile = world.Entities.Single(e => e.Name == "Flecha").Get<Projectile>()!;
        Assert.Equal(300f, projectile.Velocity.X, Tolerance);
        Assert.Equal(archer.Id, projectile.Source!.Value.Id);
    }

    [Fact]
    public void AttackSpawnerRespeitaOCooldown()
    {
        var world = BuildWorld(("prefabs/corte.json", SlashPrefab));
        var player = world.CreateEntity("Player");
        player.Add(new Transform());
        var attack = new AttackSpawner { Prefab = "prefabs/corte.json", Cooldown = 0.5f };
        player.Add(attack);

        Assert.True(attack.Attack());
        Assert.False(attack.Attack());

        for (int i = 0; i < 40; i++)
            world.Update(0.02f);   // 0.8s

        Assert.True(attack.Attack());
    }

    [Fact]
    public void AttackSpawnerTravaAMiraQuandoDirectionSnapEstaLigado()
    {
        // DirectionSnap existe pra casar com spritesheet que só tem algumas poses: 4 direções
        // arredonda qualquer mira pra reta mais próxima.
        var world = BuildWorld(("prefabs/corte.json", SlashPrefab));
        var player = world.CreateEntity("Player");
        player.Add(new Transform());
        var controller = new TopDownController();
        player.Add(controller);
        var attack = new AttackSpawner
        {
            Prefab = "prefabs/corte.json",
            Distance = 10f,
            DirectionSnap = 4,
        };
        player.Add(attack);

        // Facing começa pra baixo (0,1), que já é uma das 4 retas.
        attack.Attack();

        var position = world.Entities.Single(e => e.Name == "Corte").Get<Transform>()!.Position;
        Assert.Equal(0f, position.X, Tolerance);
        Assert.Equal(10f, position.Y, Tolerance);
    }

    [Fact]
    public void AttackSpawnerSemPrefabNaoFazNada()
    {
        var world = BuildWorld();
        var player = world.CreateEntity("Player");
        player.Add(new Transform());
        var attack = new AttackSpawner { Prefab = "" };
        player.Add(attack);

        Assert.False(attack.Attack());
    }
}
