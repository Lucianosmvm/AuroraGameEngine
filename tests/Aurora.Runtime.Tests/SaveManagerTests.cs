using System.Numerics;
using Aurora.Runtime;
using Aurora.Runtime.Assets;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Events;
using Aurora.Runtime.Saves;
using Aurora.Runtime.Scenes;
using Aurora.Runtime.UI;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Save/load: o jogador fecha o jogo e volta de onde parou. Estes testes existem porque a
/// classe não tinha nenhum — e é o tipo de código em que a falha só aparece na hora em que
/// mais dói, com o progresso de alguém dentro.
///
/// Cada teste amarra uma parte do "de onde parou": cena, posição, variáveis, itens, missões —
/// e as saídas sujas (arquivo corrompido, slot vazio, escrita interrompida).
/// </summary>
public class SaveManagerTests : IDisposable
{
    private const float Tolerance = 0.001f;

    private readonly string _assetsRoot = Path.Combine(Path.GetTempPath(), "aurora-save-" + Guid.NewGuid().ToString("N"));

    /// <summary>Nome de jogo único por instância de teste: o SaveManager grava em
    /// %LocalAppData%/[GameName]/saves, então dois testes com o mesmo nome disputariam o mesmo
    /// arquivo — e a suíte roda em paralelo.</summary>
    private readonly string _gameName = "AuroraTeste" + Guid.NewGuid().ToString("N");

    private readonly World _world = new();
    private readonly GameState _state = new();
    private readonly InventoryManager _inventory = new();
    private readonly QuestManager _quests = new();
    private readonly SceneManager _scenes;
    private readonly SaveManager _save;

    public SaveManagerTests()
    {
        Directory.CreateDirectory(Path.Combine(_assetsRoot, "scenes"));
        WriteScene("fase1.json", 1, 2);
        WriteScene("fase2.json", 50, 60);

        // GL null: nada aqui carrega textura, e LoadText só toca o IAssetSource (mesmo padrão
        // de SceneManagerTests).
        var assets = new AssetManager(null!, new FileAssetSource(_assetsRoot));
        _scenes = new SceneManager(_world, new SceneSerializer(), new EventSystem(_world, _state),
            new DialogueSystem(), assets);
        _save = new SaveManager(_state, _scenes, _world, _gameName, _inventory, _quests);
    }

    private void WriteScene(string name, float playerX, float playerY)
        => File.WriteAllText(Path.Combine(_assetsRoot, "scenes", name), $$"""
            {
              "Scene": "{{Path.GetFileNameWithoutExtension(name)}}",
              "Objects": [
                { "Name": "Player", "Components": [{ "Type": "Transform", "X": {{playerX}}, "Y": {{playerY}} }] }
              ]
            }
            """);

    private void WriteSceneWithHealth(string name, float max, float current)
        => File.WriteAllText(Path.Combine(_assetsRoot, "scenes", name), $$"""
            {
              "Scene": "{{Path.GetFileNameWithoutExtension(name)}}",
              "Objects": [{
                "Name": "Player",
                "Components": [
                  { "Type": "Transform", "X": 0, "Y": 0 },
                  { "Type": "Health", "Max": {{max}}, "Current": {{current}} }
                ]
              }]
            }
            """);

    private Health PlayerHealth()
    {
        Assert.True(_world.TryFind("Player", out var player), "A cena carregada não tem Player.");
        return player.Get<Health>()!;
    }

    private Transform PlayerTransform()
    {
        Assert.True(_world.TryFind("Player", out var player), "A cena carregada não tem Player.");
        return player.Get<Transform>()!;
    }

    // ---- O básico do "continuar de onde parou" ----

    [Fact]
    public void SaveThenLoad_RestoresTheScene()
    {
        _scenes.Load("scenes/fase2.json");
        _save.Save();

        _scenes.Load("scenes/fase1.json");
        Assert.True(_save.Load());

        Assert.Equal("scenes/fase2.json", _scenes.CurrentScene);
    }

    [Fact]
    public void SaveThenLoad_RestoresThePlayerPosition_NotTheSceneDefault()
    {
        // O ponto do save: o jogador estava andando, não no ponto onde a cena o nasce.
        _scenes.Load("scenes/fase1.json");
        PlayerTransform().Position = new Vector2(777, 888);
        _save.Save();

        _scenes.Load("scenes/fase1.json");
        Assert.Equal(1, PlayerTransform().Position.X, Tolerance);

        _save.Load();

        Assert.Equal(777, PlayerTransform().Position.X, Tolerance);
        Assert.Equal(888, PlayerTransform().Position.Y, Tolerance);
    }

