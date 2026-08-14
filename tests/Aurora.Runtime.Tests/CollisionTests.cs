using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Colisão e resolução do <see cref="World"/>. Os testes passam por <c>World.Update</c>
/// porque <c>Overlap</c>/<c>BoxBox</c>/<c>Resolve</c> são privados — a detecção só é
/// observável pelo efeito (posição corrigida) e pelos callbacks dos Behaviors.
/// </summary>
public class CollisionTests
{
    private const float Tolerance = 0.001f;

    private static (Entity Entity, Transform Transform, RecordingBehavior Recorder) Spawn(
        World world, string name, Vector2 position, Collider collider)
    {
        var entity = world.CreateEntity(name);
        var transform = entity.Add(new Transform(position));
        entity.Add(collider);
        var recorder = entity.Add(new RecordingBehavior());
        return (entity, transform, recorder);
    }

    private static Collider Box(float size = 16f, bool solid = true, bool kinematic = false)
        => new() { Shape = ColliderShape.Box, Width = size, Height = size, IsSolid = solid, IsKinematic = kinematic };

    private static Collider Circle(float radius = 8f, bool solid = true, bool kinematic = false)
        => new() { Shape = ColliderShape.Circle, Radius = radius, IsSolid = solid, IsKinematic = kinematic };

    [Fact]
    public void CorpoDinamicoEEmpurradoParaForaDoKinematico()
    {
        var world = new World();
        Spawn(world, "Parede", new Vector2(0f, 0f), Box(16f, kinematic: true));
        var (_, dinamico, _) = Spawn(world, "Player", new Vector2(10f, 0f), Box(16f));

        world.Update(1f / 60f);

        // Sobreposição de 6px no eixo X (menor que a de Y) — só X é corrigido, e a separação
        // final é exatamente a soma das meias-larguras.
        Assert.Equal(16f, dinamico.Position.X, Tolerance);
        Assert.Equal(0f, dinamico.Position.Y, Tolerance);
    }

    [Fact]
    public void KinematicoNaoEMovidoPelaResolucao()
    {
        var world = new World();
        var (_, parede, _) = Spawn(world, "Parede", new Vector2(0f, 0f), Box(16f, kinematic: true));
        Spawn(world, "Player", new Vector2(10f, 0f), Box(16f));

        world.Update(1f / 60f);

        Assert.Equal(Vector2.Zero, parede.Position);
    }

    [Fact]
    public void DoisDinamicosDividemACorrecaoAoMeio()
    {
        var world = new World();
        var (_, a, _) = Spawn(world, "A", new Vector2(0f, 0f), Box(16f));
        var (_, b, _) = Spawn(world, "B", new Vector2(10f, 0f), Box(16f));

        world.Update(1f / 60f);

        // 6px de sobreposição divididos: 3 pra cada lado. Centro do par não se move.
        Assert.Equal(-3f, a.Position.X, Tolerance);
        Assert.Equal(13f, b.Position.X, Tolerance);
        Assert.Equal(16f, b.Position.X - a.Position.X, Tolerance);
    }

    [Fact]
    public void ResolucaoUsaOEixoDeMenorPenetracao()
    {
        var world = new World();
        Spawn(world, "Chao", new Vector2(0f, 0f), Box(16f, kinematic: true));
        // Muito sobreposto em X (14px) e pouco em Y (2px): a saída correta é pra cima/baixo.
        var (_, player, _) = Spawn(world, "Player", new Vector2(2f, 14f), Box(16f));

        world.Update(1f / 60f);

        Assert.Equal(2f, player.Position.X, Tolerance);
        Assert.Equal(16f, player.Position.Y, Tolerance);
    }

    [Fact]
    public void CirculosSeparamAoLongoDaLinhaDeCentros()
    {
        var world = new World();
        Spawn(world, "A", new Vector2(0f, 0f), Circle(8f, kinematic: true));
        var (_, b, _) = Spawn(world, "B", new Vector2(10f, 0f), Circle(8f));

        world.Update(1f / 60f);

        Assert.Equal(16f, b.Position.Length(), Tolerance);
    }

