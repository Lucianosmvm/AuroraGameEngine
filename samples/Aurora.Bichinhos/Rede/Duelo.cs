using Aurora.Runtime.Net;

namespace Bichinhos;

/// <summary>
/// Duelo contra um amigo no mesmo Wi-Fi (ou no roteador do celular de um dos dois).
///
/// <para><b>Como a luta anda sem mandar a luta pela rede (lockstep).</b> Os dois celulares montam
/// a MESMA <see cref="Batalha"/>: o bicho de quem criou a sala é sempre o lado
/// <see cref="Lado.Jogador"/> e o de quem entrou é o <see cref="Lado.Inimigo"/>. A cada turno o
/// host junta as duas escolhas, sorteia uma semente e manda os três números; cada celular roda
/// <see cref="Batalha.TurnoDuelo"/> com eles e chega no mesmo resultado. A tela só vira de
/// perspectiva (<see cref="MeuLado"/>) — cada um se vê embaixo.</para>
///
/// <para>Por que não mandar os eventos prontos: RPC da engine leva string curta (32 caracteres), e
/// três inteiros são menores e mais simples que qualquer formato de texto. Pra não ficar à mercê
/// de uma diferença boba entre os aparelhos, o host manda junto a vida dos dois depois do turno e
/// o cliente corrige se discordar.</para>
/// </summary>
public sealed class Duelo : IDisposable
{
    /// <summary>Só acha e só entra em sala de Bichinhos — não aparece partida de outro jogo da engine.</summary>
    public const string IdDoJogo = "Bichinhos";

    /// <summary>Muda quando a regra da batalha mudar: versões diferentes simulariam turnos
    /// diferentes, então nem começam.</summary>
    public const int Versao = 1;

    private const string RpcBicho = "bichos.bicho";
    private const string RpcEscolha = "bichos.escolha";
    private const string RpcTurno = "bichos.turno";

    private readonly NetSession _net;
    private readonly Bicho _meu;

    // Congelado na criação: o humor muda com o relógio, e o valor que o outro celular recebe
    // tem que ser exatamente o que este usa na conta, senão os danos saem diferentes.
    private readonly Lutador _meuLutador;
    private readonly Random _rng = new();
    private readonly Queue<List<EventoBatalha>> _turnos = new();

    private Lutador? _rival;
    private int? _minhaEscolha;
    private int? _escolhaDoOutro;
    private bool _enviouBicho;

    public bool SouHost { get; }
    public Batalha? Batalha { get; private set; }

    /// <summary>De que lado da batalha comum este celular está.</summary>
    public Lado MeuLado => SouHost ? Lado.Jogador : Lado.Inimigo;

    public bool Pronto => Batalha is not null;
    /// <summary>Motivo pra encerrar (amigo saiu, versão diferente, não achou a sala). Null = tudo bem.</summary>
    public string? Erro { get; private set; }

    public bool EsperandoOutro => _minhaEscolha is not null;

    private Duelo(NetSession net, Bicho meu, bool souHost)
    {
        _net = net;
        _meu = meu;
        _meuLutador = Lutador.De(meu);
        SouHost = souHost;

        net.Rpc.On(RpcBicho, AoReceberBicho);
        net.Rpc.On(RpcEscolha, AoReceberEscolha);
        net.Rpc.On(RpcTurno, AoReceberTurno);
        net.PlayerJoined += AoEntrarAlguem;
        net.PlayerLeft += AoSairAlguem;
        net.JoinedRoom += AoEntrarNaSala;
        net.LeftRoom += AoSairDaSala;
    }

    /// <summary>Cria a sala e espera o amigo. Aparece na busca de quem estiver no mesmo Wi-Fi.</summary>
    public static Duelo Hospedar(NetSession net, Bicho meu)
    {
        net.GameId = IdDoJogo;
        net.RoomName = $"{meu.Nome} Nv {meu.Nivel}";
        var duelo = new Duelo(net, meu, souHost: true);
        try
        {
            net.StartHost(meu.Nome, maxPlayers: 2);
        }
        catch (Exception ex)
        {
            // Porta ocupada (outro jogo aberto) ou rede desligada.
            duelo.Erro = $"Não deu pra criar a sala: {ex.Message}";
        }
        return duelo;
    }

