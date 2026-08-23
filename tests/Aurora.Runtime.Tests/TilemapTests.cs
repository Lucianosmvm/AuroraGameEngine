using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.Tests;

/// <summary>Autotile de máscara, animação por linhas de frame e o atlas do LiquidTileset.</summary>
public class TilemapTests
{
    private static Tilemap NewMap(int width, int height)
    {
        var map = new Tilemap { Width = width, Height = height };
        map.EnsureSize();
        return map;
    }

    [Fact]
    public void AutotileMarcaMioloBordaECantoDaLagoa()
    {
        // 5x5 com um bloco 3x3 de água no meio.
        var map = NewMap(5, 5);
        map.Fill(1, 1, 3, 3, 0);
        map.Autotile(outsideIsFilled: false);

        // Centro: quatro vizinhos molhados (N=1|L=2|S=4|O=8).
        Assert.Equal(LiquidTileset.Center, map.GetTile(2, 2));

        // Canto superior esquerdo do bloco: só tem vizinho a leste e ao sul.
        Assert.Equal(2 | 4, map.GetTile(1, 1));

        // Borda de cima, meio: leste, sul e oeste.
        Assert.Equal(2 | 4 | 8, map.GetTile(2, 1));

        // Fora da lagoa continua vazio.
        Assert.Equal(-1, map.GetTile(0, 0));
    }

    [Fact]
    public void AutotileLeAMascaraDoEstadoOriginalNaoDoJaReescrito()
    {
        // Uma tira horizontal de 3: se o Autotile lesse Tiles enquanto escreve, a célula do
        // meio veria a vizinha da esquerda já virada em máscara e a lagoa "vazaria".
        var map = NewMap(5, 1);
        map.Fill(1, 0, 3, 1, 0);
        map.Autotile(outsideIsFilled: false);

        Assert.Equal(2, map.GetTile(1, 0));      // só leste
        Assert.Equal(2 | 8, map.GetTile(2, 0));  // leste e oeste
        Assert.Equal(8, map.GetTile(3, 0));      // só oeste
    }

    [Fact]
    public void ForaDoMapaContaComoPreenchidoQuandoPedido()
    {
        var map = NewMap(2, 2);
        map.Fill(0, 0, 2, 2, 0);

        map.Autotile(outsideIsFilled: true);
        Assert.Equal(LiquidTileset.Center, map.GetTile(0, 0));   // oceano: sem margem na borda

        map.Fill(0, 0, 2, 2, 0);
        map.Autotile(outsideIsFilled: false);
        Assert.Equal(2 | 4, map.GetTile(0, 0));                  // lagoa: borda do mapa vira margem
    }

    [Fact]
    public void AutotilePodeComecarEmOutroIndiceDoTileset()
    {
        var map = NewMap(3, 3);
        map.Fill(0, 0, 3, 3, 0);
        map.Autotile(firstIndex: 64, outsideIsFilled: true);

        Assert.Equal(64 + LiquidTileset.Center, map.GetTile(1, 1));
    }

    [Fact]
    public void SemAnimacaoOIndiceDesenhadoEOMesmoGuardado()
    {
        var map = NewMap(1, 1);
        map.AnimationColumns = 16;
        map.AnimationTime = 99f;

        Assert.Equal(7, map.ResolveTile(7));
        Assert.Equal(-1, map.ResolveTile(-1));
    }

    [Fact]
    public void TileDaPrimeiraLinhaTrocaDeLinhaConformeOTempo()
    {
        var map = NewMap(1, 1);
        map.AnimationColumns = 16;
        map.AnimationFrames = 4;
        map.AnimationFrameDuration = 0.10f;

        map.AnimationTime = 0f;
        Assert.Equal(3, map.ResolveTile(3));

        map.AnimationTime = 0.15f;                 // frame 1
        Assert.Equal(3 + 16, map.ResolveTile(3));

        map.AnimationTime = 0.35f;                 // frame 3
        Assert.Equal(3 + 48, map.ResolveTile(3));
    }

    [Fact]
    public void IndiceQueJaEFrameNaoEAnimadoDeNovo()
    {
        // 20 está na segunda linha (>= 16): é o frame de outro tile, não um tile animável —
        // senão desenhar o atlas inteiro numa camada animada embaralharia as linhas de baixo.
        var map = NewMap(1, 1);
        map.AnimationColumns = 16;
        map.AnimationFrames = 4;
        map.AnimationFrameDuration = 0.10f;
        map.AnimationTime = 0.25f;

        Assert.Equal(20, map.ResolveTile(20));
    }

