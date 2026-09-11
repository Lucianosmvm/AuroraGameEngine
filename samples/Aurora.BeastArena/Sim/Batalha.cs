using System.Numerics;

namespace BeastArena.Sim;

public enum ResultadoDaBatalha
{
    EmAndamento,
    VitoriaDoJogador,
    VitoriaDoInimigo,
    Empate,
}

/// <summary>
/// A batalha inteira, como lógica pura: sem janela, sem GPU, sem relógio de parede.
///
/// <para><b>Objetivo.</b> Três santuários no meio da arena. Dominar dá pontos e mana por
/// segundo; quem chega a <see cref="MetaDePontos"/> ganha na hora, e quando o tempo acaba
/// ganha quem tem mais pontos.</para>
///
/// <para><b>Passo fixo.</b> <see cref="Avancar"/> anda sempre <see cref="Passo"/> segundos. Quem
/// roda o jogo acumula o tempo real e chama quantas vezes couber. O mesmo baralho + a mesma
/// semente + os mesmos comandos nos mesmos passos dão a mesma partida — é a base de replay, de
/// teste e, mais pra frente, de PvP por lockstep ou servidor autoritativo.</para>
///
/// <para><b>Comandos em fila.</b> Jogar uma carta não muda nada na hora: <see cref="Jogar"/>
/// enfileira, e o próximo passo valida de novo e aplica. Online, é exatamente o formato que
/// viaja pela rede (equipe, carta, posição, passo).</para>
///
/// <para>Limite honesto do determinismo: a conta é em <c>float</c>. Na mesma build e mesma CPU é
/// reprodutível; entre arquiteturas diferentes (ARM x x64) pode divergir em casas decimais. Pra
/// lockstep entre aparelhos, trocar por ponto fixo — a estrutura já está pronta pra isso.</para>
/// </summary>
public sealed partial class Batalha
{
    public const float Passo = 1f / 30f;
    public const float Duracao = 180f;
    public const float TempoDeImplantacao = 1f;

    /// <summary>Pontos que ganham a partida na hora.</summary>
    public const float MetaDePontos = 200f;

    /// <summary>Pontos por segundo de CADA santuário dominado.</summary>
    public const float PontosPorSegundo = 1f;

    /// <summary>Mana que pinga sozinha, sem santuário nenhum. Baixa de propósito: quem joga só
    /// esperando a barra fica pra trás de quem disputa o mapa.</summary>
    public const float ManaBasePorSegundo = 0.3f;

    public const float ManaPorSantuarioPorSegundo = 0.06f;

    /// <summary>Fração do custo (por unidade da carta) que o abate devolve a quem matou. É o
    /// que dá fôlego a quem defende em desvantagem de santuários.</summary>
    public const float ManaPorAbate = 0.4f;

    /// <summary>Segundos de neutro a dominado com UMA tropa dentro. Mais tropas aceleram.</summary>
    public const float TempoDeCaptura = 4f;

    private readonly record struct Comando(Equipe Equipe, int IndiceDaMao, string IdDaCarta, Vector2 Posicao);

    private readonly Lado[] _lados = new Lado[2];
    private readonly List<Santuario> _santuarios = [];
    private readonly List<Unidade> _unidades = [];
    private readonly List<Projetil> _projeteis = [];
    private readonly List<FeiticoPendente> _feiticos = [];
    private readonly List<EventoDeBatalha> _eventos = [];
    private readonly Queue<Comando> _comandos = new();
    private int _proximoId = 1;

    public float Tempo { get; private set; }
    public int PassoAtual { get; private set; }
    public ResultadoDaBatalha Resultado { get; private set; }

    public float TempoRestante => MathF.Max(0f, Duracao - Tempo);
    public bool Acabou => Resultado != ResultadoDaBatalha.EmAndamento;

    public IReadOnlyList<Santuario> Santuarios => _santuarios;
    public IReadOnlyList<Unidade> Unidades => _unidades;
    public IReadOnlyList<Projetil> Projeteis => _projeteis;
    public IReadOnlyList<FeiticoPendente> Feiticos => _feiticos;

    public Batalha(IReadOnlyList<CartaDef> baralhoDoJogador, IReadOnlyList<CartaDef> baralhoDoInimigo, int semente)
    {
        var rng = new Random(semente);
        _lados[(int)Equipe.Jogador] = new Lado(Equipe.Jogador, Embaralhar(baralhoDoJogador, rng));
        _lados[(int)Equipe.Inimigo] = new Lado(Equipe.Inimigo, Embaralhar(baralhoDoInimigo, rng));

        for (int i = 0; i < Campo.Santuarios.Length; i++)
            _santuarios.Add(new Santuario { Indice = i, Posicao = Campo.Santuarios[i] });
    }

