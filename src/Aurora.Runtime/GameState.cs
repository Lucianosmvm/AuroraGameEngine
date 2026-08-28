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
    private readonly Dictionary<string, string> _texts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Disparado em qualquer mudança (HUD, triggers SwitchOn).</summary>
    public event Action? Changed;

    public IReadOnlyDictionary<string, float> Variables => _variables;
    public IReadOnlyDictionary<string, bool> Switches => _switches;

    /// <summary>
    /// Variáveis de texto — nome do jogador, resposta digitada, apelido de save. Separadas das
    /// numéricas porque são coisas diferentes: somar não faz sentido num nome, e comparar
    /// "maior que" tampouco. Um jogo que nunca usa texto nem paga por elas.
    /// </summary>
    public IReadOnlyDictionary<string, string> Texts => _texts;

    public float GetVariable(string name, float fallback = 0f)
        => _variables.TryGetValue(name, out float value) ? value : fallback;

    public void SetVariable(string name, float value)
    {
        _variables[name] = value;
        Changed?.Invoke();
    }

    public void AddVariable(string name, float delta)
        => SetVariable(name, GetVariable(name) + delta);

    /// <summary>Texto guardado, ou <paramref name="fallback"/> se ninguém escreveu ainda.</summary>
    public string GetText(string name, string fallback = "")
        => _texts.TryGetValue(name, out string? value) ? value : fallback;

    public void SetText(string name, string value)
    {
        // Guarda "" em vez de remover: quem apagou o campo quis apagar, e uma chave ausente
        // faria o {Token} do UiText cair no fallback numérico e desenhar "0".
        _texts[name] = value ?? "";
        Changed?.Invoke();
    }

    /// <summary>Se existe texto com esse nome — o que separa "vazio" de "nunca preenchido", e é
    /// como a interpolação decide entre variável de texto e numérica.</summary>
    public bool HasText(string name) => _texts.ContainsKey(name);

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
        _texts.Clear();
        Changed?.Invoke();
    }

    public string ToJson()
        => JsonSerializer.Serialize(new StateDto(_variables, _switches, _texts),
            new JsonSerializerOptions { WriteIndented = true });

    public void LoadJson(string json)
    {
        var dto = JsonSerializer.Deserialize<StateDto>(json)
            ?? throw new InvalidDataException("Save inválido.");

        LoadFromDictionaries(dto.Variables, dto.Switches, dto.Texts);
    }

    internal void LoadFromDictionaries(
        IReadOnlyDictionary<string, float> variables,
        IReadOnlyDictionary<string, bool> switches,
        IReadOnlyDictionary<string, string>? texts = null)
    {
        _variables.Clear();
        _switches.Clear();
        _texts.Clear();
        foreach (var (k, v) in variables) _variables[k] = v;
        foreach (var (k, v) in switches) _switches[k] = v;

        // Null num save gravado antes das variáveis de texto existirem — carrega como vazio em
        // vez de estourar, que é a mesma política de Items/QuestStages.
        if (texts is not null)
            foreach (var (k, v) in texts) _texts[k] = v;

        Changed?.Invoke();
    }

    private sealed record StateDto(
        Dictionary<string, float> Variables,
        Dictionary<string, bool> Switches,
        Dictionary<string, string>? Texts = null);
}
