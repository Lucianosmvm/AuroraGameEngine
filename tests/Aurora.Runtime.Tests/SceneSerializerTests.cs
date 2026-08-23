using System.Numerics;
using System.Text.Json;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace Aurora.Runtime.Tests;

/// <summary>Script custom com um campo de cada tipo suportado, pra exercitar o registro
/// automático por reflection de <see cref="SceneScriptAttribute"/>.</summary>
[SceneScript("ScriptDeTeste")]
public sealed class ScriptDeTeste : Behavior
{
    public float Velocidade = 1.5f;
    public int Vidas = 3;
    public bool Hostil;
    public string Alvo = "";

    /// <summary>Tipo fora da lista de campos serializáveis — tem que ser ignorado sem erro.</summary>
    public Vector2 NaoSerializavel = new(9f, 9f);
}

public class SceneSerializerTests
{
    private const float Tolerance = 0.001f;

    /// <summary>Serializa o mundo e recarrega num mundo novo. Assets fica null de propósito:
    /// nada nestes testes referencia textura, então não é preciso contexto de GL.</summary>
    private static World Roundtrip(World origem, SceneSerializer? serializer = null)
    {
        serializer ??= new SceneSerializer();
        string json = serializer.Save("Teste", new SceneContext { World = origem });

        var destino = new World();
        serializer.Load(json, new SceneContext { World = destino });
        return destino;
    }

    private static Entity Achar(World world, string nome)
    {
        Assert.True(world.TryFind(nome, out var entity), $"Entidade '{nome}' não existe no mundo.");
        return entity;
    }

    [Fact]
    public void NomeDaCenaEEntidadesAparecemNoJson()
    {
        var world = new World();
        world.CreateEntity("Player").Add(new Transform(1f, 2f));

        string json = new SceneSerializer().Save("Floresta", new SceneContext { World = world });

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Floresta", doc.RootElement.GetProperty("Scene").GetString());
        Assert.Equal("Player", doc.RootElement.GetProperty("Objects")[0].GetProperty("Name").GetString());
    }

    [Fact]
    public void TransformSobreviveAoRoundtrip()
    {
        var world = new World();
        world.CreateEntity("Player").Add(new Transform(12.5f, -7.25f)
        {
            Rotation = 1.25f,
            Scale = new Vector2(2f, 3f),
        });

        var transform = Achar(Roundtrip(world), "Player").Get<Transform>();

        Assert.NotNull(transform);
        Assert.Equal(12.5f, transform.Position.X, Tolerance);
        Assert.Equal(-7.25f, transform.Position.Y, Tolerance);
        Assert.Equal(1.25f, transform.Rotation, Tolerance);
        Assert.Equal(new Vector2(2f, 3f), transform.Scale);
    }

    [Fact]
    public void TransformComValoresPadraoVoltaIgual()
    {
        // O writer omite Rotation/Scale quando estão no padrão — o reader tem que repor os
        // mesmos defaults, senão a escala volta como (0,0) e a entidade some da tela.
        var world = new World();
        world.CreateEntity("Player").Add(new Transform(0f, 0f));

        var transform = Achar(Roundtrip(world), "Player").Get<Transform>();

        Assert.NotNull(transform);
        Assert.Equal(0f, transform.Rotation, Tolerance);
        Assert.Equal(Vector2.One, transform.Scale);
    }

    [Fact]
    public void ColliderCaixaSobreviveAoRoundtrip()
    {
        var world = new World();
        world.CreateEntity("Parede").Add(new Collider
        {
            Shape = ColliderShape.Box,
            Width = 24f,
            Height = 48f,
            Offset = new Vector2(3f, -4f),
            IsSolid = true,
            IsKinematic = true,
            Layer = 2,
            Mask = 5,
        });

        var collider = Achar(Roundtrip(world), "Parede").Get<Collider>();

        Assert.NotNull(collider);
        Assert.Equal(ColliderShape.Box, collider.Shape);
        Assert.Equal(24f, collider.Width, Tolerance);
        Assert.Equal(48f, collider.Height, Tolerance);
        Assert.Equal(new Vector2(3f, -4f), collider.Offset);
        Assert.True(collider.IsKinematic);
        Assert.Equal(2, collider.Layer);
        Assert.Equal(5, collider.Mask);
    }

