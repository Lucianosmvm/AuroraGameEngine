using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Events;
using Aurora.Runtime.Scenes;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Mirar um GRUPO de entidades. Um jogo tem slime, slimeazul e slime_de_gelo: sem etiqueta, cada
/// ação de evento acerta uma entidade só (nome exato) e o autor teria que repetir a ação por tipo
/// de bicho — e ainda assim erraria os que nascem em jogo. Estes testes prendem as três formas do
/// campo de alvo (Self, nome exato, #etiqueta) e o corte por alcance.
/// </summary>
public class TagTargetingTests
{
    private const float Tolerance = 0.01f;

    private static (World World, EventSystem Events) Build()
    {
        var world = new World();
        return (world, new EventSystem(world, new GameState()));
    }

    private static Entity Enemy(World world, string name, string tags, Vector2 position)
    {
        var entity = world.CreateEntity(name);
        entity.Add(new Transform(position));
        entity.Add(new Health { Max = 100f, Current = 100f });
        entity.Add(new Tags { Value = tags });
        return entity;
    }

    /// <summary>Dispara uma lista de ações uma vez, com origem em <paramref name="origin"/>.</summary>
    private static void Fire(World world, EventSystem events, Vector2 origin, params EventAction[] actions)
    {
        var source = world.CreateEntity("Gatilho");
        source.Add(new Transform(origin));
        source.Add(new EventTrigger { Trigger = "SceneStart", Once = true, Actions = [.. actions] });
        events.Update(0.016f);
    }

    [Fact]
    public void EtiquetaAtingeTodoInimigoQualquerQueSejaONome()
    {
        var (world, events) = Build();
        var slime = Enemy(world, "slime", "inimigo", new Vector2(10f, 0f));
        var azul = Enemy(world, "slimeazul", "inimigo", new Vector2(20f, 0f));
        var gelo = Enemy(world, "slime_de_gelo", "inimigo", new Vector2(30f, 0f));
        var player = Enemy(world, "Player", "", new Vector2(15f, 0f));

        Fire(world, events, Vector2.Zero,
            new EventAction { Type = "Damage", Name = "#inimigo", Value = 40f });

        Assert.Equal(60f, slime.Get<Health>()!.Current, Tolerance);
        Assert.Equal(60f, azul.Get<Health>()!.Current, Tolerance);
        Assert.Equal(60f, gelo.Get<Health>()!.Current, Tolerance);

        // Quem não tem a etiqueta fica de fora, mesmo estando no meio deles.
        Assert.Equal(100f, player.Get<Health>()!.Current, Tolerance);
    }

    [Fact]
    public void AlcanceCortaQuemEstaLongeDeQuemDisparou()
    {
        var (world, events) = Build();
        var perto = Enemy(world, "slime", "inimigo", new Vector2(30f, 0f));
        var longe = Enemy(world, "slimeazul", "inimigo", new Vector2(300f, 0f));

        Fire(world, events, Vector2.Zero,
            new EventAction { Type = "Damage", Name = "#inimigo", Value = 50f, Radius = 100f });

        Assert.Equal(50f, perto.Get<Health>()!.Current, Tolerance);
        Assert.Equal(100f, longe.Get<Health>()!.Current, Tolerance);
    }

    [Fact]
    public void SemAlcanceOGrupoInteiroApanha()
    {
        var (world, events) = Build();
        var longe = Enemy(world, "slimeazul", "inimigo", new Vector2(5000f, 5000f));

        Fire(world, events, Vector2.Zero,
            new EventAction { Type = "Damage", Name = "#inimigo", Value = 50f });

        Assert.Equal(50f, longe.Get<Health>()!.Current, Tolerance);
    }

    [Fact]
    public void UmaEntidadePodeEstarEmVariosGrupos()
    {
        var (world, events) = Build();
        var morcego = Enemy(world, "morcego", "inimigo, voador", new Vector2(10f, 0f));
        var slime = Enemy(world, "slime", "inimigo", new Vector2(20f, 0f));

        Fire(world, events, Vector2.Zero,
            new EventAction { Type = "Damage", Name = "#voador", Value = 30f });

        Assert.Equal(70f, morcego.Get<Health>()!.Current, Tolerance);
        Assert.Equal(100f, slime.Get<Health>()!.Current, Tolerance);
    }

