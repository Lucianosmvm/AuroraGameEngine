using System.Text.Json;
using Aurora.Runtime.Events;

namespace Aurora.Runtime.Database;

/// <summary>
/// Uma sequência de ações cadastrada uma vez e chamável de qualquer lugar — a "evento comum" do
/// RPG Maker.
///
/// <para>Sem isto, a mesma sequência ("toca som, mostra mensagem, dá o item, liga o switch")
/// precisa ser recopiada em cada porta de cada cena, e corrigir um detalhe vira caçada por
/// arquivo. Com id, existe UMA cópia: a ação <c>CallEvent</c> aponta pra cá, e um item, um botão
/// de HUD e um gatilho de cena chamam a mesma coisa.</para>
/// </summary>
public sealed class CommonEventDefinition
{
    /// <summary>Chave usada na ação <c>CallEvent</c>.</summary>
    public string Id = "";

    /// <summary>Rótulo pro editor. Não afeta o jogo.</summary>
    public string Name = "";

    /// <summary>
    /// Quando roda sozinho:
    /// <list type="bullet">
    ///   <item><c>Manual</c> (padrão) — só quando alguém chama com <c>CallEvent</c>;</item>
    ///   <item><c>OnSwitchOn</c> — uma vez, no instante em que <see cref="Switch"/> liga;</item>
    ///   <item><c>WhileSwitchOn</c> — todo frame enquanto <see cref="Switch"/> estiver ligado.</item>
    /// </list>
    ///
    /// <para>Diferente do Autorun do RPG Maker, nada aqui trava o jogador: não existe o conceito
    /// de "bloquear a entrada" nesta engine, e um evento que rodasse em laço travando o
    /// personagem seria um congelamento sem explicação. Quem quer bloquear usa uma cutscene com
    /// as próprias ações.</para>
    /// </summary>
    public string Trigger = "Manual";

    /// <summary>Switch do GameState que liga o disparo automático. Vazio com Trigger automático
    /// = nunca dispara (é o que evita um evento novo começar a rodar todo frame por descuido).</summary>
    public string Switch = "";

    public List<EventAction> Actions = [];
}

/// <summary>
/// Catálogo de eventos comuns, carregado de <c>database/common_events.json</c>.
/// Formato: <c>{ "Events": [ { "Id": "abrir_bau", "Actions": [ … ] } ] }</c>.
/// </summary>
public sealed class CommonEventDatabase
{
    private readonly Dictionary<string, CommonEventDefinition> _events = new(StringComparer.OrdinalIgnoreCase);

    public const string DefaultPath = "database/common_events.json";

    public IReadOnlyDictionary<string, CommonEventDefinition> Events => _events;

    public int Count => _events.Count;

    /// <summary>Eventos com disparo automático, na ordem de cadastro. O EventSystem varre esta
    /// lista por frame — ela é pequena de propósito (só quem tem Trigger diferente de Manual).</summary>
    public IReadOnlyList<CommonEventDefinition> Automatic { get; private set; } = [];

    public CommonEventDefinition? Get(string id)
        => _events.TryGetValue(id, out var found) ? found : null;

    public void Add(CommonEventDefinition definition)
    {
        _events[definition.Id] = definition;
        RebuildAutomatic();
    }

    public void Clear()
    {
        _events.Clear();
        Automatic = [];
    }

    public void Load(string json)
    {
        _events.Clear();

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Events", out var events))
        {
            RebuildAutomatic();
            return;
        }

        foreach (var element in events.EnumerateArray())
        {
            string id = GetString(element, "Id");
            if (id.Length == 0)
            {
                Console.Error.WriteLine("[CommonEventDatabase] Evento sem \"Id\" — ignorado (nada poderia chamá-lo).");
                continue;
            }

            var definition = new CommonEventDefinition
            {
                Id = id,
                Name = GetString(element, "Name"),
                Trigger = GetString(element, "Trigger") is { Length: > 0 } trigger ? trigger : "Manual",
                Switch = GetString(element, "Switch"),
            };

            if (element.TryGetProperty("Actions", out var actions))
                definition.Actions = EventAction.ParseList(actions);

            _events[id] = definition;
        }

        RebuildAutomatic();
    }

    private void RebuildAutomatic()
        => Automatic = [.. _events.Values.Where(e =>
               !e.Trigger.Equals("Manual", StringComparison.OrdinalIgnoreCase)
               && e.Switch.Length > 0)];

    private static string GetString(JsonElement json, string name)
        => json.TryGetProperty(name, out var prop) ? prop.GetString() ?? "" : "";
}