    [Fact]
    public void ColliderCirculoSobreviveAoRoundtrip()
    {
        var world = new World();
        world.CreateEntity("Bola").Add(new Collider
        {
            Shape = ColliderShape.Circle,
            Radius = 12.5f,
            IsSolid = false,
        });

        var collider = Achar(Roundtrip(world), "Bola").Get<Collider>();

        Assert.NotNull(collider);
        Assert.Equal(ColliderShape.Circle, collider.Shape);
        Assert.Equal(12.5f, collider.Radius, Tolerance);
        Assert.False(collider.IsSolid);
    }

    [Fact]
    public void HealthSobreviveAoRoundtrip()
    {
        var world = new World();
        world.CreateEntity("Inimigo").Add(new Health
        {
            Max = 250f,
            Current = 80f,
            InvulnerabilityAfterHit = 0.4f,
            Invulnerable = true,
            DestroyOnDeath = false,
        });

        var health = Achar(Roundtrip(world), "Inimigo").Get<Health>();

        Assert.NotNull(health);
        Assert.Equal(250f, health.Max, Tolerance);
        Assert.Equal(80f, health.Current, Tolerance);
        Assert.Equal(0.4f, health.InvulnerabilityAfterHit, Tolerance);
        Assert.True(health.Invulnerable);
        Assert.False(health.DestroyOnDeath);
    }

    [Fact]
    public void HealthComVidaCheiaVoltaComVidaCheia()
    {
        // Current é omitido do JSON quando igual a Max; o reader precisa cair no fallback = Max,
        // não no 100 hardcoded do campo, senão inimigo com Max=250 nasce com 100.
        var world = new World();
        world.CreateEntity("Inimigo").Add(new Health { Max = 250f, Current = 250f });

        var health = Achar(Roundtrip(world), "Inimigo").Get<Health>();

        Assert.NotNull(health);
        Assert.Equal(250f, health.Max, Tolerance);
        Assert.Equal(250f, health.Current, Tolerance);
    }

    [Fact]
    public void TilemapSobreviveAoRoundtrip()
    {
        var world = new World();
        var map = new Tilemap { TileWidth = 32, TileHeight = 24, Width = 3, Height = 2, Layer = 5 };
        map.EnsureSize();
        map.SetTile(0, 0, 7);
        map.SetTile(2, 1, 9);
        map.SolidTiles.Add(7);
        map.SolidTiles.Add(9);
        world.CreateEntity("Mapa").Add(map);

        var recarregado = Achar(Roundtrip(world), "Mapa").Get<Tilemap>();

        Assert.NotNull(recarregado);
        Assert.Equal(32, recarregado.TileWidth);
        Assert.Equal(24, recarregado.TileHeight);
        Assert.Equal(3, recarregado.Width);
        Assert.Equal(2, recarregado.Height);
        Assert.Equal(5, recarregado.Layer);
        Assert.Equal(7, recarregado.GetTile(0, 0));
        Assert.Equal(9, recarregado.GetTile(2, 1));
        Assert.Equal(-1, recarregado.GetTile(1, 0));
        Assert.Equal(new[] { 7, 9 }, recarregado.SolidTiles.OrderBy(i => i).ToArray());
    }

    [Fact]
    public void SolidTilesAceitaStringSeparadaPorVirgula()
    {
        // Formato que o editor grava no campo de texto do Inspector.
        const string json = """
            {
              "Scene": "Teste",
              "Objects": [
                { "Name": "Mapa", "Components": [
                  { "Type": "Tilemap", "Width": 2, "Height": 2, "SolidTiles": "1, 3 ,5" }
                ]}
              ]
            }
            """;

        var world = new World();
        new SceneSerializer().Load(json, new SceneContext { World = world });

        var map = Achar(world, "Mapa").Get<Tilemap>();
        Assert.NotNull(map);
        Assert.Equal(new[] { 1, 3, 5 }, map.SolidTiles.OrderBy(i => i).ToArray());
    }