    public static Duelo Entrar(NetSession net, Bicho meu, NetRoomInfo sala)
    {
        net.GameId = IdDoJogo;
        var duelo = new Duelo(net, meu, souHost: false);
        net.Join(sala, meu.Nome);
        return duelo;
    }

    public static Duelo Entrar(NetSession net, Bicho meu, string ip)
    {
        net.GameId = IdDoJogo;
        var duelo = new Duelo(net, meu, souHost: false);
        try
        {
            net.Join(ip, playerName: meu.Nome);
        }
        catch (Exception ex)
        {
            duelo.Erro = $"Endereço inválido: {ex.Message}";
        }
        return duelo;
    }

    // ------------------------------------------------------------------ conexão

    private void AoEntrarAlguem(NetPeer peer)
    {
        // Host: o amigo chegou. Cliente: o host "entra" na lista quando a gente conecta.
        if (SouHost && peer.Id != _net.SelfId)
            EnviarMeuBicho();
    }

    private void AoEntrarNaSala(byte _) => EnviarMeuBicho();

    private void AoSairAlguem(NetPeer peer)
    {
        if (peer.Id != _net.SelfId && Batalha is { Acabou: false })
            Erro = "Seu amigo saiu do duelo.";
        else if (peer.Id != _net.SelfId && Batalha is null)
            _rival = null;   // saiu antes de começar: a sala volta a esperar
    }

    private void AoSairDaSala(NetDisconnectReason motivo)
    {
        if (Batalha is { Acabou: true })
            return;

        Erro = motivo switch
        {
            NetDisconnectReason.ConnectFailed => "Não achei a sala. Confira se estão no mesmo Wi-Fi.",
            NetDisconnectReason.Rejected => "A sala já está cheia.",
            NetDisconnectReason.TimedOut => "A conexão caiu.",
            NetDisconnectReason.HostShutdown => Batalha is null ? "O amigo fechou a sala." : "Seu amigo saiu do duelo.",
            _ => Batalha is null ? "Não deu pra entrar na sala." : "O duelo foi encerrado.",
        };
    }

    private void EnviarMeuBicho()
    {
        if (_enviouBicho)
            return;
        _enviouBicho = true;

        _net.Rpc.Send(NetRpcTarget.Others, RpcBicho, _meu.EspecieId, _meuLutador.Nivel, _meuLutador.Estagio, _meuLutador.Humor, Versao);
    }

    private void AoReceberBicho(NetRpcArgs a)
    {
        if (a.SenderId == _net.SelfId || Batalha is not null)
            return;

        if (a.GetInt(4) != Versao)
        {
            Erro = "Vocês estão com versões diferentes do jogo. Atualizem os dois.";
            return;
        }

        string especieId = a.GetString(0);
        if (Catalogo.Atual.Especies.All(e => e.Id != especieId))
        {
            Erro = "O bicho do seu amigo não existe nesta versão do jogo.";
            return;
        }

        var especie = Catalogo.Atual.Especie(especieId);
        int nivel = Math.Clamp(a.GetInt(1), 1, Catalogo.Atual.NivelMaximo);
        int estagio = Math.Clamp(a.GetInt(2), 0, 2);
        _rival = MontarLutador(especie, nivel, estagio, a.GetFloat(3));

        // Responde sempre com o nosso: se o primeiro envio saiu antes do outro lado terminar de
        // conectar e se perdeu, sem esta resposta os dois ficariam esperando pra sempre.
        _enviouBicho = false;
        EnviarMeuBicho();

        var meu = _meuLutador;
        // Mesma ordem nos dois celulares: bicho do host sempre é o "Jogador" da batalha comum.
        var (ladoJogador, ladoInimigo) = SouHost ? (meu, _rival) : (_rival, meu);
        Batalha = new Batalha(ladoJogador, ladoInimigo, Dificuldade.Normal, new Random(0)) { Duelo = true };
    }

