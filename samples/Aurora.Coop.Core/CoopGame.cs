using System.Numerics;
using Aurora.Runtime;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Net;
using Silk.NET.Maths;

namespace Aurora.Coop;

/// <summary>
/// Caça às moedas cooperativa, feita pra testar o multiplayer da engine numa rede local —
/// inclusive entre celulares.
///
/// <para>Um jogador hospeda, os outros acham a partida numa lista (ou digitam o IP). Todo mundo
/// corre pelo mesmo mapa pegando moedas, e o placar é do time.</para>
///
/// <para>Exercita, nesta ordem: busca de salas na LAN, entrada/saída de jogadores, criação e
/// destruição de entidades em rede, movimento com host autoritativo mais previsão local,
/// interpolação dos bonecos dos outros e RPC com entrega garantida pro placar.</para>
/// </summary>
public sealed class CoopGame : Game
{
    private const byte PrefabJogador = 1;
    private const byte PrefabMoeda = 2;

    private const float Velocidade = 220f;
    private const float RaioColeta = 28f;
    private const float MetadeArena = 900f;
    private const int MoedasNoMapa = 25;
    private const int MoedasPraVencer = 30;

    /// <summary>Uma cor por jogador (índice = id na sala). Sem isso ninguém distingue o próprio
    /// boneco do boneco do vizinho, que é justamente o que se quer olhar num teste de rede.</summary>
    private static readonly Color[] Cores =
    [
        Color.FromBytes(255, 255, 255),
        Color.FromBytes(255, 120, 120),
        Color.FromBytes(120, 200, 255),
        Color.FromBytes(150, 255, 150),
        Color.FromBytes(255, 220, 120),
        Color.FromBytes(220, 140, 255),
        Color.FromBytes(120, 255, 230),
        Color.FromBytes(255, 170, 200),
    ];

    private readonly TouchStick _stick = new();
    private readonly Random _random = new();
    private readonly List<ushort> _paraRemover = [];

    private Font _fonte = null!;
    private Texture2D _texJogador = null!;
    private Texture2D _texMoeda = null!;
    private Texture2D _texArvore = null!;
    private NetLobby _lobby = null!;

    private int _placar;
    private string _aviso = "";
    private float _avisoTimer;
    private float _vitoriaTimer;

    private readonly string? _autoJoin;
    private readonly bool _autoHost;
    private readonly bool _bot;
    private float _autoExit;
    private float _relatorio;

    /// <param name="autoHost">Hospeda sozinho ao abrir, sem passar pelo menu.</param>
    /// <param name="autoJoin">IP pra entrar sozinho ao abrir.</param>
    /// <param name="autoExit">Segundos até fechar sozinho. 0 = nunca.</param>
    /// <param name="bot">O boneco anda sozinho atrás da moeda mais perto.</param>
    /// <remarks>Os três existem pro teste de duas janelas na mesma máquina: sem eles, cada
    /// rodada de teste exige clicar nos menus das duas janelas antes de ver qualquer coisa.</remarks>
    public CoopGame(bool autoHost = false, string? autoJoin = null, float autoExit = 0f, bool bot = false)
    {
        _bot = bot;
        _autoHost = autoHost;
        _autoJoin = autoJoin;
        _autoExit = autoExit;

        // O GameName vira o identificador na busca por salas (Game preenche Net.GameId com
        // ele), então só partidas deste jogo aparecem na lista.
        GameName = "AuroraCoop";

        // Resolução de referência: o layout da UI e as contas de toque passam a ser as mesmas
        // em qualquer celular, em vez de dependerem da densidade de tela do aparelho.
        DesignResolution = new Vector2D<int>(1280, 720);
    }

    private float Largura => ScreenSize.X;
    private float Altura => ScreenSize.Y;
    private bool EmPartida => Net.IsReady;

