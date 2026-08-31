using System.Text.Json.Nodes;
using Aurora.Editor.ViewModels;
using Aurora.Runtime.Graphics;

namespace Aurora.Editor.Tests;

/// <summary>
/// O editor de sprite sheet. Os testes cobrem o que o mouse não consegue conferir sozinho: a
/// ORDEM da seleção (é ela que vira a sequência da animação, e um HashSet a perderia sem
/// avisar), o que vai parar no arquivo, e o que o botão "Aplicar" escreve no Animator da cena.
///
/// <para>Nada aqui abre imagem: decodificar PNG exigiria a plataforma gráfica do Avalonia ligada
/// no teste, e o que precisa de proteção é a lógica, não o decodificador.</para>
/// </summary>
public sealed class SpriteSheetEditorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aurora_sheet_ed_" + Guid.NewGuid().ToString("N"));
    private readonly MainViewModel _main = new();

    public SpriteSheetEditorTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Assets", "sprites"));
        Directory.CreateDirectory(Path.Combine(_dir, "Assets", "scenes"));
        _main.NewScene(Path.Combine(_dir, "Assets", "scenes", "teste.json"));
        // Cena nova nasce com o assets root na própria pasta dela; aqui a arte fica um nível
        // acima, como num projeto de verdade (Assets/scenes, Assets/sprites, Assets/spritesheets).
        _main.ChangeAssetsRoot(Path.Combine(_dir, "Assets"));
    }

    private string SheetPath(string name) => Path.Combine(_dir, "Assets", "spritesheets", name + ".sheet.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Uma folha 4×2 de frames 16×16, com os campos preenchidos direto — o mesmo estado
    /// em que o editor fica depois de escolher a imagem e digitar o tamanho do frame.</summary>
    private SpriteSheetViewModel Folha()
    {
        var vm = new SpriteSheetViewModel(_main)
        {
            SheetName = "player",
            TexturePath = "sprites/player.png",
            Columns = 4,
            Rows = 2,
            FrameWidth = 16,
            FrameHeight = 16,
        };
        return vm;
    }

    // ------------------------------------------------------------- seleção

    [Fact]
    public void OrdemDosCliquesEhAOrdemDaAnimacao()
    {
        var vm = Folha();

        vm.ToggleFrame(2, additive: true);
        vm.ToggleFrame(0, additive: true);
        vm.ToggleFrame(1, additive: true);

        Assert.Equal([2, 0, 1], vm.Selection);
    }

    [Fact]
    public void CtrlCliqueNoMesmoFrameDesmarca()
    {
        var vm = Folha();

        vm.ToggleFrame(3, additive: true);
        vm.ToggleFrame(3, additive: true);

        Assert.Empty(vm.Selection);
    }

    [Fact]
    public void CliqueSemCtrlRecomecaASelecao()
    {
        var vm = Folha();
        vm.ToggleFrame(1, additive: true);
        vm.ToggleFrame(2, additive: true);

        vm.ToggleFrame(5, additive: false);

        Assert.Equal([5], vm.Selection);
    }

    [Fact]
    public void ArrastoMarcaTodosOsFramesQueAAreaTocou()
    {
        var vm = Folha();

        // Faixa que cruza as duas primeiras colunas da primeira linha.
        vm.SelectArea(2, 2, 20, 10, additive: false);

        Assert.Equal([0, 1], vm.Selection);
    }

    [Fact]
    public void FrameForaDaGradeNaoEntraNaSelecao()
    {
        var vm = Folha();

        vm.ToggleFrame(99, additive: true);

        Assert.Empty(vm.Selection);
    }

    // -------------------------------------------------------------- clipes

    [Fact]
    public void ClipeNasceComOsFramesMarcados()
    {
        var vm = Folha();
        vm.ToggleFrame(4, additive: true);
        vm.ToggleFrame(5, additive: true);

        vm.AddClipCommand.Execute(null);

        Assert.Equal("0.1", vm.Clips[0].DurationText);
        Assert.Equal([4, 5], vm.Clips[0].Frames);
        Assert.Equal("Idle", vm.Clips[0].ClipName);
    }

    [Fact]
    public void FpsEDuracaoSaoADuasCarasDoMesmoNumero()
    {
        var vm = Folha();
        vm.AddClipCommand.Execute(null);
        var clip = vm.Clips[0];

        clip.FpsText = "12";

        Assert.Equal(1f / 12f, clip.Duration, 0.0001);
        Assert.Equal("12", clip.FpsText);
    }

    [Fact]
    public void AcrescentarMarcadosMontaIdaEVolta()
    {
        var vm = Folha();
        vm.ToggleFrame(0, additive: true);
        vm.ToggleFrame(1, additive: true);
        vm.ToggleFrame(2, additive: true);
        vm.AddClipCommand.Execute(null);

        vm.ToggleFrame(1, additive: false);
        vm.AppendSelectionCommand.Execute(null);

        Assert.Equal([0, 1, 2, 1], vm.Clips[0].Frames);
    }

    [Fact]
    public void MarcarLinhaPegaALinhaInteiraDoPrimeiroMarcado()
    {
        var vm = Folha();
        vm.ToggleFrame(5, additive: false);

        vm.SelectRowCommand.Execute(null);

        Assert.Equal([4, 5, 6, 7], vm.Selection);
    }

    // ------------------------------------------------------------- arquivo

    [Fact]
    public void SalvarGravaOArquivoQueORuntimeLe()
    {
        var vm = Folha();
        vm.MarginX = 2;
        vm.SpacingY = 1;
        vm.ToggleFrame(0, additive: true);
        vm.ToggleFrame(1, additive: true);
        vm.AddClipCommand.Execute(null);
        vm.Clips[0].ClipName = "Andar";

        vm.SaveCommand.Execute(null);

        string path = SheetPath("player");
        Assert.True(File.Exists(path), vm.Status);

        var lida = SpriteSheetAsset.FromJson(File.ReadAllText(path));
        Assert.Equal("sprites/player.png", lida.Texture);
        Assert.Equal(16, lida.FrameWidth);
        Assert.Equal(2, lida.MarginX);
        Assert.Equal(1, lida.SpacingY);
        Assert.Equal("Andar", lida.Clips[0].Name);
        Assert.Equal([0, 1], lida.Clips[0].Frames);
    }

    [Fact]
    public void FolhaGravadaVoltaIgualAoSerReaberta()
    {
        var vm = Folha();
        vm.FreeCut = true;
        vm.AddFreeRect(0, 0, 40, 60);
        vm.AddFreeRect(40, 0, 55, 60);
        vm.AddClipCommand.Execute(null);
        vm.Clips[0].FramesText = "0, 1";
        vm.SaveCommand.Execute(null);

        var recarregada = new SpriteSheetViewModel(_main);
        recarregada.LoadSheet("spritesheets/player.sheet.json");

        Assert.True(recarregada.FreeCut);
        Assert.Equal(2, recarregada.FrameCount);
        Assert.Equal(new RectF(40, 0, 55, 60), recarregada.RectOf(1));
        Assert.Equal([0, 1], recarregada.Clips[0].Frames);
    }

    [Fact]
    public void DoisClipesComOMesmoNomeNaoGravam()
    {
        var vm = Folha();
        vm.AddClipCommand.Execute(null);
        vm.AddClipCommand.Execute(null);
        vm.Clips[1].ClipName = vm.Clips[0].ClipName;

        vm.SaveCommand.Execute(null);

        Assert.False(File.Exists(SheetPath("player")));
        Assert.Contains("Dois clipes", vm.Status);
    }

    [Fact]
    public void SalvarSemImagemAvisaEmVezDeGravarFolhaVazia()
    {
        var vm = new SpriteSheetViewModel(_main) { SheetName = "vazia" };

        vm.SaveCommand.Execute(null);

        Assert.False(File.Exists(SheetPath("vazia")));
        Assert.Contains("imagem", vm.Status);
    }

    // ------------------------------------------------------------ na cena

    private EntityViewModel NovaEntidade(string nome)
    {
        var node = new JsonObject
        {
            ["Name"] = nome,
            ["Components"] = new JsonArray(new JsonObject { ["Type"] = "Transform" }),
        };
        var entity = new EntityViewModel(node, _main);
        _main.Entities.Add(entity);
        _main.SelectedEntity = entity;
        return entity;
    }

    [Fact]
    public void AplicarApontaOAnimatorDaEntidadeProArquivo()
    {
        var entity = NovaEntidade("Player");
        var vm = Folha();
        vm.MarginX = 2;
        vm.AddClipCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        vm.ApplyToEntityCommand.Execute(null);

        var components = (JsonArray)entity.Node["Components"]!;
        var animator = components.OfType<JsonObject>().Single(c => c["Type"]!.GetValue<string>() == "Animator");

        Assert.Equal("spritesheets/player.sheet.json", animator["Sheet"]!.GetValue<string>());
        Assert.Equal(16, animator["FrameWidth"]!.GetValue<int>());
        Assert.Equal(2, animator["MarginX"]!.GetValue<int>());

        // Os clipes ficam na folha: cópia velha na cena venceria a folha e faria "corrigir a
        // folha" deixar de corrigir as entidades que a usam.
        Assert.False(animator.ContainsKey("Clips"));
    }

    [Fact]
    public void AplicarPreencheATexturaDoSpriteQuandoEstaVazia()
    {
        var entity = NovaEntidade("Player");
        var vm = Folha();
        vm.SaveCommand.Execute(null);

        vm.ApplyToEntityCommand.Execute(null);

        var components = (JsonArray)entity.Node["Components"]!;
        var sprite = components.OfType<JsonObject>().Single(c => c["Type"]!.GetValue<string>() == "SpriteRenderer");
        Assert.Equal("sprites/player.png", sprite["Texture"]!.GetValue<string>());
    }

    [Fact]
    public void AplicarNaoTrocaATexturaQueAEntidadeJaTinha()
    {
        var entity = NovaEntidade("Player");
        ((JsonArray)entity.Node["Components"]!).Add(new JsonObject
        {
            ["Type"] = "SpriteRenderer",
            ["Texture"] = "sprites/outro.png",
        });

        var vm = Folha();
        vm.SaveCommand.Execute(null);
        vm.ApplyToEntityCommand.Execute(null);

        var components = (JsonArray)entity.Node["Components"]!;
        var sprite = components.OfType<JsonObject>().Single(c => c["Type"]!.GetValue<string>() == "SpriteRenderer");
        Assert.Equal("sprites/outro.png", sprite["Texture"]!.GetValue<string>());
    }

    [Fact]
    public void AplicarSemEntidadeSelecionadaAvisaEmVezDeSumir()
    {
        _main.SelectedEntity = null;
        var vm = Folha();
        vm.SaveCommand.Execute(null);

        vm.ApplyToEntityCommand.Execute(null);

        Assert.Contains("Selecione uma entidade", vm.Status);
    }
}