    private static Lutador MontarLutador(Especie especie, int nivel, int estagio, float humor)
    {
        var atributos = Catalogo.Atual.AtributosNoNivel(especie, nivel, estagio);
        return new Lutador
        {
            Especie = especie,
            Nivel = nivel,
            Estagio = estagio,
            Atributos = atributos,
            Golpes = Catalogo.Atual.GolpesNoNivel(especie, nivel),
            Humor = Math.Clamp(humor, 0.9f, 1.1f),
            Vida = atributos.Vida,
        };
    }

    // ------------------------------------------------------------------ turnos

    /// <summary>Esta tela escolheu: índice do golpe ou <see cref="AcaoDuelo.Desistir"/>.</summary>
    public void Escolher(int acao)
    {
        if (Batalha is null || Batalha.Acabou || _minhaEscolha is not null)
            return;

        _minhaEscolha = acao;
        if (SouHost)
            TentarResolver();
        else
            _net.Rpc.Send(NetRpcTarget.Host, RpcEscolha, acao);
    }

    private void AoReceberEscolha(NetRpcArgs a)
    {
        if (!SouHost || a.SenderId == _net.SelfId)
            return;

        _escolhaDoOutro = a.GetInt(0);
        TentarResolver();
    }

    private void TentarResolver()
    {
        if (Batalha is null || _minhaEscolha is not { } minha || _escolhaDoOutro is not { } dele)
            return;

        int semente = _rng.Next();
        var eventos = Batalha.TurnoDuelo(minha, dele, semente);
        _net.Rpc.Send(NetRpcTarget.Others, RpcTurno, minha, dele, semente, Batalha.Jogador.Vida, Batalha.Inimigo.Vida);

        _minhaEscolha = null;
        _escolhaDoOutro = null;
        _turnos.Enqueue(eventos);
    }

    private void AoReceberTurno(NetRpcArgs a)
    {
        if (SouHost || Batalha is null)
            return;

        var eventos = Batalha.TurnoDuelo(a.GetInt(0), a.GetInt(1), a.GetInt(2));

        // Conferência: se a conta daqui divergiu da do host, vale a do host.
        int vidaJogador = a.GetInt(3), vidaInimigo = a.GetInt(4);
        if (Batalha.Jogador.Vida != vidaJogador || Batalha.Inimigo.Vida != vidaInimigo)
        {
            Console.Error.WriteLine($"[Duelo] Turno divergiu do host ({Batalha.Jogador.Vida}/{Batalha.Inimigo.Vida} vs {vidaJogador}/{vidaInimigo}); corrigindo.");
            Batalha.Jogador.Vida = vidaJogador;
            Batalha.Inimigo.Vida = vidaInimigo;
            eventos.Add(new Dano(Lado.Jogador, 0, vidaJogador, 1f));
            eventos.Add(new Dano(Lado.Inimigo, 0, vidaInimigo, 1f));
        }

        _minhaEscolha = null;
        _turnos.Enqueue(eventos);
    }

    /// <summary>Próximo turno resolvido pra tela tocar, se já chegou.</summary>
    public bool TryProximoTurno(out List<EventoBatalha> eventos) => _turnos.TryDequeue(out eventos!);

    // ------------------------------------------------------------------ fim

    public void Dispose()
    {
        _net.Rpc.Off(RpcBicho);
        _net.Rpc.Off(RpcEscolha);
        _net.Rpc.Off(RpcTurno);
        _net.PlayerJoined -= AoEntrarAlguem;
        _net.PlayerLeft -= AoSairAlguem;
        _net.JoinedRoom -= AoEntrarNaSala;
        _net.LeftRoom -= AoSairDaSala;
        _net.Leave();
    }
}