    public Lado LadoDe(Equipe equipe) => _lados[(int)equipe];

    public int SantuariosDe(Equipe equipe) => _santuarios.Count(s => s.Dono == equipe);

    /// <summary>Mana por segundo da equipe agora (base + santuários dominados).</summary>
    public float ManaPorSegundo(Equipe equipe)
        => ManaBasePorSegundo + ManaPorSantuarioPorSegundo * SantuariosDe(equipe);

    // ------------------------------------------------------------------ jogar carta

    /// <summary>
    /// Onde a equipe pode soltar CRIATURA: em volta do próprio ninho, ou em volta de um santuário
    /// que ela domina e que não está <see cref="Santuario.SobAtaque"/> — tomar o meio do mapa é o
    /// que empurra a linha de implantação pra frente. Feitiço vale no campo inteiro e não passa
    /// por aqui.
    /// </summary>
    public bool AreaDeImplantacao(Equipe equipe, Vector2 ponto)
    {
        if (!Campo.DentroDoCampo(ponto))
            return false;

        float raioNinho = Campo.RaioDeImplantacaoDoNinho;
        if (Vector2.DistanceSquared(ponto, Campo.PosicaoNinho(equipe)) <= raioNinho * raioNinho)
            return true;

        float raioSantuario = Campo.RaioDeImplantacaoDoSantuario;
        foreach (var santuario in _santuarios)
        {
            if (santuario.Dono == equipe && !santuario.SobAtaque
                && Vector2.DistanceSquared(ponto, santuario.Posicao) <= raioSantuario * raioSantuario)
                return true;
        }

        return false;
    }

    public bool PodeJogar(Equipe equipe, int indiceDaMao, Vector2 posicao, out string motivo)
    {
        motivo = "";

        if (Acabou)
        {
            motivo = "A partida acabou";
            return false;
        }

        if (indiceDaMao is < 0 or >= Lado.TamanhoDaMao)
        {
            motivo = "Carta inválida";
            return false;
        }

        var lado = LadoDe(equipe);
        var carta = lado.Mao[indiceDaMao];

        if (lado.Mana < carta.Custo)
        {
            motivo = "Mana insuficiente";
            return false;
        }

        bool lugarValido = carta.Tipo == TipoDeCarta.Feitico
            ? Campo.DentroDoCampo(posicao)
            : AreaDeImplantacao(equipe, posicao);

        if (!lugarValido)
        {
            motivo = carta.Tipo == TipoDeCarta.Feitico ? "Fora da arena" : "Fora da sua área";
            return false;
        }

        return true;
    }

    /// <summary>Enfileira a jogada pro próximo passo. Não falha: se no passo ela não valer
    /// mais (a mana foi gasta por outra jogada no mesmo frame), é descartada.</summary>
    public void Jogar(Equipe equipe, int indiceDaMao, Vector2 posicao)
    {
        if (indiceDaMao is < 0 or >= Lado.TamanhoDaMao)
            return;

        _comandos.Enqueue(new Comando(equipe, indiceDaMao, LadoDe(equipe).Mao[indiceDaMao].Id, posicao));
    }

    private void Aplicar(Comando comando)
    {
        var lado = LadoDe(comando.Equipe);

        // A carta daquele espaço pode ter trocado entre o clique e o passo (duas jogadas no mesmo
        // frame). Jogar a carta errada é pior que não jogar nada.
        if (lado.Mao[comando.IndiceDaMao].Id != comando.IdDaCarta
            || !PodeJogar(comando.Equipe, comando.IndiceDaMao, comando.Posicao, out _))
            return;

        var carta = lado.Jogar(comando.IndiceDaMao);
        lado.Mana -= carta.Custo;

        if (carta.Tipo == TipoDeCarta.Feitico)
        {
            _feiticos.Add(new FeiticoPendente
            {
                Carta = carta, Equipe = comando.Equipe, Centro = comando.Posicao, Restante = carta.Atraso,
            });
            return;
        }

        for (int i = 0; i < carta.Quantidade; i++)
        {
            var posicao = comando.Posicao + Formacao(i, carta);
            posicao = Vector2.Clamp(posicao, new Vector2(carta.Raio), new Vector2(Campo.Largura, Campo.Altura) - new Vector2(carta.Raio));
            CriarUnidade(comando.Equipe, carta, posicao);
        }

        Anotar(new EventoDeBatalha(TipoDeEvento.Implantou, comando.Equipe, comando.Posicao, carta.Nome));
    }