    [Fact]
    public void WorldAvancaEDaVoltaNoRelogioDaAnimacao()
    {
        var world = new World();
        var entity = world.CreateEntity("Lago");
        entity.Add(new Transform(Vector2.Zero));
        var map = entity.Add(new Tilemap
        {
            Width = 1,
            Height = 1,
            AnimationFrames = 4,
            AnimationFrameDuration = 0.25f,   // ciclo de 1 segundo
        });

        world.Update(0.4f);
        Assert.Equal(0.4f, map.AnimationTime, 3);

        world.Update(0.8f);
        // 1.2s vira 0.2s: o relógio não cresce pra sempre (em float o incremento sumiria).
        Assert.Equal(0.2f, map.AnimationTime, 3);
    }

    [Fact]
    public void TilemapEstaticoNaoMexeNoRelogio()
    {
        var world = new World();
        var entity = world.CreateEntity("Chao");
        entity.Add(new Transform(Vector2.Zero));
        var map = entity.Add(new Tilemap { Width = 1, Height = 1 });

        world.Update(1f);

        Assert.Equal(0f, map.AnimationTime);
    }

    [Fact]
    public void AtlasDeLiquidoTemDezesseisColunasPorLinhaDeFrame()
    {
        var style = new LiquidStyle { TileSize = 8, Frames = 3 };

        Assert.Equal(128, LiquidTileset.AtlasWidth(style));
        Assert.Equal(24, LiquidTileset.AtlasHeight(style));
        Assert.Equal(128 * 24 * 4, LiquidTileset.BuildAtlas(style).Length);
    }

    [Fact]
    public void AtlasDeLiquidoEDeterministico()
    {
        // O tileset é arte versionada: duas execuções (ou duas máquinas) têm que gerar o
        // mesmo PNG, senão cada build sujaria o diff do repositório.
        var a = LiquidTileset.BuildAtlas(new LiquidStyle { TileSize = 8, Frames = 2 });
        var b = LiquidTileset.BuildAtlas(new LiquidStyle { TileSize = 8, Frames = 2 });

        Assert.Equal(a, b);
    }

    [Fact]
    public void MioloEOpacoECantoSoltoERecortado()
    {
        var style = new LiquidStyle { TileSize = 16, Frames = 1 };
        var atlas = LiquidTileset.BuildAtlas(style);
        int width = LiquidTileset.AtlasWidth(style);

        byte AlphaAt(int mask, int x, int y)
            => atlas[((y * width) + mask * style.TileSize + x) * 4 + 3];

        // Máscara 15 (cercado de água): tile cheio, inclusive nos cantos.
        Assert.Equal(255, AlphaAt(LiquidTileset.Center, 0, 0));
        Assert.Equal(255, AlphaAt(LiquidTileset.Center, 8, 8));

        // Máscara 0 (poça solta): os quatro cantos são arredondados, então o pixel 0,0 fica
        // de fora do arco e transparente — é o que deixa a grama aparecer por baixo.
        Assert.Equal(0, AlphaAt(0, 0, 0));
        Assert.Equal(255, AlphaAt(0, 8, 8));
    }

    [Fact]
    public void OndaCosturaEntreTilesVizinhos()
    {
        // As frequências da onda são inteiras sobre o tile de propósito: a coluna da direita
        // e a da esquerda do mesmo tile têm que ser quase iguais, senão a lagoa mostraria a
        // grade dos tiles. Testa no miolo (máscara 15), que não tem margem por cima.
        var style = new LiquidStyle { TileSize = 16, Frames = 1, Seed = 0, EdgeWidth = 0f };
        var atlas = LiquidTileset.BuildAtlas(style);
        int width = LiquidTileset.AtlasWidth(style);
        int ox = LiquidTileset.Center * style.TileSize;

        for (int y = 0; y < style.TileSize; y++)
        {
            int left = ((y * width) + ox + 0) * 4;
            int right = ((y * width) + ox + style.TileSize - 1) * 4;

            // Um pixel de distância entre as bordas opostas: só o granulado fino separa.
            Assert.True(Math.Abs(atlas[left] - atlas[right]) < 40,
                $"linha {y}: costura horizontal com salto de {Math.Abs(atlas[left] - atlas[right])}");
        }
    }
}