    protected override void OnLoad()
    {
        ClearColor = Color.FromBytes(28, 34, 44);

        _fonte = Assets.LoadFont("fonts/DejaVuSans.ttf", 22f);
        _texJogador = Assets.LoadTexture("sprites/player.png");
        _texMoeda = Assets.LoadTexture("sprites/coin.png");
        _texArvore = Assets.LoadTexture("sprites/tree.png");

        EspalharArvores();

        var sync = Net.Sync!;

        // Host autoritativo: o celular manda o que o dedo está pedindo, o host calcula e
        // devolve. A previsão local é o que faz o boneco andar sem esperar a resposta.
        sync.Authority = NetAuthority.Host;
        sync.SampleInput = LerInput;
        sync.Prefabs.Register(PrefabJogador, CriarJogador, Mover);
        sync.Prefabs.Register(PrefabMoeda, CriarMoeda);

        Net.Rpc.On("Moeda", OnMoedaColetada);
        Net.Rpc.On("Fim", OnFimDeRodada);

        _lobby = new NetLobby(Net)
        {
            PlayerName = $"Jogador{_random.Next(100, 999)}",
            MaxPlayers = NetProtocol.MaxPlayersLimit,
        };

        Net.PlayerJoined += peer => Console.WriteLine($"[coop] entrou: {peer.Name} (#{peer.Id})");
        Net.PlayerLeft += peer => Console.WriteLine($"[coop] saiu: {peer.Name} (#{peer.Id})");
        Net.LeftRoom += motivo => Console.WriteLine($"[coop] desconectado: {motivo} — {_lobby.Message}");

        if (_autoHost)
        {
            _lobby.Host();
            Console.WriteLine($"[coop] hospedando em {_lobby.LocalAddress}:{_lobby.Port}");
        }
        else if (_autoJoin is { Length: > 0 })
        {
            _lobby.Address = _autoJoin;
            _lobby.JoinTyped();
            Console.WriteLine($"[coop] conectando em {_autoJoin}:{_lobby.Port}");
        }
    }

    /// <summary>Decoração. Não leva <see cref="NetworkIdentity"/> de propósito: é igual em toda
    /// máquina porque a semente é a mesma, e mandá-la pela rede seria desperdício puro.</summary>
    private void EspalharArvores()
    {
        var random = new Random(1234);

        for (int i = 0; i < 60; i++)
        {
            var arvore = World.CreateEntity($"Arvore{i}");
            arvore.Add(new Transform(
                random.Next((int)-MetadeArena, (int)MetadeArena),
                random.Next((int)-MetadeArena, (int)MetadeArena)));
            arvore.Add(new SpriteRenderer(_texArvore, layer: 2) { Color = Color.FromBytes(90, 110, 90) });
        }
    }

    private Entity CriarJogador(World world, NetworkIdentity identity)
    {
        var entidade = world.CreateEntity($"Jogador{identity.OwnerId}");
        entidade.Add(new Transform());
        entidade.Add(new SpriteRenderer(_texJogador, layer: 10)
        {
            Color = Cores[identity.OwnerId % Cores.Length],
        });

        return entidade;
    }

    private Entity CriarMoeda(World world, NetworkIdentity identity)
    {
        var entidade = world.CreateEntity("Moeda");
        entidade.Add(new Transform());
        entidade.Add(new SpriteRenderer(_texMoeda, layer: 6));

        return entidade;
    }

    /// <summary>
    /// Movimento do jogador. Roda no host pra valer e em cada celular como previsão, então só
    /// pode depender da entidade e do input — nada de ler toque, relógio ou aleatório aqui.
    /// </summary>
    private static void Mover(Entity entidade, in NetInput input)
    {
        if (entidade.Get<Transform>() is not { } transform) return;

        var direcao = new Vector2(input.AxisX, input.AxisY);
        if (direcao.LengthSquared() > 1f)
            direcao = Vector2.Normalize(direcao);

        transform.Position += direcao * Velocidade * input.DeltaTime;
        transform.Position = new Vector2(
            Math.Clamp(transform.Position.X, -MetadeArena, MetadeArena),
            Math.Clamp(transform.Position.Y, -MetadeArena, MetadeArena));
    }

    /// <summary>Analógico no celular, teclado no PC.</summary>
    private NetInputState LerInput()
    {
        if (_bot)
            return new NetInputState(DirecaoDoBot().X, DirecaoDoBot().Y, 0u);

        var direcao = _stick.Value;
        if (direcao.LengthSquared() < 0.0001f)
            direcao = new Vector2(Input.AxisX, Input.AxisY);

        return new NetInputState(direcao.X, direcao.Y, 0u);
    }

    /// <summary>Anda atrás da moeda mais perto. Existe pra provar o laço inteiro (pegar →
    /// destruir → RPC → placar nas duas máquinas) sem ninguém segurando o controle.</summary>
    private Vector2 DirecaoDoBot()
    {
        if (MeuBoneco()?.Get<Transform>() is not { } meu) return Vector2.Zero;

        float melhor = float.MaxValue;
        var alvo = Vector2.Zero;

        foreach (var (_, transform, identity) in World.Query<Transform, NetworkIdentity>())
        {
            if (identity.PrefabId != PrefabMoeda) continue;

            float distancia = Vector2.DistanceSquared(meu.Position, transform.Position);
            if (distancia >= melhor) continue;

            melhor = distancia;
            alvo = transform.Position;
        }

        if (melhor == float.MaxValue) return Vector2.Zero;

        var delta = alvo - meu.Position;
        return delta.LengthSquared() < 1f ? Vector2.Zero : Vector2.Normalize(delta);
    }

