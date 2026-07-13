namespace Aurora.Runtime;

/// <summary>
/// Progresso de quests: id → estágio atual (0 = não iniciada, N = etapa N concluída).
/// Eventos leem/escrevem aqui (ações SetQuestStage/AdvanceQuest, gatilho QuestStageAtLeast);
/// o texto/descrição de cada etapa é responsabilidade do autor via ShowMessage/Dialogue —
/// este gerenciador só rastreia o número do estágio, igual variável de progresso do RPG Maker.
/// </summary>
public sealed class QuestManager
{
    private readonly Dictionary<string, int> _stages = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Disparado em qualquer mudança (HUD/diário de quests reagir).</summary>
    public event Action? Changed;

    public IReadOnlyDictionary<string, int> Stages => _stages;

    public int GetStage(string quest) => _stages.TryGetValue(quest, out int stage) ? stage : 0;

    public bool IsAtLeast(string quest, int stage) => GetStage(quest) >= stage;

    public void SetStage(string quest, int stage)
    {
        _stages[quest] = stage;
        Changed?.Invoke();
    }

    public void Advance(string quest, int delta = 1) => SetStage(quest, GetStage(quest) + delta);

    public void Clear()
    {
        _stages.Clear();
        Changed?.Invoke();
    }

    internal void LoadFromDictionary(IReadOnlyDictionary<string, int> stages)
    {
        _stages.Clear();
        foreach (var (key, value) in stages)
            _stages[key] = value;
        Changed?.Invoke();
    }
}