    /// <summary>Deslocamento da unidade <paramref name="indice"/> de um enxame em volta do ponto
    /// solto, num círculo — nascer empilhadas faria a separação espalhar tudo num tranco. Público
    /// pra prévia da tela mostrar exatamente onde cada uma vai nascer.</summary>
    public static Vector2 Formacao(int indice, CartaDef carta)
    {
        if (carta.Quantidade <= 1)
            return Vector2.Zero;

        float angulo = MathF.Tau * indice / carta.Quantidade - MathF.PI / 2f;
        return new Vector2(MathF.Cos(angulo), MathF.Sin(angulo)) * (carta.Raio * 1.3f);
    }

    // ------------------------------------------------------------------ passo

    public void Avancar()
    {
        if (Acabou)
            return;

        PassoAtual++;
        Tempo += Passo;

        foreach (var lado in _lados)
            lado.GanharMana(ManaPorSegundo(lado.Equipe) * Passo);

        while (_comandos.Count > 0)
            Aplicar(_comandos.Dequeue());

        AtualizarFeiticos();
        AtualizarNinhos();
        AtualizarUnidades();
        AtualizarProjeteis();
        Separar();

        _unidades.RemoveAll(u => !u.Viva);

        AtualizarSantuarios();

        if (!Acabou && TempoRestante <= 0f)
            Resultado = Comparar();
    }

    /// <summary>Quem tem mais pontos INTEIROS — o placar da tela mostra inteiro, e "42 x 42"
    /// com derrota por uma casa decimal pareceria roubo.</summary>
    private ResultadoDaBatalha Comparar()
    {
        float jogador = MathF.Floor(LadoDe(Equipe.Jogador).Pontos);
        float inimigo = MathF.Floor(LadoDe(Equipe.Inimigo).Pontos);

        return jogador > inimigo ? ResultadoDaBatalha.VitoriaDoJogador
            : inimigo > jogador ? ResultadoDaBatalha.VitoriaDoInimigo
            : ResultadoDaBatalha.Empate;
    }

    /// <summary>Copia os eventos acumulados desde a última colheita pra <paramref name="destino"/>
    /// e esvazia a lista interna.</summary>
    public void ColherEventos(List<EventoDeBatalha> destino)
    {
        destino.AddRange(_eventos);
        _eventos.Clear();
    }

    private void Anotar(EventoDeBatalha evento)
    {
        // Teto de segurança: quem nunca colhe (servidor, teste longo) não pode crescer sem fim.
        if (_eventos.Count < 512)
            _eventos.Add(evento);
    }

    /// <summary>Resumo numérico do estado, pra comparar duas execuções (teste de determinismo,
    /// e no futuro detecção de dessincronia no PvP).</summary>
    public long Assinatura()
    {
        long hash = PassoAtual;

        foreach (var santuario in _santuarios)
            hash = hash * 31 + (long)MathF.Round(santuario.Influencia * 1000f);

        foreach (var unidade in _unidades)
        {
            hash = hash * 31 + unidade.Id;
            hash = hash * 31 + (long)MathF.Round(unidade.Posicao.X * 1000f);
            hash = hash * 31 + (long)MathF.Round(unidade.Posicao.Y * 1000f);
            hash = hash * 31 + (long)MathF.Round(unidade.Vida * 10f);
        }

        foreach (var lado in _lados)
        {
            hash = hash * 31 + (long)MathF.Round(lado.Mana * 1000f);
            hash = hash * 31 + (long)MathF.Round(lado.Pontos * 100f);
        }

        return hash;
    }

    // ------------------------------------------------------------------ criação

    /// <param name="instantanea">Pula o segundo de implantação — pra testes e cenários montados.</param>
    internal Unidade CriarUnidade(Equipe equipe, CartaDef carta, Vector2 posicao, bool instantanea = false)
    {
        var unidade = new Unidade
        {
            Id = _proximoId++, Equipe = equipe, Carta = carta,
            Posicao = posicao, PosicaoAnterior = posicao,
            Vida = carta.Vida, VidaMaxima = carta.Vida,
            Implantando = instantanea ? 0f : TempoDeImplantacao,
            Recarga = 0.3f,
        };

        _unidades.Add(unidade);
        return unidade;
    }

    private static List<CartaDef> Embaralhar(IReadOnlyList<CartaDef> baralho, Random rng)
    {
        var copia = baralho.ToList();
        for (int i = copia.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (copia[i], copia[j]) = (copia[j], copia[i]);
        }

        return copia;
    }
}