    [Fact]
    public void VariasEntidadesEComponentesSobrevivemJuntos()
    {
        var world = new World();
        var player = world.CreateEntity("Player");
        player.Add(new Transform(10f, 20f));
        player.Add(new Collider { Width = 12f, Height = 12f });
        player.Add(new Health { Max = 50f, Current = 50f });

        var parede = world.CreateEntity("Parede");
        parede.Add(new Transform(100f, 0f));
        parede.Add(new Collider { IsKinematic = true });

        var destino = Roundtrip(world);

        Assert.Equal(2, destino.EntityCount);
        var playerRecarregado = Achar(destino, "Player");
        Assert.NotNull(playerRecarregado.Get<Transform>());
        Assert.NotNull(playerRecarregado.Get<Collider>());
        Assert.Equal(50f, playerRecarregado.Get<Health>()!.Max, Tolerance);
        Assert.True(Achar(destino, "Parede").Get<Collider>()!.IsKinematic);
    }

    [Fact]
    public void ComponenteDesconhecidoEIgnoradoSemDerrubarACena()
    {
        const string json = """
            {
              "Scene": "Teste",
              "Objects": [
                { "Name": "Player", "Components": [
                  { "Type": "Transform", "X": 5, "Y": 6 },
                  { "Type": "ComponenteQueNaoExiste", "Foo": 1 }
                ]}
              ]
            }
            """;

        var world = new World();
        new SceneSerializer().Load(json, new SceneContext { World = world });

        var transform = Achar(world, "Player").Get<Transform>();
        Assert.NotNull(transform);
        Assert.Equal(new Vector2(5f, 6f), transform.Position);
    }

    [Fact]
    public void ComponenteSemTypeLancaErroClaro()
    {
        const string json = """
            { "Scene": "Teste", "Objects": [ { "Name": "Player", "Components": [ { "X": 1 } ] } ] }
            """;

        var world = new World();

        Assert.Throws<KeyNotFoundException>(
            () => new SceneSerializer().Load(json, new SceneContext { World = world }));
    }

    [Fact]
    public void EntidadeSemComponentesEhCriadaMesmoAssim()
    {
        const string json = """
            { "Scene": "Teste", "Objects": [ { "Name": "Vazia" } ] }
            """;

        var world = new World();
        new SceneSerializer().Load(json, new SceneContext { World = world });

        Assert.Equal(1, world.EntityCount);
        Assert.True(world.TryFind("Vazia", out _));
    }

    // ---- Scripts custom ----

    [Fact]
    public void ScriptMarcadoComSceneScriptFazRoundtripPorReflection()
    {
        var serializer = new SceneSerializer();
        serializer.RegisterScripts(typeof(ScriptDeTeste).Assembly);

        var world = new World();
        world.CreateEntity("Player").Add(new ScriptDeTeste
        {
            Velocidade = 42.5f,
            Vidas = 7,
            Hostil = true,
            Alvo = "Boss",
        });

        var script = Achar(Roundtrip(world, serializer), "Player").Get<ScriptDeTeste>();

        Assert.NotNull(script);
        Assert.Equal(42.5f, script.Velocidade, Tolerance);
        Assert.Equal(7, script.Vidas);
        Assert.True(script.Hostil);
        Assert.Equal("Boss", script.Alvo);
    }