    [Fact]
    public void SaveThenLoad_RestoresVariablesAndSwitches()
    {
        _state.SetVariable("ouro", 350);
        _state.SetSwitch("porta_aberta", true);
        _save.Save();

        _state.Clear();
        _save.Load();

        Assert.Equal(350, _state.GetVariable("ouro"), Tolerance);
        Assert.True(_state.GetSwitch("porta_aberta"));
    }

    [Fact]
    public void SaveThenLoad_RestoresInventoryAndQuests()
    {
        _inventory.Add("pocao", 3);
        _quests.SetStage("resgate", 2);
        _save.Save();

        _inventory.Clear();
        _quests.Clear();
        _save.Load();

        Assert.Equal(3, _inventory.GetCount("pocao"));
        Assert.Equal(2, _quests.GetStage("resgate"));
    }

    // ---- O que o jogador carrega consigo ----

    [Fact]
    public void SaveThenLoad_RestoresHealth_NotTheSceneDefault()
    {
        // Salvar com 3 de vida e voltar curado apaga a tensão inteira de um jogo.
        WriteSceneWithHealth("arena.json", max: 100, current: 100);
        _scenes.Load("scenes/arena.json");
        PlayerHealth().Current = 12f;
        _save.Save();

        _scenes.Load("scenes/arena.json");
        Assert.Equal(100, PlayerHealth().Current, Tolerance);

        _save.Load();

        Assert.Equal(12, PlayerHealth().Current, Tolerance);
    }

    [Fact]
    public void SaveThenLoad_RestoresMaxHealth_SoLevelUpsAreNotLost()
    {
        WriteSceneWithHealth("arena.json", max: 100, current: 100);
        _scenes.Load("scenes/arena.json");

        var health = PlayerHealth();
        health.Max = 250f;      // subiu de nível
        health.Current = 200f;
        _save.Save();

        _scenes.Load("scenes/arena.json");
        _save.Load();

        Assert.Equal(250, PlayerHealth().Max, Tolerance);
        Assert.Equal(200, PlayerHealth().Current, Tolerance);
    }

    [Fact]
    public void HealthAboveTheMax_IsClamped_SoATamperedSaveCannotBreakTheGame()
    {
        // Max 77, fora dos valores padrão do Health: se a leitura da cena ou do save falhasse,
        // o resultado cairia em 100 (o default) e o teste denunciaria em vez de passar à toa.
        WriteSceneWithHealth("arena.json", max: 77, current: 50);
        _scenes.Load("scenes/arena.json");
        _save.Save();

        string path = Path.Combine(_save.SaveDirectory, "slot_0.json");
        string original = File.ReadAllText(path);
        string adulterado = System.Text.RegularExpressions.Regex.Replace(
            original, "\"PlayerHealth\": *[0-9.]+", "\"PlayerHealth\": 99999");

        // Sem isto, um replace que não casasse deixaria o arquivo intacto e o teste passaria
        // sozinho, sem nunca ter testado o clamp.
        Assert.NotEqual(original, adulterado);
        File.WriteAllText(path, adulterado);

        _save.Load();

        Assert.Equal(77, PlayerHealth().Current, Tolerance);
    }

    [Fact]
    public void PlayerWithoutHealthComponent_SavesAndLoadsFine()
    {
        // Jogo sem vida (puzzle, plataforma sem dano) não pode quebrar por causa deste campo.
        _scenes.Load("scenes/fase1.json");
        _save.Save();

        Assert.True(_save.Load());
    }

    [Fact]
    public void OldSaveWithoutHealth_KeepsTheSceneValue_InsteadOfZeroingIt()
    {
        // Campo novo é opcional: save de antes dele não pode ressuscitar o jogador com 0 de vida.
        WriteSceneWithHealth("arena.json", max: 100, current: 100);
        _scenes.Load("scenes/arena.json");
        _save.Save();

        string path = Path.Combine(_save.SaveDirectory, "slot_0.json");
        File.WriteAllText(path, """
            {
              "Slot": 0,
              "Scene": "scenes/arena.json",
              "SavedAt": "2024-01-01T00:00:00Z",
              "Variables": {},
              "Switches": {}
            }
            """);

        Assert.True(_save.Load());
        Assert.Equal(100, PlayerHealth().Current, Tolerance);
    }

    [Fact]
    public void GoldAsAVariable_Persists()
    {
        // "Ouro" na engine é variável do GameState OU item do inventário (ver GUIA-JOGO-BASE).
        // Os dois caminhos precisam sobreviver — este é o primeiro.
        _state.SetVariable("Gold", 1250);
        _save.Save();

        _state.Clear();
        _save.Load();

        Assert.Equal(1250, _state.GetVariable("Gold"), Tolerance);
    }