    [Fact]
    public void NomeExatoContinuaAtingindoUmaEntidadeSo()
    {
        // Cenas antigas dependem disto: nome não é único, e a ação sempre acertou a mais antiga.
        var (world, events) = Build();
        var primeiro = Enemy(world, "slime", "inimigo", new Vector2(10f, 0f));
        var segundo = Enemy(world, "slime", "inimigo", new Vector2(20f, 0f));

        Fire(world, events, Vector2.Zero,
            new EventAction { Type = "Damage", Name = "slime", Value = 40f });

        Assert.Equal(60f, primeiro.Get<Health>()!.Current, Tolerance);
        Assert.Equal(100f, segundo.Get<Health>()!.Current, Tolerance);
    }

    [Fact]
    public void CuraETambemDestroiPorEtiqueta()
    {
        var (world, events) = Build();
        var ferido = Enemy(world, "aliado", "amigo", new Vector2(10f, 0f));
        ferido.Get<Health>()!.Current = 20f;
        var descartavel = Enemy(world, "caixa", "cenario", new Vector2(20f, 0f));

        Fire(world, events, Vector2.Zero,
            new EventAction { Type = "Heal", Name = "#amigo", Value = 50f },
            new EventAction { Type = "Destroy", Name = "#cenario" });

        Assert.Equal(70f, ferido.Get<Health>()!.Current, Tolerance);
        Assert.False(world.IsAlive(descartavel.Id));
    }

    [Fact]
    public void EtiquetaInexistenteNaoAtingeNinguemENaoQuebra()
    {
        var (world, events) = Build();
        var slime = Enemy(world, "slime", "inimigo", new Vector2(10f, 0f));

        Fire(world, events, Vector2.Zero,
            new EventAction { Type = "Damage", Name = "#chefe", Value = 999f });

        Assert.Equal(100f, slime.Get<Health>()!.Current, Tolerance);
    }

    [Fact]
    public void ContactDamageMiraPorEtiquetaAlemDePrefixo()
    {
        var (world, _) = Build();
        var espinho = world.CreateEntity("Espinho");
        var damage = new ContactDamage { Damage = 15f, Interval = 0f, TargetPrefix = "#inimigo" };
        espinho.Add(new Transform(Vector2.Zero));
        espinho.Add(damage);

        var slime = Enemy(world, "slimeazul", "inimigo", new Vector2(4f, 0f));
        var player = Enemy(world, "Player", "", new Vector2(4f, 0f));

        damage.OnTriggerEnter(slime);
        damage.OnTriggerEnter(player);

        Assert.Equal(85f, slime.Get<Health>()!.Current, Tolerance);
        Assert.Equal(100f, player.Get<Health>()!.Current, Tolerance);
    }

    [Fact]
    public void EtiquetasSobrevivemAoSalvarECarregarACena()
    {
        var serializer = new SceneSerializer();
        var world = new World();
        var entity = world.CreateEntity("slimeazul");
        entity.Add(new Transform(new Vector2(5f, 6f)));
        entity.Add(new Tags { Value = "inimigo, voador" });

        string json = serializer.Save("Teste", new SceneContext { World = world });

        var reloaded = new World();
        serializer.Load(json, new SceneContext { World = reloaded });

        var tags = reloaded.Entities.Single(e => e.Name == "slimeazul").Get<Tags>();
        Assert.NotNull(tags);
        Assert.True(tags!.Has("inimigo"));
        Assert.True(tags.Has("#voador"));
        Assert.False(tags.Has("chefe"));
    }

    [Fact]
    public void AlcanceSobreviveAoSalvarECarregarACena()
    {
        var action = new EventAction { Type = "Damage", Name = "#inimigo", Value = 40f, Radius = 120f };
        var serializer = new SceneSerializer();
        var world = new World();
        var entity = world.CreateEntity("Bomba");
        entity.Add(new Transform(Vector2.Zero));
        entity.Add(new EventTrigger { Trigger = "PlayerTouch", Actions = [action] });

        string json = serializer.Save("Teste", new SceneContext { World = world });

        var reloaded = new World();
        serializer.Load(json, new SceneContext { World = reloaded });

        var loaded = reloaded.Entities.Single(e => e.Name == "Bomba").Get<EventTrigger>()!.Actions.Single();
        Assert.Equal("#inimigo", loaded.Name);
        Assert.Equal(120f, loaded.Radius, Tolerance);
    }
}
