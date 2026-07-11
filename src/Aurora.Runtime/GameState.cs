using System.Text.Json;

namespace Aurora.Runtime;

/// <summary>
/// Variáveis (Gold, Life, XP…) e switches (flags) globais do jogo — o modelo
/// RPG Maker. Eventos leem/escrevem aqui; o sistema de save serializa tudo.
/// </summary>
public sealed class GameState
{
    private readonly Dictionary<string, float> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _switches = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Disparado em qualquer mudança (HUD, triggers SwitchOn).</summary>
    public event Action? Changed;

    public IReadOnlyDictionary<string, float> Variables => _variables;
    public IReadOnlyDictionary<string, bool> Switches => _switches;

    public float GetVariable(string name, float fallback = 0f)
        => _variables.TryGetValue(name, out float value) ? value : fallback;

    public void SetVariable(string name, float value)
    {
        _variables[name] = value;
        Changed?.Invoke();
    }

    public void AddVariable(string name, float delta)
        => SetVariable(name, GetVariable(name) + delta);

    public bool GetSwitch(string name)
        => _switches.TryGetValue(name, out bool on) && on;

    public void SetSwitch(string name, bool on)
    {
        _switches[name] = on;
        Changed?.Invoke();
    }

    public void Clear()
    {
        _variables.Clear();
        _switches.Clear();
        Changed?.Invoke();
    }

    public string ToJson()
        => JsonSerializer.Serialize(new StateDto(_variables, _switches),
            new JsonSerializerOptions { WriteIndented = true });

    public void LoadJson(string json)
    {
        var dto = JsonSerializer.Deserialize<StateDto>(json)
            ?? throw new InvalidDataException("Save inválido.");

        _variables.Clear();
        _switches.Clear();
        foreach (var (key, value) in dto.Variables)
            _variables[key] = value;
        foreach (var (key, value) in dto.Switches)
            _switches[key] = value;
        Changed?.Invoke();
    }

    internal void LoadFromDictionaries(
        IReadOnlyDictionary<string, float> variables,
        IReadOnlyDictionary<string, bool> switches)
    {
        _variables.Clear();
        _switches.Clear();
        foreach (var (k, v) in variables) _variables[k] = v;
        foreach (var (k, v) in switches) _switches[k] = v;
        Changed?.Invoke();
    }

    private sealed record StateDto(Dictionary<string, float> Variables, Dictionary<string, bool> Switches);
}
