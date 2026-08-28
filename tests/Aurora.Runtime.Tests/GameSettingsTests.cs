using Aurora.Runtime.Saves;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Preferências do jogador. A razão de existirem separadas do <see cref="GameState"/>: aquele
/// vive dentro do slot de save, então volume guardado lá seria esquecido a cada jogo novo.
/// </summary>
public sealed class GameSettingsTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;

    public GameSettingsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "aurora-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void GuardaELeValor()
    {
        var settings = new GameSettings(_file);

        Assert.Equal(1f, settings.Get(GameSettings.MasterVolume, 1f));

        settings.Set(GameSettings.MasterVolume, 0.3f);

        Assert.Equal(0.3f, settings.Get(GameSettings.MasterVolume, 1f));
    }

    /// <summary>O ciclo que importa: ajustar, fechar, abrir de novo.</summary>
    [Fact]
    public void SobreviveAoDisco()
    {
        var settings = new GameSettings(_file);
        settings.Set(GameSettings.MusicVolume, 0.5f);
        settings.SetText("Idioma", "pt-BR");
        settings.Save();

        var recarregado = new GameSettings(_file);
        recarregado.Load();

        Assert.Equal(0.5f, recarregado.Get(GameSettings.MusicVolume, 1f));
        Assert.Equal("pt-BR", recarregado.GetText("Idioma"));
    }

    /// <summary>Primeira execução: não existe arquivo, e isso é normal — tem que ficar no
    /// padrão, não estourar.</summary>
    [Fact]
    public void SemArquivo_FicaNoPadrao()
    {
        var settings = new GameSettings(_file);
        settings.Load();

        Assert.Equal(1f, settings.Get(GameSettings.MasterVolume, 1f));
        Assert.False(settings.Has(GameSettings.MasterVolume));
    }

    /// <summary>Arquivo corrompido não pode impedir o jogo de abrir: trocar som alto demais por
    /// uma tela que não abre é péssimo negócio.</summary>
    [Fact]
    public void ArquivoCorrompido_NaoDerruba()
    {
        File.WriteAllText(_file, "{ isto não é json válido");

        var settings = new GameSettings(_file);
        settings.Load();

        Assert.Equal(1f, settings.Get(GameSettings.MasterVolume, 1f));
    }

    [Fact]
    public void MudancaAvisaOsOuvintes()
    {
        var settings = new GameSettings(_file);
        int avisos = 0;
        settings.Changed += () => avisos++;

        settings.Set(GameSettings.MasterVolume, 0.5f);

        Assert.Equal(1, avisos);
    }

    /// <summary>Escrever o mesmo valor não avisa: com um slider chamando Set a cada frame de
    /// arrasto, avisar sem mudança viraria gravação em disco à toa.</summary>
    [Fact]
    public void ValorIgual_NaoAvisa()
    {
        var settings = new GameSettings(_file);
        settings.Set(GameSettings.MasterVolume, 0.5f);

        int avisos = 0;
        settings.Changed += () => avisos++;
        settings.Set(GameSettings.MasterVolume, 0.5f);

        Assert.Equal(0, avisos);
    }

    [Fact]
    public void ClearVoltaAoPadrao()
    {
        var settings = new GameSettings(_file);
        settings.Set(GameSettings.MasterVolume, 0.2f);

        settings.Clear();

        Assert.False(settings.Has(GameSettings.MasterVolume));
        Assert.Equal(1f, settings.Get(GameSettings.MasterVolume, 1f));
    }

    /// <summary>Gravar em pasta inexistente cria o caminho — na primeira execução ela não
    /// existe.</summary>
    [Fact]
    public void CriaAPastaAoGravar()
    {
        string nested = Path.Combine(_dir, "sub", "settings.json");
        var settings = new GameSettings(nested);
        settings.Set(GameSettings.SfxVolume, 0.7f);
        settings.Save();

        Assert.True(File.Exists(nested));
    }

    /// <summary>Não deixa .tmp órfão da escrita atômica.</summary>
    [Fact]
    public void NaoDeixaTemporarioParaTras()
    {
        var settings = new GameSettings(_file);
        settings.Set(GameSettings.MasterVolume, 0.4f);
        settings.Save();

        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }
}