    [Fact]
    public void GoldAsAnInventoryItem_Persists()
    {
        _inventory.Add("Gold", 87);
        _save.Save();

        _inventory.Clear();
        _save.Load();

        Assert.Equal(87, _inventory.GetCount("Gold"));
    }

    [Fact]
    public void EverythingThePlayerCarries_SurvivesTogether()
    {
        // O teste de aceitação do pedido: fechar o jogo e voltar com tudo — cena, lugar, vida,
        // ouro, itens e missão.
        WriteSceneWithHealth("arena.json", max: 120, current: 120);
        _scenes.Load("scenes/arena.json");

        PlayerTransform().Position = new Vector2(640, 360);
        PlayerHealth().Current = 45f;
        _state.SetVariable("Gold", 300);
        _inventory.Add("pocao", 4);
        _inventory.Add("chave_mestra", 1);
        _quests.SetStage("resgate", 3);
        _state.SetSwitch("ponte_baixada", true);

        _save.Save(slot: 1);

        // Simula reabrir o jogo do zero.
        _state.Clear();
        _inventory.Clear();
        _quests.Clear();
        _scenes.Load("scenes/fase1.json");

        Assert.True(_save.Load(slot: 1));

        Assert.Equal("scenes/arena.json", _scenes.CurrentScene);
        Assert.Equal(640, PlayerTransform().Position.X, Tolerance);
        Assert.Equal(360, PlayerTransform().Position.Y, Tolerance);
        Assert.Equal(45, PlayerHealth().Current, Tolerance);
        Assert.Equal(120, PlayerHealth().Max, Tolerance);
        Assert.Equal(300, _state.GetVariable("Gold"), Tolerance);
        Assert.Equal(4, _inventory.GetCount("pocao"));
        Assert.Equal(1, _inventory.GetCount("chave_mestra"));
        Assert.Equal(3, _quests.GetStage("resgate"));
        Assert.True(_state.GetSwitch("ponte_baixada"));
    }

    // ---- Slots ----

    [Fact]
    public void DifferentSlots_DoNotOverwriteEachOther()
    {
        _state.SetVariable("ouro", 10);
        _save.Save(slot: 0);

        _state.SetVariable("ouro", 999);
        _save.Save(slot: 1);

        _save.Load(slot: 0);
        Assert.Equal(10, _state.GetVariable("ouro"), Tolerance);

        _save.Load(slot: 1);
        Assert.Equal(999, _state.GetVariable("ouro"), Tolerance);
    }

    [Fact]
    public void AutoSave_IsSeparateFromTheManualSlots()
    {
        _state.SetVariable("ouro", 10);
        _save.Save(slot: 0);

        _state.SetVariable("ouro", 20);
        _save.AutoSave();

        Assert.True(_save.HasAutoSave());

        _save.Load(slot: 0);
        Assert.Equal(10, _state.GetVariable("ouro"), Tolerance);

        _save.LoadAutoSave();
        Assert.Equal(20, _state.GetVariable("ouro"), Tolerance);
    }

    [Fact]
    public void LoadingAnEmptySlot_ReturnsFalse_WithoutTouchingTheCurrentGame()
    {
        _state.SetVariable("ouro", 42);

        Assert.False(_save.HasSave(slot: 7));
        Assert.False(_save.Load(slot: 7));
        Assert.Equal(42, _state.GetVariable("ouro"), Tolerance);
    }

    [Fact]
    public void Delete_RemovesTheSlot()
    {
        _save.Save(slot: 3);
        Assert.True(_save.HasSave(slot: 3));

        _save.Delete(slot: 3);

        Assert.False(_save.HasSave(slot: 3));
    }

    [Fact]
    public void GetInfo_ReadsMetadataWithoutLoadingTheGame()
    {
        // É o que uma tela de "Continuar" precisa: mostrar os slots sem entrar em nenhum.
        _scenes.Load("scenes/fase2.json");
        _state.SetVariable("ouro", 55);
        _save.Save(slot: 2);

        _state.Clear();
        _scenes.Load("scenes/fase1.json");

        var info = _save.GetInfo(slot: 2);

        Assert.NotNull(info);
        Assert.Equal("scenes/fase2.json", info!.Scene);
        Assert.Equal(55, info.Variables["ouro"], Tolerance);
        Assert.Equal("scenes/fase1.json", _scenes.CurrentScene);   // não entrou no save
        Assert.Equal(0, _state.GetVariable("ouro"), Tolerance);
    }

    [Fact]
    public void GetInfo_OnAnEmptySlot_IsNull()
    {
        Assert.Null(_save.GetInfo(slot: 9));
    }

