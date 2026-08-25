using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Mapa grande com muita entidade. O tamanho do mapa em si nunca custou nada (tile é desenhado e
/// colidido só perto); o que crescia era o número de entidades — colisão todos-contra-todos e
/// desenho de sprite fora da tela. Estes testes prendem que as otimizações não mudaram NADA do
/// comportamento: mesma colisão, mesmo empurrão, mesmo trigger.
/// </summary>
public class BigMapScalingTests
{
    private const float Tolerance = 0.01f;

    /// <summary>Acima de 32 colliders o World troca o todos-contra-todos pela grade espacial.
    /// Os testes usam este número pra garantir que estão exercitando o caminho da grade.</summary>
    private const int AcimaDoLimiteDaGrade = 40;

    private static Entity Solid(World world, string name, Vector2 position, float size = 32f)
    {
        var entity = world.CreateEntity(name);
        entity.Add(new Transform(position));
        entity.Add(new Collider { Width = size, Height = size, IsSolid = true });
        return entity;
    }

    /// <summary>Enche a cena de colliders longe de tudo, só pra empurrar a contagem acima do
    /// limite e ligar a grade.</summary>
    private static void Encher(World world, int count)
    {
        for (int i = 0; i < count; i++)
            Solid(world, $"longe{i}", new Vector2(10_000f + i * 500f, 10_000f + i * 500f));
    }

    [Fact]
    public void GradeEmpurraIgualAoCaminhoSimples()
    {
        // Mesma geometria nos dois mundos; um roda todos-contra-todos, o outro a grade.
        static (Vector2 A, Vector2 B) Rodar(int enchimento)
        {
            var world = new World();
            var a = Solid(world, "a", new Vector2(0f, 0f));
            var b = Solid(world, "b", new Vector2(20f, 0f));
            Encher(world, enchimento);

            world.Update(0.016f);
            return (a.Get<Transform>()!.Position, b.Get<Transform>()!.Position);
        }

        var simples = Rodar(0);
        var comGrade = Rodar(AcimaDoLimiteDaGrade);

        Assert.Equal(simples.A.X, comGrade.A.X, Tolerance);
        Assert.Equal(simples.A.Y, comGrade.A.Y, Tolerance);
        Assert.Equal(simples.B.X, comGrade.B.X, Tolerance);
        Assert.Equal(simples.B.Y, comGrade.B.Y, Tolerance);

        // E empurrou de verdade: sem separação o teste acima passaria com os dois parados.
        Assert.True(comGrade.B.X - comGrade.A.X > 20f);
    }

    [Fact]
    public void ColisorGrandeEmVariasCelulasNaoEmpurraDuasVezes()
    {
        // Collider maior que a célula da grade entra em várias células. Sem deduplicar o par, o
        // mesmo empurrão seria aplicado uma vez por célula compartilhada.
        var world = new World();
        var a = Solid(world, "grandeA", new Vector2(0f, 0f), size: 300f);
        var b = Solid(world, "grandeB", new Vector2(100f, 0f), size: 300f);
        Encher(world, AcimaDoLimiteDaGrade);

        world.Update(0.016f);

        float separacao = b.Get<Transform>()!.Position.X - a.Get<Transform>()!.Position.X;

        // Empurrão único: os dois se separam até encostar (300), não além.
        Assert.Equal(300f, separacao, 1f);
    }

    [Fact]
    public void TriggerEntraUmaVezSoComGrade()
    {
        var world = new World();

        var zona = world.CreateEntity("zona");
        zona.Add(new Transform(Vector2.Zero));
        zona.Add(new Collider { Width = 200f, Height = 200f, IsSolid = false });
        var registro = zona.Add(new RecordingBehavior());

        var player = world.CreateEntity("Player");
        player.Add(new Transform(new Vector2(10f, 10f)));
        player.Add(new Collider { Width = 32f, Height = 32f, IsSolid = false });

        Encher(world, AcimaDoLimiteDaGrade);

        world.Update(0.016f);
        world.Update(0.016f);

        Assert.Single(registro.TriggerEnters);
        Assert.Equal("Player", registro.TriggerEnters[0].Name);
    }

    [Fact]
    public void QuemEstaLongeNaoColideComGrade()
    {
        var world = new World();

        var a = world.CreateEntity("a");
        a.Add(new Transform(Vector2.Zero));
        a.Add(new Collider { Width = 32f, Height = 32f, IsSolid = true });
        var registro = a.Add(new RecordingBehavior());

        Solid(world, "b", new Vector2(5_000f, 5_000f));
        Encher(world, AcimaDoLimiteDaGrade);

        world.Update(0.016f);

        Assert.Empty(registro.CollisionsWith);
    }

    // ---------- Limites do mapa pra câmera ----------

    [Fact]
    public void LimitesSaemDosTilemapsDaCena()
    {
        var world = new World();
        var entity = world.CreateEntity("mapa");
        entity.Add(new Transform(new Vector2(100f, 50f)));
        entity.Add(new Tilemap { Width = 40, Height = 30, TileWidth = 16, TileHeight = 16 });

        var bounds = world.TilemapWorldBounds();

        Assert.NotNull(bounds);
        Assert.Equal(100f, bounds!.Value.Min.X, Tolerance);
        Assert.Equal(50f, bounds.Value.Min.Y, Tolerance);
        Assert.Equal(100f + 40 * 16, bounds.Value.Max.X, Tolerance);
        Assert.Equal(50f + 30 * 16, bounds.Value.Max.Y, Tolerance);
    }

    [Fact]
    public void CenaSemTilemapNaoTemLimites()
    {
        var world = new World();
        world.CreateEntity("solto").Add(new Transform(Vector2.Zero));

        Assert.Null(world.TilemapWorldBounds());
    }

    // ---------- Dormir fora da tela ----------

    private static (World World, RecordingBehavior Longe, RecordingBehavior Perto) MundoComSonolentos()
    {
        var world = new World { Camera = new Camera2D() };
        world.Camera!.SetViewport(1280, 720);
        world.Camera.Position = Vector2.Zero;

        var perto = world.CreateEntity("perto");
        perto.Add(new Transform(Vector2.Zero));
        perto.Add(new SleepOffscreen());
        var registroPerto = perto.Add(new RecordingBehavior());

        var longe = world.CreateEntity("longe");
        longe.Add(new Transform(new Vector2(9_000f, 0f)));
        longe.Add(new SleepOffscreen());
        var registroLonge = longe.Add(new RecordingBehavior());

        return (world, registroLonge, registroPerto);
    }

    [Fact]
    public void QuemEstaForaDaTelaNaoRodaUpdate()
    {
        var (world, longe, perto) = MundoComSonolentos();

        world.Update(0.016f);
        world.Update(0.016f);

        Assert.Equal(2, perto.UpdateCount);
        Assert.Equal(0, longe.UpdateCount);
    }

    [Fact]
    public void AcordaQuandoACameraChega()
    {
        var (world, longe, _) = MundoComSonolentos();

        world.Update(0.016f);
        Assert.Equal(0, longe.UpdateCount);

        world.Camera!.Position = new Vector2(9_000f, 0f);
        world.Update(0.016f);

        Assert.Equal(1, longe.UpdateCount);
    }

    [Fact]
    public void SemCameraNinguemDorme()
    {
        // Ferramenta, teste, servidor sem tela: não há "fora da tela" pra falar.
        var world = new World();

        var entity = world.CreateEntity("longe");
        entity.Add(new Transform(new Vector2(9_000f, 0f)));
        entity.Add(new SleepOffscreen());
        var registro = entity.Add(new RecordingBehavior());

        world.Update(0.016f);

        Assert.Equal(1, registro.UpdateCount);
    }

    [Fact]
    public void SemOComponenteNinguemDorme()
    {
        var world = new World { Camera = new Camera2D() };
        world.Camera!.SetViewport(1280, 720);

        var entity = world.CreateEntity("longe");
        entity.Add(new Transform(new Vector2(9_000f, 0f)));
        var registro = entity.Add(new RecordingBehavior());

        world.Update(0.016f);

        Assert.Equal(1, registro.UpdateCount);
    }
}
