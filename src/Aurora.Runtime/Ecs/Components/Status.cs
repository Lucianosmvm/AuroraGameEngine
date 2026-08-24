using Aurora.Runtime.Database;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Efeitos de status ativos numa entidade (veneno, lentidão, blindagem). A ficha de cada um mora
/// no <see cref="StatusDatabase"/>; aqui fica só o que é por entidade: quais estão ativos e
/// quanto falta pra cada um sair.
///
/// <para>Quem aplica é a ação <c>AddStatus</c> (ou código: <c>Get&lt;Status&gt;()?.Apply("veneno")</c>).
/// A entidade não precisa nascer com o componente pra receber status — as ações criam na hora se
/// faltar; ele só é autorado na cena quando o bicho já começa com alguma coisa (<see cref="Initial"/>).</para>
/// </summary>
public sealed class Status : Behavior
{
    /// <summary>Status com que a entidade nasce, separados por vírgula: <c>"veneno, lento"</c>.
    /// Aplicados no primeiro Update — antes disso o World ainda não tem o banco pendurado.</summary>
    public string Initial = "";

    private readonly List<Entry> _active = [];
    private bool _initialApplied;

    private sealed class Entry
    {
        public required StatusDefinition Definition;
        public float Remaining;
        // Sobra de dano ainda não aplicada. O veneno acumula até fechar 1 ponto em vez de mandar
        // 0.064 por frame: assim o número na barra de vida anda de um em um (dano fracionário
        // deixaria a vida com casas decimais na HUD) e o OnDamaged de quem escuta dispara umas
        // poucas vezes por segundo, não 60.
        public float PendingDamage;
    }

    /// <summary>Ids ativos agora, na ordem em que foram aplicados. Pra HUD desenhar os ícones.</summary>
    public IEnumerable<StatusDefinition> Active => _active.Select(e => e.Definition);

    public bool Has(string id)
        => _active.Any(e => e.Definition.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Produto do SpeedMultiplier de todos os status ativos. 1 quando não há nenhum —
    /// é o que os controladores de movimento multiplicam na velocidade.</summary>
    public float SpeedMultiplier
    {
        get
        {
            float total = 1f;
            foreach (var entry in _active)
                total *= entry.Definition.SpeedMultiplier;
            return total;
        }
    }

    /// <summary>Produto do DamageTakenMultiplier de todos os status ativos. Lido pelo
    /// <see cref="World.Damage"/>.</summary>
    public float DamageTakenMultiplier
    {
        get
        {
            float total = 1f;
            foreach (var entry in _active)
                total *= entry.Definition.DamageTakenMultiplier;
            return total;
        }
    }

    /// <summary>
    /// Aplica um status pelo id. <paramref name="durationOverride"/> maior que 0 manda na duração
    /// da ficha (a mesma "poção de veneno" durar mais na mão de um chefe).
    /// </summary>
    /// <returns>False quando o id não está no banco, ou quando já estava ativo e a ficha diz pra
    /// não renovar.</returns>
    public bool Apply(string id, float durationOverride = 0f)
    {
        if (World?.StatusDatabase?.Get(id) is not { } definition)
        {
            Console.Error.WriteLine($"[Status] '{id}' não está no banco de status — ignorado.");
            return false;
        }

        return Apply(definition, durationOverride);
    }

    public bool Apply(StatusDefinition definition, float durationOverride = 0f)
    {
        float duration = durationOverride > 0f ? durationOverride : definition.Duration;

        if (_active.FirstOrDefault(e => e.Definition.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase))
            is { } existing)
        {
            if (!definition.RefreshOnReapply)
                return false;

            existing.Remaining = duration;
            return true;
        }

        _active.Add(new Entry { Definition = definition, Remaining = duration });
        return true;
    }

    public bool Remove(string id)
        => _active.RemoveAll(e => e.Definition.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;

    public void RemoveAll() => _active.Clear();

    public override void Update(float deltaTime)
    {
        ApplyInitial();

        if (_active.Count == 0)
            return;

        // Cópia: o dano do veneno pode matar (e destruir) a entidade, e a morte mexe na lista.
        foreach (var entry in _active.ToList())
        {
            if (entry.Definition.DamagePerSecond != 0f)
                Tick(entry, deltaTime);

            // Duração 0 = permanente: nunca expira sozinho.
            if (entry.Definition.Duration <= 0f && entry.Remaining <= 0f)
                continue;

            entry.Remaining -= deltaTime;
            if (entry.Remaining <= 0f)
                _active.Remove(entry);
        }
    }

    private void Tick(Entry entry, float deltaTime)
    {
        if (World is null)
            return;

        entry.PendingDamage += entry.Definition.DamagePerSecond * deltaTime;

        if (entry.PendingDamage >= 1f)
        {
            float amount = entry.PendingDamage;
            entry.PendingDamage = 0f;

            // Sem "source": veneno não tem quem bata, e passar a própria entidade faria um
            // OnDamaged de contra-ataque revidar contra ela mesma.
            World.Damage(Entity, amount, source: null, ignoreHitFrames: true);
        }
        else if (entry.PendingDamage <= -1f)
        {
            World.Heal(Entity, -entry.PendingDamage);
            entry.PendingDamage = 0f;
        }
    }

    private void ApplyInitial()
    {
        if (_initialApplied)
            return;

        _initialApplied = true;
        if (Initial.Length == 0)
            return;

        foreach (string id in Initial.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries
                                                      | StringSplitOptions.TrimEntries))
            Apply(id);
    }
}
