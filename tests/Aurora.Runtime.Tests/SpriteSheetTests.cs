using Aurora.Runtime.Assets;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Scenes;

namespace Aurora.Runtime.Tests;

/// <summary>
/// A folha de sprites recortada no editor (<c>.sheet.json</c>) e como o Animator a usa.
///
/// <para>O que estes testes protegem: o recorte é a única parte da animação que ninguém confere
/// olhando o JSON. Um frame deslocado por causa de margem ignorada não quebra nada — o jogo roda,
/// e o personagem só fica com meio pixel de outro frame no canto. Por isso a conta de índice →
/// retângulo tem teste, não inspeção.</para>
/// </summary>
public class SpriteSheetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aurora_sheet_" + Guid.NewGuid().ToString("N"));

    public SpriteSheetTests() => Directory.CreateDirectory(Path.Combine(_root, "spritesheets"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    // ------------------------------------------------------------- formato

    [Fact]
    public void FolhaSobreviveAoRoundtripDeJson()
    {
        var original = new SpriteSheetAsset
        {
            Texture = "sprites/player.png",
            FrameWidth = 32,
            FrameHeight = 48,
            Columns = 4,
            Rows = 3,
            MarginX = 2,
            MarginY = 3,
            SpacingX = 1,
            SpacingY = 4,
        };
        original.Clips.Add(new SpriteSheetClip { Name = "Andar", Frames = [4, 5, 6, 5], Duration = 0.08f, Loop = true });
        original.Clips.Add(new SpriteSheetClip { Name = "Morrer", Frames = [8, 9], Duration = 0.2f, Loop = false });

        var lida = SpriteSheetAsset.FromJson(original.ToJson());

        Assert.Equal("sprites/player.png", lida.Texture);
        Assert.Equal(32, lida.FrameWidth);
        Assert.Equal(48, lida.FrameHeight);
        Assert.Equal(4, lida.Columns);
        Assert.Equal(3, lida.Rows);
        Assert.Equal(2, lida.MarginX);
        Assert.Equal(3, lida.MarginY);
        Assert.Equal(1, lida.SpacingX);
        Assert.Equal(4, lida.SpacingY);
        Assert.Equal(2, lida.Clips.Count);
        Assert.Equal([4, 5, 6, 5], lida.Clips[0].Frames);
        Assert.Equal(0.08f, lida.Clips[0].Duration, 0.0001);
        Assert.False(lida.Clips[1].Loop);
    }

    [Fact]
    public void RecorteLivreSobreviveAoRoundtrip()
    {
        var original = new SpriteSheetAsset { Texture = "sprites/chefe.png" };
        original.Frames.Add(new SpriteSheetFrame { X = 0, Y = 0, Width = 40, Height = 60 });
        original.Frames.Add(new SpriteSheetFrame { X = 40, Y = 0, Width = 55, Height = 60 });

        var lida = SpriteSheetAsset.FromJson(original.ToJson());

        Assert.True(lida.IsFreeCut);
        Assert.Equal(2, lida.FrameCount);
        Assert.Equal(new RectF(40, 0, 55, 60), lida.FrameRect(1));
    }

    [Fact]
    public void GradeConsideraMargemEVao()
    {
        var sheet = new SpriteSheetAsset
        {
            FrameWidth = 16, FrameHeight = 16, Columns = 3, Rows = 2,
            MarginX = 5, MarginY = 7, SpacingX = 2, SpacingY = 4,
        };

        // Índice 4 = segunda linha, segunda coluna.
        Assert.Equal(new RectF(5 + 1 * 18, 7 + 1 * 20, 16, 16), sheet.FrameRect(4));
    }

    [Fact]
    public void IndiceForaDoRecorteNaoInventaRetangulo()
    {
        var sheet = new SpriteSheetAsset { FrameWidth = 16, FrameHeight = 16, Columns = 2, Rows = 2 };

        Assert.Null(sheet.FrameRect(-1));
        Assert.Null(new SpriteSheetAsset().FrameRect(0));   // folha sem tamanho de frame
    }

    [Theory]
    // Clique no meio de uma célula: o índice de leitura da grade.
    [InlineData(0, 0, 0)]
    [InlineData(17, 0, 1)]
    [InlineData(0, 17, 3)]
    // Dentro do vão entre frames: nenhum, e não o vizinho arredondado.
    [InlineData(16, 0, -1)]
    [InlineData(0, 16, -1)]
    // Além da última coluna/linha da grade (a imagem pode continuar, a grade não).
    [InlineData(51, 0, -1)]
    [InlineData(0, 51, -1)]
    public void PixelViraIndiceDeFrame(int px, int py, int esperado)
    {
        // 3x3 células de 16px com 1px de vão: colunas em 0, 17, 34.
        int index = SpriteSheetAsset.GridIndexAt(px, py, columns: 3, rows: 3,
            frameWidth: 16, frameHeight: 16, spacingX: 1, spacingY: 1);

        Assert.Equal(esperado, index);
    }

    [Fact]
    public void PixelViraIndiceRespeitandoMargem()
    {
        int index = SpriteSheetAsset.GridIndexAt(10, 10, columns: 2, rows: 2,
            frameWidth: 16, frameHeight: 16, marginX: 4, marginY: 4);

        Assert.Equal(0, index);
        Assert.Equal(-1, SpriteSheetAsset.GridIndexAt(2, 2, 2, 2, 16, 16, marginX: 4, marginY: 4));
    }

    [Fact]
    public void ContagemDeCelulasFechaComOTamanhoDoFrame()
    {
        // 128px de imagem, frame de 32: 4 colunas. Com 2px de vão entre elas: 3 (a 4ª não cabe).
        Assert.Equal(4, SpriteSheetAsset.FitCount(128, 32, margin: 0, spacing: 0));
        Assert.Equal(3, SpriteSheetAsset.FitCount(128, 32, margin: 0, spacing: 2));
        Assert.Equal(3, SpriteSheetAsset.FitCount(128, 32, margin: 8, spacing: 0));

        // E o caminho inverso devolve o tamanho que cabe naquela contagem.
        Assert.Equal(32, SpriteSheetAsset.FitSize(128, 4, margin: 0, spacing: 0));
        Assert.Equal(30, SpriteSheetAsset.FitSize(128, 4, margin: 0, spacing: 2));
    }

    [Fact]
    public void FolhaCorrompidaNaoDerruba()
    {
        var sheet = SpriteSheetAsset.FromJson("isso não é json");

        Assert.Equal("", sheet.Texture);
        Assert.Empty(sheet.Clips);
    }

    // ------------------------------------------------------------ animator

    [Fact]
    public void AnimatorRecortaOFrameComMargemEVao()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        entity.Add(new Transform());
        var sprite = new SpriteRenderer();
        entity.Add(sprite);

        var animator = new Animator
        {
            FrameWidth = 16, FrameHeight = 16, SheetColumns = 4,
            MarginX = 1, MarginY = 1, SpacingX = 2, SpacingY = 2,
            Clips = { new AnimationClip { Name = "Idle", Frames = [5], FrameDuration = 1f } },
        };
        entity.Add(animator);
        world.Update(0f);

        Assert.Equal(new RectF(1 + 18, 1 + 18, 16, 16), sprite.SourceRect);
    }

    [Fact]
    public void RecorteLivreVenceAGrade()
    {
        var animator = new Animator { FrameWidth = 16, FrameHeight = 16, SheetColumns = 4 };
        animator.FrameRects.Add(new RectF(3, 4, 40, 60));

        Assert.Equal(new RectF(3, 4, 40, 60), animator.RectOf(0));
        Assert.Null(animator.RectOf(1));   // fora da lista: não cai de volta na grade
    }

    [Fact]
    public void ApplySheetNaoSobrescreveClipeAutoradoNaCena()
    {
        var animator = new Animator();
        animator.Clips.Add(new AnimationClip { Name = "Andar", Frames = [9, 9, 9], FrameDuration = 0.5f });

        var sheet = new SpriteSheetAsset { FrameWidth = 16, FrameHeight = 16, Columns = 4, Rows = 2 };
        sheet.Clips.Add(new SpriteSheetClip { Name = "Andar", Frames = [0, 1], Duration = 0.1f });
        sheet.Clips.Add(new SpriteSheetClip { Name = "Parado", Frames = [4], Duration = 0.1f });

        animator.ApplySheet(sheet);

        Assert.Equal(2, animator.Clips.Count);
        Assert.Equal([9, 9, 9], animator.Clips.Find(c => c.Name == "Andar")!.Frames);
        Assert.Equal([4], animator.Clips.Find(c => c.Name == "Parado")!.Frames);
    }

    // ------------------------------------------------------------- na cena

    private AssetManager Assets()
    {
        // GL null de propósito: nada aqui carrega textura, e LoadText só toca o IAssetSource.
        return new AssetManager(null!, new FileAssetSource(_root));
    }

    private void EscreverFolha(string name, SpriteSheetAsset sheet)
        => File.WriteAllText(Path.Combine(_root, "spritesheets", name), sheet.ToJson());

    private static Animator CarregarAnimator(string sceneJson, AssetManager assets)
    {
        var world = new World();
        new SceneSerializer().Load(sceneJson, new SceneContext { World = world, Assets = assets });
        Assert.True(world.TryFind("Player", out var player));
        return player.Get<Animator>()!;
    }

    [Fact]
    public void CenaComCampoSheetPuxaRecorteEClipesDoArquivo()
    {
        var sheet = new SpriteSheetAsset { FrameWidth = 24, FrameHeight = 32, Columns = 6, Rows = 4, MarginX = 2 };
        sheet.Clips.Add(new SpriteSheetClip { Name = "Andar", Frames = [0, 1, 2], Duration = 0.09f, Loop = false });
        EscreverFolha("player.sheet.json", sheet);

        var animator = CarregarAnimator("""
            { "Scene": "t", "Objects": [ { "Name": "Player", "Components": [
                { "Type": "Transform" },
                { "Type": "Animator", "Sheet": "spritesheets/player.sheet.json" }
            ] } ] }
            """, Assets());

        Assert.Equal(24, animator.FrameWidth);
        Assert.Equal(32, animator.FrameHeight);
        Assert.Equal(6, animator.SheetColumns);
        Assert.Equal(2, animator.MarginX);
        Assert.Single(animator.Clips);
        Assert.Equal([0, 1, 2], animator.Clips[0].Frames);
        Assert.False(animator.Clips[0].Loop);
    }

    [Fact]
    public void CampoEscritoNaCenaVenceOArquivoDaFolha()
    {
        // Uma entidade que usa a mesma arte com o dobro do frame não pode ser obrigada a
        // duplicar a folha só pra mudar um número.
        EscreverFolha("player.sheet.json", new SpriteSheetAsset { FrameWidth = 24, FrameHeight = 32, Columns = 6 });

        var animator = CarregarAnimator("""
            { "Scene": "t", "Objects": [ { "Name": "Player", "Components": [
                { "Type": "Transform" },
                { "Type": "Animator", "Sheet": "spritesheets/player.sheet.json", "FrameWidth": 48 }
            ] } ] }
            """, Assets());

        Assert.Equal(48, animator.FrameWidth);
        Assert.Equal(32, animator.FrameHeight);
    }

    [Fact]
    public void FolhaQueNaoExisteNaoDerrubaACena()
    {
        var animator = CarregarAnimator("""
            { "Scene": "t", "Objects": [ { "Name": "Player", "Components": [
                { "Type": "Transform" },
                { "Type": "Animator", "Sheet": "spritesheets/sumiu.sheet.json", "FrameWidth": 16, "FrameHeight": 16 }
            ] } ] }
            """, Assets());

        Assert.Equal(16, animator.FrameWidth);
        Assert.Empty(animator.Clips);
    }

    [Fact]
    public void CaminhoDaFolhaSobreviveAoSave()
    {
        var world = new World();
        var entity = world.CreateEntity("Player");
        entity.Add(new Transform());
        entity.Add(new Animator
        {
            Sheet = "spritesheets/player.sheet.json",
            FrameWidth = 16, FrameHeight = 16, SheetColumns = 4, SpacingX = 2,
        });

        var serializer = new SceneSerializer();
        string json = serializer.Save("t", new SceneContext { World = world });

        var destino = new World();
        serializer.Load(json, new SceneContext { World = destino });
        Assert.True(destino.TryFind("Player", out var recarregado));
        var animator = recarregado.Get<Animator>()!;

        Assert.Equal("spritesheets/player.sheet.json", animator.Sheet);
        Assert.Equal(2, animator.SpacingX);
    }
}