    protected override void OnUpdate(float deltaTime)
    {
        _stick.Update(Input, Largura * 0.5f);

        if (_avisoTimer > 0f) _avisoTimer -= deltaTime;
        if (_vitoriaTimer > 0f) _vitoriaTimer -= deltaTime;

        _lobby.Update(deltaTime);
        Relatar(deltaTime);

        if (_autoExit > 0f)
        {
            _autoExit -= deltaTime;
            if (_autoExit <= 0f) Exit();
        }

        if (!EmPartida)
        {
            AtualizarMenu();
            return;
        }

        if (Net.IsHost)
            AtualizarHost(deltaTime);

        SeguirCamera(deltaTime);
    }

    /// <summary>Tudo que só o host decide: quem tem boneco, quantas moedas há no mapa e quem
    /// pegou o quê.</summary>
    private void AtualizarHost(float deltaTime)
    {
        SincronizarBonecos();
        ReporMoedas();
        ChecarColeta();
    }

    /// <summary>
    /// Garante um boneco por jogador na sala, e nenhum a mais.
    /// <para>Conferir o estado todo frame, em vez de reagir aos eventos de entrada e saída,
    /// é o que faz isso se consertar sozinho — o host que entrou na partida não recebe evento
    /// de "eu entrei", e um evento perdido no meio de uma troca de cena deixaria um boneco
    /// fantasma andando pra sempre.</para>
    /// </summary>
    private void SincronizarBonecos()
    {
        foreach (var peer in Net.Peers)
        {
            if (BonecoDe(peer.Id) is not null) continue;

            var boneco = Net.Sync!.Spawn(PrefabJogador, peer.Id);
            boneco.Get<Transform>()!.Position = PosicaoAleatoria();
        }

        _paraRemover.Clear();
        foreach (var (_, _, identity) in World.Query<Transform, NetworkIdentity>())
        {
            if (identity.PrefabId != PrefabJogador) continue;
            if (ContinuaNaSala(identity.OwnerId)) continue;

            _paraRemover.Add(identity.NetId);
        }

        foreach (ushort netId in _paraRemover)
            Net.Sync!.Despawn(netId);
    }

    private bool ContinuaNaSala(byte ownerId)
    {
        foreach (var peer in Net.Peers)
        {
            if (peer.Id == ownerId) return true;
        }

        return false;
    }

    private void ReporMoedas()
    {
        int vivas = 0;
        foreach (var (_, _, identity) in World.Query<Transform, NetworkIdentity>())
        {
            if (identity.PrefabId == PrefabMoeda) vivas++;
        }

        for (int i = vivas; i < MoedasNoMapa; i++)
        {
            var moeda = Net.Sync!.Spawn(PrefabMoeda, NetProtocol.HostId);
            moeda.Get<Transform>()!.Position = PosicaoAleatoria();
        }
    }

    private void ChecarColeta()
    {
        _paraRemover.Clear();

        foreach (var (_, transformMoeda, identityMoeda) in World.Query<Transform, NetworkIdentity>())
        {
            if (identityMoeda.PrefabId != PrefabMoeda) continue;

            foreach (var (_, transformJogador, identityJogador) in World.Query<Transform, NetworkIdentity>())
            {
                if (identityJogador.PrefabId != PrefabJogador) continue;
                if (Vector2.Distance(transformMoeda.Position, transformJogador.Position) > RaioColeta) continue;

                _paraRemover.Add(identityMoeda.NetId);

                // O placar novo viaja junto do evento, em vez de cada máquina somar 1 por
                // conta própria: quem entrar no meio da partida acerta o número no primeiro
                // evento que receber, em vez de contar a partir do zero pra sempre.
                _placar++;
                Net.Rpc.Send("Moeda", identityJogador.OwnerId, _placar);
                break;
            }
        }

        // Destruir fora do laço: mexer no mundo enquanto se percorre a consulta invalidaria
        // a enumeração no meio do caminho.
        foreach (ushort netId in _paraRemover)
            Net.Sync!.Despawn(netId);

        if (_placar < MoedasPraVencer) return;

        Net.Rpc.Send("Fim", _placar);
        _placar = 0;
    }

