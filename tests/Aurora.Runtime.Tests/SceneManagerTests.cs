using Aurora.Runtime.Assets;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Events;
using Aurora.Runtime.Scenes;
using Aurora.Runtime.UI;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Cobre o contrato do evento <see cref="SceneManager.SceneLoaded"/> — o gancho que jogos usam
/// pra criar entidades por código depois de cada carga (carregar cena chama World.Clear(),
/// então o que nasce no OnLoad não sobrevive à primeira troca).
/// </summary>
public class SceneManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aurora-scenes-" + Guid.NewGuid().ToString("N"));
    private readonly World _world = new();
    private readonly SceneManager _manager;

    public SceneManagerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "scenes"));
        Escrever("scenes/main.json", """
            { "Scene": "main", "Objects": [ { "Name": "Player", "Components": [ { "Type": "Transform", "X": 1, "Y": 2 } ] } ] }
            """);

        var state = new GameState();
        // GL null de propósito: nada aqui carrega textura, e LoadText só toca o IAssetSource.
        var assets = new AssetManager(null!, new FileAssetSource(_root));
        _manager = new SceneManager(_world, new SceneSerializer(), new EventSystem(_world, state),
            new DialogueSystem(), assets);
    }

    private void Escrever(string path, string content)
        => File.WriteAllText(Path.Combine(_root, path), content);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void SceneLoadedDisparaComOMundoJaMontado()
    {
        // O assinante precisa enxergar a cena de pé: é aí que ele cria os inimigos dele.
        int chamadas = 0;
        string? recebido = null;
        bool viuPlayer = false;
        _manager.SceneLoaded += path =>
        {
            chamadas++;
            recebido = path;
            viuPlayer = _world.TryFind("Player", out _);
        };

        _manager.Load("scenes/main.json");

        Assert.Equal(1, chamadas);
        Assert.Equal("scenes/main.json", recebido);
        Assert.True(viuPlayer, "SceneLoaded disparou antes de a cena estar no World.");
    }

    [Fact]
    public void SceneLoadedDisparaDeNovoACadaCarga()
    {
        // World.Clear() a cada load é justamente o motivo do evento existir: recarregar a mesma
        // cena tem que reavisar, senão o jogo não repõe o que foi apagado.
        int chamadas = 0;
        _manager.SceneLoaded += _ => chamadas++;

        _manager.Load("scenes/main.json");
        _manager.Load("scenes/main.json");

        Assert.Equal(2, chamadas);
    }

    [Fact]
    public void SceneLoadedNaoDisparaQuandoOLoadFalha()
    {
        // Cena inexistente não derruba o jogo (fica só o log), mas também não pode avisar
        // "carregou" — o assinante spawnaria num mundo vazio.
        int chamadas = 0;
        _manager.SceneLoaded += _ => chamadas++;

        _manager.Load("scenes/naoexiste.json");

        Assert.Equal(0, chamadas);
        Assert.Null(_manager.CurrentScene);
    }

    [Fact]
    public void ExcecaoDoAssinanteNaoViraFalhaDeLoad()
    {
        // Bug no handler do jogo tem que estourar de verdade, com o stack dele. Se caísse no
        // catch do load, viraria "falha ao carregar cena" e esconderia a causa real.
        _manager.SceneLoaded += _ => throw new InvalidOperationException("bug do jogo");

        var ex = Assert.Throws<InvalidOperationException>(() => _manager.Load("scenes/main.json"));

        Assert.Equal("bug do jogo", ex.Message);
        Assert.Equal("scenes/main.json", _manager.CurrentScene);
    }
}