    [Fact]
    public void CampoDeTipoNaoSuportadoEIgnoradoSemQuebrar()
    {
        var serializer = new SceneSerializer();
        serializer.RegisterScripts(typeof(ScriptDeTeste).Assembly);

        var world = new World();
        world.CreateEntity("Player").Add(new ScriptDeTeste { NaoSerializavel = new Vector2(1f, 2f) });

        var script = Achar(Roundtrip(world, serializer), "Player").Get<ScriptDeTeste>();

        Assert.NotNull(script);
        Assert.Equal(new Vector2(9f, 9f), script.NaoSerializavel); // volta ao default da classe
    }

    [Fact]
    public void DescribeScriptsRelataOsCamposComOsDefaultsReais()
    {
        var scripts = new SceneSerializer().DescribeScripts(typeof(ScriptDeTeste).Assembly);

        var info = Assert.Single(scripts, s => s.Name == "ScriptDeTeste");
        Assert.Equal("1.5", Assert.Single(info.Fields, f => f.Name == "Velocidade").Default);
        Assert.Equal("3", Assert.Single(info.Fields, f => f.Name == "Vidas").Default);
        Assert.Equal("false", Assert.Single(info.Fields, f => f.Name == "Hostil").Default);
        Assert.Equal("float", Assert.Single(info.Fields, f => f.Name == "Velocidade").Kind);
        Assert.DoesNotContain(info.Fields, f => f.Name == "NaoSerializavel");
    }

    [Fact]
    public void DescribeScriptsUsaODefaultRealDeEnabledHerdadoDeBehavior()
    {
        // Enabled vem de Behavior e vale true; um chute por tipo diria false e o editor
        // mostraria scripts desligados por padrão.
        var scripts = new SceneSerializer().DescribeScripts(typeof(ScriptDeTeste).Assembly);

        var info = Assert.Single(scripts, s => s.Name == "ScriptDeTeste");
        Assert.Equal("true", Assert.Single(info.Fields, f => f.Name == "Enabled").Default);
    }

    [Fact]
    public void ComponentesDeGameplayFazemRoundtripPelosNomesDosCampos()
    {
        // Registro reflexivo: o nome do campo É o nome no JSON. Este teste é o que percebe se
        // alguém renomear um campo de componente e quebrar as cenas já salvas.
        var world = new World();
        var player = world.CreateEntity("Player");
        player.Add(new TopDownController { Speed = 210f, JoystickName = "MoveStick", FlipSpriteByDirection = false });
        player.Add(new AttackSpawner { Prefab = "prefabs/corte.json", AimMode = "Mouse", DirectionSnap = 8, Cooldown = 0.2f });

        var slime = world.CreateEntity("Slime");
        slime.Add(new ContactDamage { Damage = 14f, TargetPrefix = "Player", Knockback = 30f });
        slime.Add(new AutoMotion { RotateSpeedDegrees = 45f, BobAmplitude = 6f });
        slime.Add(new Lifetime { Seconds = 3f, DestroyOnAnimationEnd = true });
        slime.Add(new FollowTarget { TargetName = "Player", OffsetX = 5f, FollowSpeed = 80f });

        var destino = Roundtrip(world);

        var controller = Achar(destino, "Player").Get<TopDownController>()!;
        Assert.Equal(210f, controller.Speed, Tolerance);
        Assert.Equal("MoveStick", controller.JoystickName);
        Assert.False(controller.FlipSpriteByDirection);

        var attack = Achar(destino, "Player").Get<AttackSpawner>()!;
        Assert.Equal("prefabs/corte.json", attack.Prefab);
        Assert.Equal("Mouse", attack.AimMode);
        Assert.Equal(8, attack.DirectionSnap);

        var contact = Achar(destino, "Slime").Get<ContactDamage>()!;
        Assert.Equal(14f, contact.Damage, Tolerance);
        Assert.Equal("Player", contact.TargetPrefix);
        Assert.Equal(30f, contact.Knockback, Tolerance);

        Assert.Equal(45f, Achar(destino, "Slime").Get<AutoMotion>()!.RotateSpeedDegrees, Tolerance);
        Assert.True(Achar(destino, "Slime").Get<Lifetime>()!.DestroyOnAnimationEnd);
        Assert.Equal(80f, Achar(destino, "Slime").Get<FollowTarget>()!.FollowSpeed, Tolerance);
    }

