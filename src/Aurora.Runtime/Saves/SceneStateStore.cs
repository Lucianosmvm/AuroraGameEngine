using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Events;

namespace Aurora.Runtime.Saves;

/// <summary>
/// Guarda o que já aconteceu com as entidades marcadas com <see cref="Persistent"/>, por cena.
///
/// <para>Resolve o buraco entre "o mundo é recriado do arquivo a cada carga" e "o jogador espera
/// que o chefe que ele matou continue morto". Vale nas duas travessias: sair da sala e voltar
/// dentro da mesma partida, e fechar o jogo e carregar o save.</para>
///
/// <para>Só guarda FATOS booleanos — destruída, gatilho já disparado, efeito ligado/desligado.
/// Nada de vida ou posição (ver <see cref="Persistent"/>), e nada de entidade sem a marca: um
/// jogo que destrói projétil todo frame encheria o save de nomes de bala.</para>
/// </summary>
public sealed class SceneStateStore
{
    /// <summary>Fatos de uma cena. Listas (e não conjuntos) na superfície de serialização porque
    /// é o que o JSON representa direto; a busca é por nome em cena, sempre pequena.</summary>
    public sealed class SceneFacts
    {
        public HashSet<string> Destroyed { get; init; } = new(StringComparer.Ordinal);
        public HashSet<string> TriggersFired { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, bool> Active { get; init; } = new(StringComparer.Ordinal);

        public bool IsEmpty => Destroyed.Count == 0 && TriggersFired.Count == 0 && Active.Count == 0;
    }

    private readonly Dictionary<string, SceneFacts> _byScene = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cena carregada agora. O <see cref="World"/> não conhece o SceneManager, então
    /// quem sabe em que cena um Destroy aconteceu é este campo — atualizado a cada carga.</summary>
    public string? CurrentScene { get; set; }

    public bool HasAnything => _byScene.Values.Any(facts => !facts.IsEmpty);

    private SceneFacts? FactsFor(string? scene, bool create)
    {
        if (string.IsNullOrEmpty(scene))
            return null;

        if (_byScene.TryGetValue(scene, out var facts))
            return facts;

        if (!create)
            return null;

        facts = new SceneFacts();
        _byScene[scene] = facts;
        return facts;
    }

    // ---- Registro ----

    /// <summary>Registra que uma entidade morreu pra valer. Chamado pelo World em todo Destroy;
    /// entidade sem <see cref="Persistent"/> é ignorada aqui mesmo, pra o registro não crescer
    /// com projétil e efeito.</summary>
    public void RecordDestroyed(Entity entity)
    {
        if (entity.Get<Persistent>() is null)
            return;

        FactsFor(CurrentScene, create: true)?.Destroyed.Add(entity.Name);
    }

    /// <summary>Registra que o gatilho <c>Once</c> de uma entidade já disparou — o baú que já foi
    /// aberto não reabre ao voltar na sala.</summary>
    public void RecordTriggerFired(Entity entity)
    {
        if (entity.Get<Persistent>() is null)
            return;

        FactsFor(CurrentScene, create: true)?.TriggersFired.Add(entity.Name);
    }

    /// <summary>Registra o último SetActive aplicado (tocha acesa, chuva ligada).</summary>
    public void RecordActive(Entity entity, bool on)
    {
        if (entity.Get<Persistent>() is null)
            return;

        var facts = FactsFor(CurrentScene, create: true);
        if (facts is not null)
            facts.Active[entity.Name] = on;
    }

    // ---- Aplicação ----

    /// <summary>
    /// Reaplica os fatos guardados sobre a cena recém-montada. Chamado pelo SceneManager logo
    /// depois de o serializador criar as entidades e ANTES de qualquer update — assim nada chega
    /// a rodar um frame com o inimigo que devia estar morto.
    /// </summary>
    public void ApplyTo(string scene, World world)
    {
        if (FactsFor(scene, create: false) is not { } facts)
            return;

        foreach (string name in facts.Destroyed)
        {
            if (world.TryFind(name, out var entity))
                world.Destroy(entity);
        }

        foreach (string name in facts.TriggersFired)
        {
            if (world.TryFind(name, out var entity) && entity.Get<EventTrigger>() is { } trigger)
                trigger.Fired = true;
        }

        foreach (var (name, on) in facts.Active)
        {
            if (!world.TryFind(name, out var entity))
                continue;

            if (entity.Get<ParticleEmitter>() is { } particles)
                particles.Emitting = on;

            if (entity.Get<Light2D>() is { } light)
                light.Enabled = on;
        }
    }

    // ---- Persistência ----

    public void Clear() => _byScene.Clear();

    /// <summary>Fotografia pro arquivo de save. Cena sem nenhum fato fica de fora, pra um jogo
    /// que nunca usou <see cref="Persistent"/> não carregar um bloco vazio em todo save.</summary>
    public Dictionary<string, SceneFacts> ToSnapshot()
        => _byScene.Where(entry => !entry.Value.IsEmpty)
                   .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>Substitui (não mistura) o conteúdo. Carregar o slot 2 depois do slot 1 não pode
    /// herdar os inimigos que morreram na outra partida.</summary>
    public void LoadSnapshot(Dictionary<string, SceneFacts>? snapshot)
    {
        _byScene.Clear();

        if (snapshot is null)
            return;

        foreach (var (scene, facts) in snapshot)
            _byScene[scene] = facts;
    }
}