    private void OnMoedaColetada(NetRpcArgs args)
    {
        byte jogador = (byte)args.GetInt(0);
        _placar = args.GetInt(1);

        Avisar(jogador == Net.SelfId ? "Você pegou uma moeda!" : $"Jogador {jogador} pegou uma moeda");
    }

    private void OnFimDeRodada(NetRpcArgs args)
    {
        _placar = 0;
        _vitoriaTimer = 5f;
    }

    private void Avisar(string texto)
    {
        _aviso = texto;
        _avisoTimer = 2f;
    }

    private void SeguirCamera(float deltaTime)
    {
        if (MeuBoneco()?.Get<Transform>() is not { } transform) return;

        Camera.Follow(transform.Position, 8f, deltaTime);
    }

    private Entity? MeuBoneco() => BonecoDe(Net.SelfId);

    private Entity? BonecoDe(byte ownerId)
    {
        foreach (var (entidade, _, identity) in World.Query<Transform, NetworkIdentity>())
        {
            if (identity.PrefabId == PrefabJogador && identity.OwnerId == ownerId)
                return entidade;
        }

        return null;
    }

    private Vector2 PosicaoAleatoria()
        => new(_random.Next((int)-MetadeArena, (int)MetadeArena),
               _random.Next((int)-MetadeArena, (int)MetadeArena));

    /// <summary>Uma linha por segundo no console — é o que dá pra olhar quando o teste roda
    /// em duas janelas e ninguém está clicando em nada.</summary>
    private void Relatar(float deltaTime)
    {
        _relatorio -= deltaTime;
        if (_relatorio > 0f) return;

        _relatorio = 1f;

        if (!EmPartida)
        {
            Console.WriteLine($"[coop] {_lobby.State} — salas: {_lobby.Rooms.Count}");
            return;
        }

        string papel = Net.IsHost ? "host" : "cliente";
        var posicao = MeuBoneco()?.Get<Transform>()?.Position ?? Vector2.Zero;

        Console.WriteLine($"[coop] {papel} #{Net.SelfId} — jogadores {Net.PlayerCount}, " +
            $"moedas {_placar}/{MoedasPraVencer}, entidades {Net.Sync!.SyncedCount}, " +
            $"pos ({posicao.X:F0},{posicao.Y:F0}), input pendente {Net.Sync.PendingInputCount}");
    }

    // ---------------------------------------------------------------- menu

    private void AtualizarMenu()
    {
        // Nada aqui: os botões do menu são resolvidos no desenho, onde as posições já existem.
    }

    protected override void OnRenderUI(float deltaTime)
    {
        if (EmPartida)
        {
            DesenharHud();
            _stick.Draw(SpriteBatch);
            return;
        }

        DesenharMenu();
    }

    private void DesenharMenu()
    {
        SpriteBatch.DrawRect(Vector2.Zero, new Vector2(Largura, Altura), new Color(0f, 0f, 0f, 0.55f));
        Titulo("AURORA COOP", 60f);
        Texto($"Você: {_lobby.PlayerName}", new Vector2(40f, 120f), Color.FromBytes(180, 190, 210));

        switch (_lobby.State)
        {
            case NetLobbyState.Idle:
            case NetLobbyState.Failed:
                DesenharMenuInicial();
                break;

            case NetLobbyState.Browsing:
                DesenharListaDeSalas();
                break;

            case NetLobbyState.Connecting:
                Texto("Conectando...", new Vector2(40f, 200f), Color.White);
                if (Botao(new Vector2(40f, 260f), new Vector2(200f, 56f), "CANCELAR"))
                    _lobby.Cancel();
                break;
        }

        if (_lobby.Message.Length > 0)
            Texto(_lobby.Message, new Vector2(40f, Altura - 60f), Color.FromBytes(255, 140, 140));
    }

    private void DesenharMenuInicial()
    {
        if (Botao(new Vector2(40f, 200f), new Vector2(340f, 72f), "HOSPEDAR PARTIDA"))
            _lobby.Host();

        if (Botao(new Vector2(40f, 292f), new Vector2(340f, 72f), "PROCURAR PARTIDAS"))
            _lobby.Browse();

        Texto("Todos precisam estar no mesmo Wi-Fi.", new Vector2(40f, 392f),
            Color.FromBytes(150, 160, 180));
    }