    [Fact]
    public void EstadoDeRuntimeDosComponentesNaoVazaProJson()
    {
        // Facing/Velocity/Age/CooldownRemaining são "{ get; private set; }" — leitura pra HUD e
        // script, não campo de cena. Se vazassem, apareceriam no inspector como se fossem
        // autoráveis e o editor mostraria um valor que o jogo sobrescreve no primeiro frame.
        var world = new World();
        world.CreateEntity("Player").Add(new TopDownController());

        string json = new SceneSerializer().Save("Teste", new SceneContext { World = world });

        Assert.DoesNotContain("Facing", json);
        Assert.DoesNotContain("Velocity", json);
    }

    [Fact]
    public void NavAgentGuardaOFollowNaCena()
    {
        var world = new World();
        world.CreateEntity("Slime").Add(new NavAgent { Follow = "Player", FollowRange = 400f });

        var agent = Achar(Roundtrip(world), "Slime").Get<NavAgent>()!;

        Assert.Equal("Player", agent.Follow);
        Assert.Equal(400f, agent.FollowRange, Tolerance);
    }

    [Fact]
    public void ComponenteSemWriterNaoAparaNoSave()
    {
        // RecordingBehavior não tem [SceneScript] nem registro manual — não deve virar JSON,
        // e o load do resultado não pode quebrar por causa disso.
        var world = new World();
        var player = world.CreateEntity("Player");
        player.Add(new Transform(1f, 1f));
        player.Add(new RecordingBehavior());

        var destino = Roundtrip(world);

        Assert.NotNull(Achar(destino, "Player").Get<Transform>());
        Assert.Null(Achar(destino, "Player").Get<RecordingBehavior>());
    }

    [Fact]
    public void SizeDoSpriteSobreviveAoRoundtrip()
    {
        var world = new World();
        world.CreateEntity("Slime").Add(new SpriteRenderer { Size = new Vector2(28f, 14f) });

        var sprite = Achar(Roundtrip(world), "Slime").Get<SpriteRenderer>()!;

        Assert.NotNull(sprite.Size);
        Assert.Equal(28f, sprite.Size!.Value.X, Tolerance);
        Assert.Equal(14f, sprite.Size!.Value.Y, Tolerance);
    }

    [Fact]
    public void SpriteSemSizeContinuaComTamanhoNatural()
    {
        // Size null não é "size zero": quer dizer desenhar no tamanho da textura. Se o
        // roundtrip materializasse um Vector2.Zero aqui, todo sprite salvo pelo editor sem
        // tamanho explícito sumiria da tela ao recarregar.
        var world = new World();
        world.CreateEntity("Slime").Add(new SpriteRenderer { Layer = 3 });

        Assert.Null(Achar(Roundtrip(world), "Slime").Get<SpriteRenderer>()!.Size);
    }

    [Theory]
    [InlineData(28f, 0f)]
    [InlineData(0f, 28f)]
    [InlineData(0f, 0f)]
    public void SizeComEixoZeradoNoJsonViraTamanhoNatural(float sizeX, float sizeY)
    {
        // Campo em branco no inspector do editor chega aqui como 0. Virar um lado de tamanho
        // zero deixaria o sprite invisível sem nenhum erro explicando; "natural" é o que o
        // autor da cena quis dizer ao não preencher.
        string json = $$"""
            {
              "Scene": "Teste",
              "Objects": [ { "Name": "Slime", "Components": [
                { "Type": "SpriteRenderer", "SizeX": {{sizeX}}, "SizeY": {{sizeY}} } ] } ]
            }
            """;

        var world = new World();
        new SceneSerializer().Load(json, new SceneContext { World = world });

        Assert.Null(Achar(world, "Slime").Get<SpriteRenderer>()!.Size);
    }
}