    // ---- Saídas sujas ----

    [Fact]
    public void CorruptSaveFile_ReturnsFalse_InsteadOfThrowing()
    {
        _save.Save(slot: 0);
        File.WriteAllText(Path.Combine(_save.SaveDirectory, "slot_0.json"), "{ isto não é json");

        Assert.False(_save.Load(slot: 0));
        Assert.Null(_save.GetInfo(slot: 0));
    }

    [Fact]
    public void OverwritingASave_LeavesNoTempFileBehind()
    {
        // A escrita é atômica (grava .tmp e move por cima). Se o .tmp sobrasse, a pasta de saves
        // acumularia lixo a cada gravação.
        _save.Save(slot: 0);
        _save.Save(slot: 0);

        Assert.Empty(Directory.GetFiles(_save.SaveDirectory, "*.tmp"));
    }

    [Fact]
    public void OverwritingASave_KeepsTheNewestContent()
    {
        _state.SetVariable("ouro", 1);
        _save.Save(slot: 0);
        _state.SetVariable("ouro", 2);
        _save.Save(slot: 0);

        _state.Clear();
        _save.Load(slot: 0);

        Assert.Equal(2, _state.GetVariable("ouro"), Tolerance);
    }

    [Fact]
    public void SaveFromAnOlderVersion_WithoutItemsOrQuests_StillLoads()
    {
        // Campos novos entraram como opcionais. Um save gravado antes deles não pode virar
        // progresso perdido.
        _save.Save(slot: 0);
        string path = Path.Combine(_save.SaveDirectory, "slot_0.json");
        File.WriteAllText(path, """
            {
              "Slot": 0,
              "Scene": "scenes/fase1.json",
              "SavedAt": "2024-01-01T00:00:00Z",
              "Variables": { "ouro": 5 },
              "Switches": { "porta": true }
            }
            """);

        Assert.True(_save.Load(slot: 0));
        Assert.Equal(5, _state.GetVariable("ouro"), Tolerance);
        Assert.True(_state.GetSwitch("porta"));
    }

    [Fact]
    public void PlayerEntityName_IsConfigurable()
    {
        // Jogo que não chama o jogador de "Player" ainda precisa ter a posição salva.
        File.WriteAllText(Path.Combine(_assetsRoot, "scenes", "heroi.json"), """
            {
              "Scene": "heroi",
              "Objects": [{ "Name": "Heroi", "Components": [{ "Type": "Transform", "X": 0, "Y": 0 }] }]
            }
            """);

        _save.PlayerEntityName = "Heroi";
        _scenes.Load("scenes/heroi.json");
        _world.TryFind("Heroi", out var heroi);
        heroi.Get<Transform>()!.Position = new Vector2(123, 456);
        _save.Save();

        _scenes.Load("scenes/heroi.json");
        _save.Load();

        _world.TryFind("Heroi", out var recarregado);
        Assert.Equal(123, recarregado.Get<Transform>()!.Position.X, Tolerance);
    }

    [Fact]
    public void LoadingASave_CarriesThePlayersChildrenAlong()
    {
        // Restaurar só a posição do jogador deixa os filhos dele onde a CENA os nasce: a arma
        // fica plantada a centenas de pixels do dono, e o vínculo pai/filho nunca conserta
        // (o encaixe é preservado a partir do frame seguinte, não recalculado).
        File.WriteAllText(Path.Combine(_assetsRoot, "scenes", "comarma.json"), """
            {
              "Scene": "comarma",
              "Objects": [
                { "Name": "Player", "Components": [{ "Type": "Transform", "X": 0, "Y": 0 }] },
                { "Name": "Arma", "Components": [{ "Type": "Transform", "X": 10, "Y": 0, "Parent": "Player" }] }
              ]
            }
            """);

        _scenes.Load("scenes/comarma.json");
        PlayerTransform().Position = new Vector2(500, 0);
        _world.TryFind("Arma", out var arma);
        arma.Get<Transform>()!.Position = new Vector2(510, 0);
        _save.Save();

        _scenes.Load("scenes/comarma.json");
        _save.Load();

        _world.TryFind("Arma", out var recarregada);
        Assert.Equal(510, recarregada.Get<Transform>()!.Position.X, Tolerance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_assetsRoot, recursive: true); } catch { /* melhor esforço */ }

        // A pasta de saves fica fora do temp (LocalAppData) — sem isto cada execução da suíte
        // deixaria um diretório órfão na máquina de quem rodou.
        try
        {
            string gameDir = Directory.GetParent(_save.SaveDirectory)!.FullName;
            Directory.Delete(gameDir, recursive: true);
        }
        catch { /* melhor esforço */ }
    }
}
