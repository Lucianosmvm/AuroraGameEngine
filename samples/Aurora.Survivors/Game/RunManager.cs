using Aurora.Runtime;

namespace Survivors;

/// <summary>
/// O estado de UMA partida: tempo, nível, XP e quais melhorias já foram escolhidas. Não conhece
/// entidade nem cena — só números e a ficha do jogador —, então dá pra testar e mexer sem abrir
/// o jogo.
///
/// <para>As variáveis do <see cref="GameState"/> (<c>Xp</c>, <c>Nivel</c>, <c>Tempo</c>,
/// <c>Kills</c>) existem porque a HUD lê variável, não objeto: quem escreve nelas é este
/// gerenciador (e os coletáveis, no caso do XP).</para>
/// </summary>
public sealed class RunManager
{
    private readonly Dictionary<string, int> _niveis = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Upgrade> _opcoes = [];

    public float Tempo { get; private set; }
    public int Nivel { get; private set; } = 1;

    /// <summary>XP necessário pro próximo nível.</summary>
    public float XpProximo { get; private set; } = 6f;

    /// <summary>Moedas que o jogador tinha ao começar — o resultado da partida mostra a
    /// diferença, não o total da conta.</summary>
    public int MoedasNoInicio { get; private set; }

    /// <summary>As melhorias sorteadas pra escolha atual (vazio fora do level up).</summary>
    public IReadOnlyList<Upgrade> Opcoes => _opcoes;

    /// <summary>Zera tudo pra uma partida nova. Chame ANTES de carregar a cena da arena.</summary>
    public void Iniciar(GameState state, InventoryManager inventario)
    {
        _niveis.Clear();
        _opcoes.Clear();
        Tempo = 0f;
        Nivel = 1;
        XpProximo = XpParaNivel(1);
        MoedasNoInicio = inventario.GetCount("Moeda");

        state.SetVariable("Xp", 0f);
        state.SetVariable("XpPct", 0f);
        state.SetVariable("Nivel", 1f);
        state.SetVariable("Tempo", 0f);
        state.SetVariable("Kills", 0f);
    }

    /// <summary>Avança o relógio da partida e mantém as variáveis da HUD em dia.</summary>
    public void Update(float deltaTime, GameState state)
    {
        Tempo += deltaTime;
        state.SetVariable("Tempo", MathF.Floor(Tempo));

        float xp = state.GetVariable("Xp");
        state.SetVariable("XpPct", XpProximo > 0f ? Math.Clamp(xp / XpProximo * 100f, 0f, 100f) : 0f);
    }

    public bool PodeSubirDeNivel(GameState state) => state.GetVariable("Xp") >= XpProximo;

    /// <summary>Consome o XP, sobe o nível e sorteia as opções. O nível sobe AQUI (não na
    /// escolha) pra uma enxurrada de gemas não abrir a mesma tela várias vezes seguidas com o
    /// mesmo nível.</summary>
    public void AbrirNivel(GameState state, int quantidadeDeOpcoes = 3)
    {
        state.SetVariable("Xp", MathF.Max(0f, state.GetVariable("Xp") - XpProximo));
        Nivel++;
        XpProximo = XpParaNivel(Nivel);
        state.SetVariable("Nivel", Nivel);

        Sortear(quantidadeDeOpcoes);
    }

    /// <summary>Aplica a opção escolhida na ficha do jogador. Devolve a melhoria aplicada (ou
    /// null se o índice não existe), pro chamador poder reagir — ex.: acertar a vida máxima.</summary>
    public Upgrade? Escolher(int indice, PlayerStats stats)
    {
        if (indice < 0 || indice >= _opcoes.Count)
            return null;

        var upgrade = _opcoes[indice];
        upgrade.Aplicar(stats);
        _niveis[upgrade.Id] = NivelDe(upgrade.Id) + 1;
        _opcoes.Clear();
        return upgrade;
    }

    /// <summary>Quantas vezes esta melhoria já foi escolhida nesta partida.</summary>
    public int NivelDe(string id) => _niveis.TryGetValue(id, out int nivel) ? nivel : 0;

    /// <summary>Curva de XP. Cresce quase linear no começo e abre no fim, que é o que segura o
    /// ritmo de "sobe de nível a cada poucos segundos" sem travar a partida longa.</summary>
    private static float XpParaNivel(int nivel) => MathF.Round(6f + (nivel - 1) * 5f + nivel * nivel * 0.4f);

    /// <summary>Sorteia opções distintas entre as que ainda não bateram no teto. Se sobrar menos
    /// que o pedido (tudo no máximo), a tela mostra menos botões em vez de repetir.</summary>
    private void Sortear(int quantidade)
    {
        _opcoes.Clear();

        var disponiveis = UpgradeCatalog.Todos
            .Where(u => NivelDe(u.Id) < u.MaxNivel)
            .OrderBy(_ => Random.Shared.Next())
            .Take(quantidade);

        _opcoes.AddRange(disponiveis);
    }
}