    [Fact]
    public void CaixaECirculoColidem()
    {
        var world = new World();
        Spawn(world, "Caixa", new Vector2(0f, 0f), Box(16f, kinematic: true));
        var (_, circulo, recorder) = Spawn(world, "Bola", new Vector2(12f, 0f), Circle(8f));

        world.Update(1f / 60f);

        Assert.Single(recorder.CollisionsWith);
        Assert.Equal(16f, circulo.Position.X, Tolerance);
    }

    [Fact]
    public void CirculoECaixaColidemNaOrdemInversa()
    {
        // Mesmo caso do teste anterior com os papéis trocados: exercita o caminho CircleBox,
        // que delega pro BoxCircle com os argumentos invertidos e precisa virar a normal.
        var world = new World();
        Spawn(world, "Bola", new Vector2(0f, 0f), Circle(8f, kinematic: true));
        var (_, caixa, recorder) = Spawn(world, "Caixa", new Vector2(12f, 0f), Box(16f));

        world.Update(1f / 60f);

        Assert.Single(recorder.CollisionsWith);
        Assert.Equal(16f, caixa.Position.X, Tolerance);
    }

    [Fact]
    public void CentroDoCirculoDentroDaCaixaSaiPeloEixoMaisCurto()
    {
        var world = new World();
        Spawn(world, "Caixa", new Vector2(0f, 0f), Box(32f, kinematic: true));
        // Centro do círculo dentro da caixa, mais perto da borda de baixo que da direita.
        var (_, bola, _) = Spawn(world, "Bola", new Vector2(4f, 14f), Circle(4f));

        world.Update(1f / 60f);

        Assert.Equal(4f, bola.Position.X, Tolerance);
        Assert.Equal(20f, bola.Position.Y, Tolerance); // borda da caixa (16) + raio (4)
    }

    [Fact]
    public void SemSobreposicaoNaoHaCallbackNemMovimento()
    {
        var world = new World();
        var (_, a, ra) = Spawn(world, "A", new Vector2(0f, 0f), Box(16f));
        var (_, b, rb) = Spawn(world, "B", new Vector2(100f, 0f), Box(16f));

        world.Update(1f / 60f);

        Assert.Empty(ra.CollisionsWith);
        Assert.Empty(rb.CollisionsWith);
        Assert.Equal(Vector2.Zero, a.Position);
        Assert.Equal(new Vector2(100f, 0f), b.Position);
    }

    [Fact]
    public void MascarasIncompativeisIgnoramOPar()
    {
        var world = new World();
        var paredeCollider = Box(16f, kinematic: true);
        paredeCollider.Layer = 1;
        paredeCollider.Mask = 2; // só interage com a camada 2

        var playerCollider = Box(16f);
        playerCollider.Layer = 4; // não é 2
        playerCollider.Mask = 8;  // não é 1

        Spawn(world, "Parede", new Vector2(0f, 0f), paredeCollider);
        var (_, player, recorder) = Spawn(world, "Player", new Vector2(10f, 0f), playerCollider);

        world.Update(1f / 60f);

        Assert.Empty(recorder.CollisionsWith);
        Assert.Equal(10f, player.Position.X, Tolerance);
    }

    [Fact]
    public void MascaraDeUmLadoSoJaBastaParaColidir()
    {
        // A regra é OR: basta um dos dois enxergar a camada do outro. Isso evita que um
        // collider "invisível" pra um lado silencie a colisão inteira.
        var world = new World();
        var paredeCollider = Box(16f, kinematic: true);
        paredeCollider.Layer = 1;
        paredeCollider.Mask = 0; // não enxerga ninguém

        var playerCollider = Box(16f);
        playerCollider.Layer = 4;
        playerCollider.Mask = 1; // enxerga a parede

        Spawn(world, "Parede", new Vector2(0f, 0f), paredeCollider);
        var (_, player, recorder) = Spawn(world, "Player", new Vector2(10f, 0f), playerCollider);

        world.Update(1f / 60f);

        Assert.Single(recorder.CollisionsWith);
        Assert.Equal(16f, player.Position.X, Tolerance);
    }

