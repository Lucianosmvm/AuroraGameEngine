using System.Text.Json;
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
    private readonly string _saveDir;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public string SaveDirectory => _saveDir;

    public SaveManager(GameState state, SceneManager sceneManager, string gameName = "AuroraGame")
    {
        _state = state;
        _sceneManager = sceneManager;
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

        var dto = new SaveDto(
            Slot: slot,
            Scene: _sceneManager.CurrentScene,
            SavedAt: DateTime.UtcNow,
            Variables: new Dictionary<string, float>(_state.Variables),
            Switches: new Dictionary<string, bool>(_state.Switches));

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

        if (dto.Scene is not null)
            _sceneManager.LoadWithFade(dto.Scene);

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
        Dictionary<string, bool> Switches);
}
