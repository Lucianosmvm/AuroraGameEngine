using System.Numerics;
using System.Text.Json;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace Aurora.Runtime.Saves;

/// <summary>Metadados de um slot de save — útil para exibir na tela de seleção de save.</summary>
public sealed record SaveInfo(
    int Slot,
    string Scene,
    DateTime SavedAt,
    IReadOnlyDictionary<string, float> Variables);

/// <summary>
/// Persiste e restaura o estado do jogo em disco.
/// Salva: <see cref="GameState"/> (variáveis + switches) e a cena atual.
/// Os saves ficam em <c>%LocalAppData%/[GameName]/saves/</c>.
/// </summary>
public sealed class SaveManager
{
    private readonly GameState _state;
    private readonly SceneManager _sceneManager;
    private readonly World _world;
    private readonly InventoryManager? _inventory;
    private readonly QuestManager? _quests;
    private readonly string _saveDir;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public string SaveDirectory => _saveDir;

    /// <summary>Nome da entidade cuja posição (Transform) é salva/restaurada junto do resto —
    /// sem isso, carregar um save sempre nasce o jogador onde o JSON da cena colocou, não onde
    /// ele estava quando salvou. Mesma convenção de <see cref="Events.EventSystem.PlayerEntityName"/>.</summary>
    public string PlayerEntityName { get; set; } = "Player";

    public SaveManager(GameState state, SceneManager sceneManager, World world, string gameName = "AuroraGame",
        InventoryManager? inventory = null, QuestManager? quests = null)
    {
        _state = state;
        _sceneManager = sceneManager;
        _world = world;
        _inventory = inventory;
        _quests = quests;
        _saveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Sanitize(gameName), "saves");
    }

    // ---- API principal ----

    /// <summary>Salva no slot dado (padrão: 0). Cria o diretório se não existir.</summary>
    public void Save(int slot = 0) => Write(SlotPath(slot), slot);

    /// <summary>Carrega o slot dado. Retorna false se o arquivo não existir.</summary>
    public bool Load(int slot = 0) => Read(SlotPath(slot));

    public bool HasSave(int slot = 0) => File.Exists(SlotPath(slot));

    public void Delete(int slot = 0)
    {
        string path = SlotPath(slot);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Retorna metadados do slot sem fazer load completo. Null se não existir.</summary>
    public SaveInfo? GetInfo(int slot = 0) => ReadInfo(SlotPath(slot), slot);

    // ---- Auto-save ----

    public void AutoSave() => Write(AutoSavePath, slot: -1);
    public bool LoadAutoSave() => Read(AutoSavePath);
    public bool HasAutoSave() => File.Exists(AutoSavePath);

    // ---- Helpers ----

    private string SlotPath(int slot) => Path.Combine(_saveDir, $"slot_{slot}.json");
    private string AutoSavePath => Path.Combine(_saveDir, "autosave.json");

    private void Write(string path, int slot)
    {
        Directory.CreateDirectory(_saveDir);

        float? playerX = null, playerY = null;
        if (_world.TryFind(PlayerEntityName, out var player) && player.Get<Transform>() is { } transform)
        {
            playerX = transform.Position.X;
            playerY = transform.Position.Y;
        }

        var dto = new SaveDto(
            Slot: slot,
            Scene: _sceneManager.CurrentScene,
            SavedAt: DateTime.UtcNow,
            Variables: new Dictionary<string, float>(_state.Variables),
            Switches: new Dictionary<string, bool>(_state.Switches),
            Items: _inventory is null ? [] : new Dictionary<string, int>(_inventory.Items),
            QuestStages: _quests is null ? [] : new Dictionary<string, int>(_quests.Stages),
            PlayerX: playerX,
            PlayerY: playerY);

        File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOpts));
    }

    private bool Read(string path)
    {
        if (!File.Exists(path))
            return false;

        SaveDto? dto;
        try { dto = JsonSerializer.Deserialize<SaveDto>(File.ReadAllText(path), JsonOpts); }
        catch { return false; }

        if (dto is null)
            return false;

        _state.LoadFromDictionaries(dto.Variables, dto.Switches);
        // Items/QuestStages podem faltar num save de antes desta feature - trata como vazio.
        if (dto.Items is not null) _inventory?.LoadFromDictionary(dto.Items);
        if (dto.QuestStages is not null) _quests?.LoadFromDictionary(dto.QuestStages);

        if (dto.Scene is not null)
        {
            // Load (não LoadWithFade): precisamos que a entidade Player já exista pra aplicar
            // a posição salva no mesmo instante — LoadWithFade só carrega de fato alguns
            // frames depois (após o fade pro preto), quando este método já teria retornado.
            _sceneManager.Load(dto.Scene);

            if (dto.PlayerX is { } px && dto.PlayerY is { } py
                && _world.TryFind(PlayerEntityName, out var player) && player.Get<Transform>() is { } transform)
            {
                transform.Position = new Vector2(px, py);
            }
        }

        return true;
    }

    private SaveInfo? ReadInfo(string path, int slot)
    {
        if (!File.Exists(path))
            return null;

        SaveDto? dto;
        try { dto = JsonSerializer.Deserialize<SaveDto>(File.ReadAllText(path), JsonOpts); }
        catch { return null; }

        return dto is null ? null
            : new SaveInfo(slot, dto.Scene ?? "", dto.SavedAt, dto.Variables);
    }

    private static string Sanitize(string name)
        => string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-' or ' '));

    // ---- DTO interno ----

    private sealed record SaveDto(
        int Slot,
        string? Scene,
        DateTime SavedAt,
        Dictionary<string, float> Variables,
        Dictionary<string, bool> Switches,
        Dictionary<string, int>? Items = null,
        Dictionary<string, int>? QuestStages = null,
        float? PlayerX = null,
        float? PlayerY = null);
}