    [Fact]
    public void OffsetDeslocaAHitbox()
    {
        var world = new World();
        var comOffset = Box(16f, kinematic: true);
        comOffset.Offset = new Vector2(100f, 0f); // hitbox longe do Transform

        Spawn(world, "Parede", new Vector2(0f, 0f), comOffset);
        var (_, player, recorder) = Spawn(world, "Player", new Vector2(10f, 0f), Box(16f));

        world.Update(1f / 60f);

        // Sem o offset os dois estariam sobrepostos; com ele, a hitbox está a 100px de distância.
        Assert.Empty(recorder.CollisionsWith);
        Assert.Equal(10f, player.Position.X, Tolerance);
    }

    [Fact]
    public void NormalRecebidaApontaParaForaDoOutro()
    {
        var world = new World();
        Spawn(world, "Parede", new Vector2(0f, 0f), Box(16f, kinematic: true));
        var (_, _, recorder) = Spawn(world, "Player", new Vector2(10f, 0f), Box(16f));

        world.Update(1f / 60f);

        var info = Assert.Single(recorder.CollisionInfos);
        // O player está à direita da parede, então ele sai pra direita.
        Assert.Equal(1f, info.Normal.X, Tolerance);
        Assert.Equal(0f, info.Normal.Y, Tolerance);
        Assert.Equal(6f, info.Depth, Tolerance);
    }

    [Fact]
    public void ColisaoNotificaOsDoisLados()
    {
        var world = new World();
        var (parede, _, recorderParede) = Spawn(world, "Parede", new Vector2(0f, 0f), Box(16f, kinematic: true));
        var (player, _, recorderPlayer) = Spawn(world, "Player", new Vector2(10f, 0f), Box(16f));

        world.Update(1f / 60f);

        Assert.Equal(player, Assert.Single(recorderParede.CollisionsWith));
        Assert.Equal(parede, Assert.Single(recorderPlayer.CollisionsWith));
    }

    // ---- Triggers ----

    [Fact]
    public void TriggerNaoEmpurraMasNotificaOsDoisLados()
    {
        var world = new World();
        var (zona, zonaTransform, recorderZona) = Spawn(world, "Zona", new Vector2(0f, 0f), Box(16f, solid: false));
        var (player, playerTransform, recorderPlayer) = Spawn(world, "Player", new Vector2(10f, 0f), Box(16f));

        world.Update(1f / 60f);

        Assert.Equal(Vector2.Zero, zonaTransform.Position);
        Assert.Equal(new Vector2(10f, 0f), playerTransform.Position);
        Assert.Equal(player, Assert.Single(recorderZona.TriggerEnters));
        Assert.Equal(zona, Assert.Single(recorderPlayer.TriggerEnters));
        Assert.Empty(recorderPlayer.CollisionsWith);
    }

    [Fact]
    public void TriggerEnterDisparaUmaVezSoEnquantoASobreposicaoDura()
    {
        var world = new World();
        Spawn(world, "Zona", new Vector2(0f, 0f), Box(16f, solid: false));
        var (_, _, recorder) = Spawn(world, "Player", new Vector2(10f, 0f), Box(16f));

        for (int i = 0; i < 5; i++)
            world.Update(1f / 60f);

        Assert.Single(recorder.TriggerEnters);
        Assert.Empty(recorder.TriggerExits);
    }

    [Fact]
    public void TriggerExitDisparaQuandoASobreposicaoTermina()
    {
        var world = new World();
        var (zona, _, _) = Spawn(world, "Zona", new Vector2(0f, 0f), Box(16f, solid: false));
        var (_, player, recorder) = Spawn(world, "Player", new Vector2(10f, 0f), Box(16f));

        world.Update(1f / 60f);
        Assert.Single(recorder.TriggerEnters);

        player.Position = new Vector2(500f, 0f);
        world.Update(1f / 60f);

        Assert.Equal(zona, Assert.Single(recorder.TriggerExits));
    }

    [Fact]
    public void ReentrarNaZonaDisparaTriggerEnterDeNovo()
    {
        var world = new World();
        Spawn(world, "Zona", new Vector2(0f, 0f), Box(16f, solid: false));
        var (_, player, recorder) = Spawn(world, "Player", new Vector2(10f, 0f), Box(16f));

        world.Update(1f / 60f);
        player.Position = new Vector2(500f, 0f);
        world.Update(1f / 60f);
        player.Position = new Vector2(10f, 0f);
        world.Update(1f / 60f);

        Assert.Equal(2, recorder.TriggerEnters.Count);
        Assert.Single(recorder.TriggerExits);
    }

