using Aurora.Editor.ViewModels;

namespace Aurora.Editor.Tests;

/// <summary>
/// Tela de referência: o tamanho que vale como "a tela inteira do jogo" em qualquer aparelho. O
/// que se prende aqui é o que o autor lê antes de montar um menu — a proporção, o efeito de girar,
/// quanto sobra de barra preta no aparelho comparado, e o aviso de orientação brigada com o APK.
/// Errar isso não quebra nada: só faz o menu ficar montado pro aparelho errado.
/// </summary>
public class ScreenPresetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aurora-tela-" + Guid.NewGuid().ToString("N"));
    private readonly MainViewModel _vm = new();

    public ScreenPresetTests()
    {
        Directory.CreateDirectory(_dir);
        string scene = Path.Combine(_dir, "menu.json");
        File.WriteAllText(scene, """
            { "Scene": "menu", "Objects": [] }
            """);
        _vm.OpenScene(scene);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void PresetGravaAResolucaoEVoltaSelecionado()
    {
        _vm.SelectedScreenPreset = new ScreenPreset("Celular retrato — 720x1280", 720, 1280);

        Assert.Equal(720, _vm.DesignWidth);
        Assert.Equal(1280, _vm.DesignHeight);

        // O getter reencontra o preset pela resolução — reabrir o projeto mostra o mesmo item.
        Assert.Equal(720, _vm.SelectedScreenPreset!.Width);
        Assert.Equal(1280, _vm.SelectedScreenPreset.Height);
    }

    [Fact]
    public void ResolucaoDigitadaAMaoNaoBateComNenhumPreset()
    {
        _vm.DesignWidth = 1333;
        _vm.DesignHeight = 777;

        Assert.Null(_vm.SelectedScreenPreset);
    }

    [Fact]
    public void GirarTrocaLarguraPorAltura()
    {
        _vm.SelectedScreenPreset = new ScreenPreset("PC", 1280, 720);

        _vm.SwapScreenOrientation();

        Assert.Equal(720, _vm.DesignWidth);
        Assert.Equal(1280, _vm.DesignHeight);
    }

    [Theory]
    [InlineData(1280, 720, "16:9")]
    [InlineData(1920, 1080, "16:9")]
    [InlineData(2400, 1080, "20:9")]
    [InlineData(1024, 768, "4:3")]
    public void ProporcaoEhReduzidaPeloMdc(int width, int height, string esperado)
    {
        _vm.DesignWidth = width;
        _vm.DesignHeight = height;

        Assert.Equal(esperado, _vm.ScreenAspectLabel);
    }

    [Fact]
    public void SemComparacaoNaoTemDicaNenhuma()
    {
        Assert.Equal("", _vm.CompareHint);
    }

    [Fact]
    public void MesmaProporcaoOcupaATelaInteira()
    {
        _vm.DesignWidth = 1280;
        _vm.DesignHeight = 720;
        _vm.CompareDevice = new ScreenPreset("Celular 16:9", 1920, 1080);

        Assert.Contains("tela inteira", _vm.CompareHint);
    }

    [Fact]
    public void CelularMaisAltoDeixaBarraEmCimaEEmbaixo()
    {
        // Jogo 16:9 (1.78) num celular 9:20 (0.45): sobra em cima e embaixo, e sobra MUITO.
        _vm.DesignWidth = 1280;
        _vm.DesignHeight = 720;
        _vm.CompareDevice = new ScreenPreset("Celular 20:9 retrato", 1080, 2400);

        Assert.Contains("em cima e embaixo", _vm.CompareHint);
        Assert.Contains("75%", _vm.CompareHint);
    }

    [Fact]
    public void MonitorMaisLargoDeixaBarraNasLaterais()
    {
        _vm.DesignWidth = 1024;
        _vm.DesignHeight = 768;
        _vm.CompareDevice = new ScreenPreset("Ultrawide", 2560, 1080);

        Assert.Contains("laterais", _vm.CompareHint);
    }

    [Fact]
    public void AvisaQuandoAOrientacaoDoApkBrigaComATela()
    {
        _vm.DesignWidth = 1280;
        _vm.DesignHeight = 720;
        _vm.AndroidOrientation = "Portrait";

        Assert.True(_vm.HasAndroidOrientationWarning);
        Assert.Contains("faixa no meio", _vm.AndroidOrientationWarning);

        // Girando a tela de referência, o aviso some — sem precisar tocar na orientação.
        _vm.SwapScreenOrientation();
        Assert.False(_vm.HasAndroidOrientationWarning);
    }

    [Fact]
    public void SensorNaoBrigaComNenhumaOrientacao()
    {
        // "Sensor" gira com o aparelho: não dá pra dizer que está errado.
        _vm.DesignWidth = 1280;
        _vm.DesignHeight = 720;
        _vm.AndroidOrientation = "Sensor";

        Assert.False(_vm.HasAndroidOrientationWarning);
    }
}
