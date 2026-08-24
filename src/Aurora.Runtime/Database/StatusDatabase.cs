using System.Text.Json;

namespace Aurora.Runtime.Database;

/// <summary>
/// Ficha de um efeito de status — o "State" do RPG Maker: veneno, lentidão, força, blindagem.
///
/// <para>Existe como dado e não como script porque é sempre a mesma conta: dura N segundos, tira
/// tanto por segundo, multiplica velocidade e dano recebido. Sem cadastro, cada jogo reescreve
/// esse mesmo Behavior com números diferentes.</para>
/// </summary>
public sealed class StatusDefinition
{
    /// <summary>Chave usada nas ações AddStatus/RemoveStatus e no campo Initial do componente.</summary>
    public string Id = "";

    /// <summary>Nome exibido. Vazio = usa o Id.</summary>
    public string Name = "";

    /// <summary>Ícone pra HUD, relativo a Assets. A engine não desenha sozinha — quem monta a
    /// barra de status lê isto.</summary>
    public string Icon = "";

    /// <summary>Segundos até sair sozinho. 0 = permanente até alguém remover (maldição, buff de
    /// equipamento).</summary>
    public float Duration;

    /// <summary>Dano por segundo enquanto durar. Negativo cura — é assim que "regeneração" sai do
    /// mesmo campo que "veneno", sem um segundo conceito.</summary>
    public float DamagePerSecond;

    /// <summary>Multiplica a velocidade de quem tem o status. 1 = não mexe; 0.5 = lentidão;
    /// 1.5 = pressa. Os controladores de movimento leem isso.</summary>
    public float SpeedMultiplier = 1f;

    /// <summary>Multiplica o dano RECEBIDO. 1 = normal; 2 = vulnerável; 0 = imune.</summary>
    public float DamageTakenMultiplier = 1f;

    /// <summary>Se aplicar de novo enquanto já está ativo renova a duração (padrão) ou é
    /// ignorado. Veneno de encostar quer renovar; um "escudo de uma vez" não.</summary>
    public bool RefreshOnReapply = true;
}

/// <summary>
/// Catálogo de status, carregado de <c>database/status.json</c>.
/// Formato: <c>{ "Status": [ { "Id": "veneno", "Duration": 5, "DamagePerSecond": 4 } ] }</c>.
/// </summary>
public sealed class StatusDatabase
{
    private readonly Dictionary<string, StatusDefinition> _status = new(StringComparer.OrdinalIgnoreCase);

    public const string DefaultPath = "database/status.json";

    public IReadOnlyDictionary<string, StatusDefinition> Status => _status;

    public int Count => _status.Count;

    public StatusDefinition? Get(string id)
        => _status.TryGetValue(id, out var found) ? found : null;

    public string DisplayName(string id) => Get(id)?.Name is { Length: > 0 } name ? name : id;

    public void Add(StatusDefinition definition) => _status[definition.Id] = definition;

    public void Clear() => _status.Clear();

    public void Load(string json)
    {
        _status.Clear();

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Status", out var list))
            return;

        foreach (var element in list.EnumerateArray())
        {
            string id = GetString(element, "Id");
            if (id.Length == 0)
            {
                Console.Error.WriteLine("[StatusDatabase] Status sem \"Id\" — ignorado.");
                continue;
            }

            _status[id] = new StatusDefinition
            {
                Id = id,
                Name = GetString(element, "Name"),
                Icon = GetString(element, "Icon"),
                Duration = GetFloat(element, "Duration", 0f),
                DamagePerSecond = GetFloat(element, "DamagePerSecond", 0f),
                SpeedMultiplier = GetFloat(element, "SpeedMultiplier", 1f),
                DamageTakenMultiplier = GetFloat(element, "DamageTakenMultiplier", 1f),
                RefreshOnReapply = !element.TryGetProperty("RefreshOnReapply", out var refresh) || refresh.GetBoolean(),
            };
        }
    }

    private static string GetString(JsonElement json, string name)
        => json.TryGetProperty(name, out var prop) ? prop.GetString() ?? "" : "";

    private static float GetFloat(JsonElement json, string name, float fallback)
        => json.TryGetProperty(name, out var prop) ? prop.GetSingle() : fallback;
}