    // ---- Tilemap ----

    private static Tilemap SolidoNoCentro()
    {
        var map = new Tilemap { TileWidth = 16, TileHeight = 16, Width = 3, Height = 3 };
        map.EnsureSize();
        map.SetTile(1, 1, 1);
        map.SolidTiles.Add(1);
        return map;
    }

    [Fact]
    public void TileSolidoEmpurraOColliderParaFora()
    {
        var world = new World();
        var mapa = world.CreateEntity("Mapa");
        mapa.Add(new Transform(Vector2.Zero));
        mapa.Add(SolidoNoCentro());

        // Tile sólido ocupa x/y de 16 a 32. O player entra por cima.
        var (_, player, _) = Spawn(world, "Player", new Vector2(24f, 20f), Box(16f));

        world.Update(1f / 60f);

        Assert.Equal(24f, player.Position.X, Tolerance);
        Assert.Equal(8f, player.Position.Y, Tolerance); // encostado no topo do tile
    }

    [Fact]
    public void TileVazioNaoEmpurra()
    {
        var world = new World();
        var mapa = world.CreateEntity("Mapa");
        mapa.Add(new Transform(Vector2.Zero));
        mapa.Add(SolidoNoCentro());

        // Collider de 8px inteiramente dentro da célula (0,0), que é vazia (-1).
        var (_, player, _) = Spawn(world, "Player", new Vector2(8f, 8f), Box(8f));

        world.Update(1f / 60f);

        Assert.Equal(new Vector2(8f, 8f), player.Position);
    }

    [Fact]
    public void ColliderKinematicoIgnoraOTilemap()
    {
        var world = new World();
        var mapa = world.CreateEntity("Mapa");
        mapa.Add(new Transform(Vector2.Zero));
        mapa.Add(SolidoNoCentro());

        var (_, parede, _) = Spawn(world, "Parede", new Vector2(24f, 20f), Box(16f, kinematic: true));

        world.Update(1f / 60f);

        Assert.Equal(new Vector2(24f, 20f), parede.Position);
    }

    [Fact]
    public void TilemapSemTilesSolidosNaoBloqueia()
    {
        var world = new World();
        var map = new Tilemap { TileWidth = 16, TileHeight = 16, Width = 3, Height = 3 };
        map.EnsureSize();
        map.SetTile(1, 1, 1); // tile pintado, mas SolidTiles vazio = decorativo

        var mapa = world.CreateEntity("Mapa");
        mapa.Add(new Transform(Vector2.Zero));
        mapa.Add(map);

        var (_, player, _) = Spawn(world, "Player", new Vector2(24f, 20f), Box(16f));

        world.Update(1f / 60f);

        Assert.Equal(new Vector2(24f, 20f), player.Position);
    }

    [Fact]
    public void TilemapRespeitaAEscalaEAPosicaoDoTransform()
    {
        var world = new World();
        var mapa = world.CreateEntity("Mapa");
        mapa.Add(new Transform(new Vector2(100f, 100f)) { Scale = new Vector2(2f, 2f) });
        mapa.Add(SolidoNoCentro());

        // Célula de 32px, origem em (100,100): o tile (1,1) sólido cobre x/y de 132 a 164.
        var (_, player, _) = Spawn(world, "Player", new Vector2(148f, 136f), Box(16f));

        world.Update(1f / 60f);

        Assert.Equal(148f, player.Position.X, Tolerance);
        Assert.Equal(124f, player.Position.Y, Tolerance); // 132 (topo do tile) - 8 (meia altura)
    }

    // ---- Pausa ----

    [Fact]
    public void PausadoNaoProcessaColisao()
    {
        var world = new World { Paused = true };
        Spawn(world, "Parede", new Vector2(0f, 0f), Box(16f, kinematic: true));
        var (_, player, recorder) = Spawn(world, "Player", new Vector2(10f, 0f), Box(16f));

        world.Update(1f / 60f);

        Assert.Equal(10f, player.Position.X, Tolerance);
        Assert.Empty(recorder.CollisionsWith);
        Assert.Equal(0, recorder.UpdateCount);
    }
}