    private void DesenharListaDeSalas()
    {
        Texto("Partidas na rede:", new Vector2(40f, 180f), Color.White);

        if (_lobby.Rooms.Count == 0)
            Texto("procurando...", new Vector2(40f, 220f), Color.FromBytes(150, 160, 180));

        for (int i = 0; i < _lobby.Rooms.Count && i < 6; i++)
        {
            var sala = _lobby.Rooms[i];
            string rotulo = $"{sala.RoomName}  {sala.PlayerCount}/{sala.MaxPlayers}"
                + (sala.IsFull ? "  (cheia)" : "");

            if (!Botao(new Vector2(40f, 220f + i * 64f), new Vector2(520f, 56f), rotulo)) continue;

            _lobby.Select(i);
            _lobby.JoinSelected();
        }

        if (Botao(new Vector2(600f, 180f), new Vector2(200f, 56f), "ATUALIZAR"))
            Net.Browser?.Refresh();

        if (Botao(new Vector2(600f, 252f), new Vector2(200f, 56f), "VOLTAR"))
            _lobby.Cancel();
    }

    private void DesenharHud()
    {
        Texto($"Moedas do time: {_placar} / {MoedasPraVencer}", new Vector2(20f, 16f),
            Color.FromBytes(251, 242, 54));

        float y = 56f;
        foreach (var peer in Net.Peers)
        {
            string marca = peer.Id == Net.SelfId ? " (você)" : "";
            Texto($"■ {peer.Name}{marca}", new Vector2(20f, y), Cores[peer.Id % Cores.Length]);
            y += 28f;
        }

        // Só quem hospeda mostra o endereço: é o número que o amigo digita se a busca na rede
        // não achar a partida (Wi-Fi de empresa costuma bloquear broadcast).
        if (Net.IsHost)
        {
            Texto($"IP: {_lobby.LocalAddress}:{_lobby.Port}", new Vector2(20f, Altura - 40f),
                Color.FromBytes(150, 200, 255));
        }

        if (Botao(new Vector2(Largura - 140f, 16f), new Vector2(120f, 48f), "SAIR"))
        {
            _lobby.Cancel();
            _placar = 0;
        }

        if (_avisoTimer > 0f)
            Centralizado(_aviso, Altura - 120f, Color.White);

        if (_vitoriaTimer > 0f)
        {
            SpriteBatch.DrawRect(Vector2.Zero, new Vector2(Largura, Altura), new Color(0f, 0f, 0f, 0.6f));
            Centralizado("MISSÃO CUMPRIDA!", Altura / 2f - 40f, Color.FromBytes(251, 242, 54));
            Centralizado("Nova rodada começando...", Altura / 2f + 10f, Color.White);
        }
    }

    // ------------------------------------------------------------ desenho

    /// <summary>
    /// Botão de menu resolvido na hora de desenhar. Sem tela de UI em JSON de propósito: o
    /// menu muda de conteúdo a cada estado (a lista de salas aparece e some sozinha), e manter
    /// isso em arquivo daria mais trabalho que as poucas linhas daqui.
    /// </summary>
    private bool Botao(Vector2 posicao, Vector2 tamanho, string texto)
    {
        var ponteiro = Input.MousePosition;
        bool dentro = ponteiro.X >= posicao.X && ponteiro.X <= posicao.X + tamanho.X
                   && ponteiro.Y >= posicao.Y && ponteiro.Y <= posicao.Y + tamanho.Y;

        SpriteBatch.DrawRect(posicao, tamanho,
            dentro ? Color.FromBytes(70, 80, 130) : Color.FromBytes(45, 52, 80));

        var medida = _fonte.MeasureText(texto);
        _fonte.Draw(SpriteBatch, texto, posicao + (tamanho - medida) / 2f, Color.White);

        // WasMouseClicked cobre mouse e toque: a Activity injeta o dedo como ponteiro único
        // além do multi-toque, então menu e analógico funcionam com o mesmo código.
        return dentro && Input.WasMouseClicked();
    }

    private void Titulo(string texto, float y)
    {
        var medida = _fonte.MeasureText(texto, 1.6f);
        _fonte.Draw(SpriteBatch, texto, new Vector2(40f, y), Color.FromBytes(251, 242, 54), 1.6f);
        _ = medida;
    }

    private void Texto(string texto, Vector2 posicao, Color cor)
        => _fonte.Draw(SpriteBatch, texto, posicao, cor);

    private void Centralizado(string texto, float y, Color cor)
    {
        var medida = _fonte.MeasureText(texto);
        _fonte.Draw(SpriteBatch, texto, new Vector2((Largura - medida.X) / 2f, y), cor);
    }
}
