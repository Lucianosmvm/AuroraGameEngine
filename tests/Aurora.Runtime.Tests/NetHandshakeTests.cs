using System.Net;
using Aurora.Runtime.Net;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Handshake, presença e queda — a fase 1 do multiplayer LAN. Tudo roda sobre
/// <see cref="LoopbackNetwork"/>: sem socket, sem thread, sem espera real, então os testes
/// não dependem do firewall nem do relógio da máquina que roda a suíte.
/// </summary>
public class NetHandshakeTests
{
    private const int HostPort = 7777;

    /// <summary>Roda alguns frames de rede. dt 0 por padrão: só bombeia pacote, sem deixar
    /// nenhum temporizador de timeout avançar.</summary>
    private static void Pump(NetHost host, IEnumerable<NetClient> clients, float dt = 0f, int steps = 3)
    {
        for (int i = 0; i < steps; i++)
        {
            host.Update(dt);
            foreach (var client in clients)
                client.Update(dt);
        }
    }

    private static void Pump(NetHost host, NetClient client, float dt = 0f, int steps = 3)
        => Pump(host, [client], dt, steps);

    private static NetHost CreateHost(LoopbackNetwork net, int maxPlayers = NetProtocol.MaxPlayersLimit)
        => new(net.CreateTransport(HostPort), "Host", maxPlayers);

    private static NetClient CreateClient(LoopbackNetwork net, string name)
    {
        var client = new NetClient(net.CreateTransport());
        client.Connect(new IPEndPoint(IPAddress.Loopback, HostPort), name);
        return client;
    }

    [Fact]
    public void HostSozinhoJaContaComoUmJogador()
    {
        var net = new LoopbackNetwork();
        using var host = CreateHost(net);

        Assert.Equal(1, host.PlayerCount);
        Assert.Equal(NetProtocol.HostId, host.Self.Id);
        Assert.True(host.Self.IsHost);
        Assert.False(host.IsFull);
    }

    [Fact]
    public void ClienteEntraERecebeIdUm()
    {
        var net = new LoopbackNetwork();
        using var host = CreateHost(net);
        using var client = CreateClient(net, "Ana");

        Pump(host, client);

        Assert.Equal(NetClientState.Connected, client.State);
        Assert.Equal(1, client.SelfId);
        Assert.Equal(2, host.PlayerCount);
    }

    [Fact]
    public void HostAvisaQuemEntrouComNome()
    {
        var net = new LoopbackNetwork();
        using var host = CreateHost(net);

        NetPeer? entrou = null;
        host.PeerJoined += p => entrou = p;

        using var client = CreateClient(net, "Ana");
        Pump(host, client);

        Assert.NotNull(entrou);
        Assert.Equal("Ana", entrou!.Name);
        Assert.Equal(1, entrou.Id);
    }

    [Fact]
    public void ClienteRecebeASalaInteiraNoAceite()
    {
        var net = new LoopbackNetwork();
        using var host = CreateHost(net);
        using var ana = CreateClient(net, "Ana");
        Pump(host, ana);

        using var bruno = CreateClient(net, "Bruno");
        Pump(host, [ana, bruno]);

        // O Bruno chegou depois e nunca viu um PeerJoined da Ana: a lista dele tem que vir
        // completa do próprio aceite, senão só apareceria quem entrasse depois dele.
        Assert.Equal(3, bruno.Peers.Count);
        Assert.Contains(bruno.Peers, p => p.Name == "Host" && p.Id == NetProtocol.HostId);
        Assert.Contains(bruno.Peers, p => p.Name == "Ana");
        Assert.Contains(bruno.Peers, p => p.Name == "Bruno");
    }

    [Fact]
    public void ClienteJaDentroVeOsQueEntramDepois()
    {
        var net = new LoopbackNetwork();
        using var host = CreateHost(net);
        using var ana = CreateClient(net, "Ana");
        Pump(host, ana);

        NetPeer? visto = null;
        ana.PeerJoined += p => visto = p;

        using var bruno = CreateClient(net, "Bruno");
        Pump(host, [ana, bruno]);

        Assert.NotNull(visto);
        Assert.Equal("Bruno", visto!.Name);
        Assert.Equal(3, ana.Peers.Count);
    }

    [Fact]
    public void OitoJogadoresCabemENonoERecusado()
    {
        var net = new LoopbackNetwork();
        using var host = CreateHost(net);

        var clients = new List<NetClient>();
        try
        {
            // Host + 7 = 8 vagas ocupadas.
            for (int i = 1; i < NetProtocol.MaxPlayersLimit; i++)
            {
                clients.Add(CreateClient(net, $"Jogador{i}"));
                Pump(host, clients);
            }

            Assert.Equal(NetProtocol.MaxPlayersLimit, host.PlayerCount);
            Assert.True(host.IsFull);
            Assert.All(clients, c => Assert.Equal(NetClientState.Connected, c.State));

            using var sobrando = CreateClient(net, "Atrasado");
            clients.Add(sobrando);
            Pump(host, clients);

            Assert.Equal(NetClientState.Disconnected, sobrando.State);
            Assert.Equal(NetRejectReason.Full, sobrando.LastRejectReason);
            Assert.Equal(NetProtocol.MaxPlayersLimit, host.PlayerCount);
        }
        finally
        {
            foreach (var client in clients)
                client.Dispose();
        }
    }

    [Fact]
    public void LimiteDeVagasMenorERespeitado()
    {
        var net = new LoopbackNetwork();
        using var host = CreateHost(net, maxPlayers: 2);
        using var ana = CreateClient(net, "Ana");
        Pump(host, ana);

        using var bruno = CreateClient(net, "Bruno");
        Pump(host, [ana, bruno]);

        Assert.Equal(NetClientState.Connected, ana.State);
        Assert.Equal(NetClientState.Disconnected, bruno.State);
        Assert.Equal(NetRejectReason.Full, bruno.LastRejectReason);
    }

    [Fact]
    public void SairAvisandoRemoveDaSalaNaHora()
    {
        var net = new LoopbackNetwork();
        using var host = CreateHost(net);
        using var ana = CreateClient(net, "Ana");
        using var bruno = CreateClient(net, "Bruno");
        Pump(host, [ana, bruno]);

        NetPeer? saiu = null;
        NetDisconnectReason motivo = default;
        host.PeerLeft += (p, r) => { saiu = p; motivo = r; };

        NetPeer? vistoPorBruno = null;
        bruno.PeerLeft += p => vistoPorBruno = p;

        ana.Disconnect();
        Pump(host, bruno);

        Assert.NotNull(saiu);
        Assert.Equal("Ana", saiu!.Name);
        Assert.Equal(NetDisconnectReason.Requested, motivo);
        Assert.Equal(2, host.PlayerCount);

        Assert.NotNull(vistoPorBruno);
        Assert.Equal("Ana", vistoPorBruno!.Name);
        Assert.Equal(2, bruno.Peers.Count);
    }

    [Fact]
    public void SilencioProlongadoDerrubaOJogador()
    {
        var net = new LoopbackNetwork();
        using var host = CreateHost(net);
        using var ana = CreateClient(net, "Ana");
        Pump(host, ana);

        NetDisconnectReason motivo = default;
        host.PeerLeft += (_, r) => motivo = r;

        // Cliente para de existir do ponto de vista da rede (travou, Wi-Fi caiu): só o host
        // roda daqui pra frente.
        for (int i = 0; i < 6; i++)
            host.Update(1f);

        Assert.Equal(NetDisconnectReason.TimedOut, motivo);
        Assert.Equal(1, host.PlayerCount);
    }

    [Fact]
    public void KeepAliveSeguraOJogadorParado()
    {
        var net = new LoopbackNetwork();
        using var host = CreateHost(net);
        using var ana = CreateClient(net, "Ana");
        Pump(host, ana);

        // 10 segundos sem o jogador apertar nada — o dobro do timeout do host. Só o
        // keepalive mantém ele na sala.
        Pump(host, ana, dt: 0.5f, steps: 20);

        Assert.Equal(2, host.PlayerCount);
        Assert.Equal(NetClientState.Connected, ana.State);
    }

    [Fact]
    public void IdDeQuemSaiuEReaproveitado()
    {
        var net = new LoopbackNetwork();
        using var host = CreateHost(net);
        using var ana = CreateClient(net, "Ana");
        using var bruno = CreateClient(net, "Bruno");
        Pump(host, [ana, bruno]);

        Assert.Equal(1, ana.SelfId);
        Assert.Equal(2, bruno.SelfId);

        ana.Disconnect();
        Pump(host, bruno);

        using var carla = CreateClient(net, "Carla");
        Pump(host, [bruno, carla]);

        Assert.Equal(1, carla.SelfId);
    }

    [Fact]
    public void JoinReenviadoNaoDuplicaOJogador()
    {
        var net = new LoopbackNetwork();
        using var host = CreateHost(net);

        // 100% de perda enquanto o cliente tenta entrar: o JoinAccepted nunca chega e o
        // cliente reenvia o Join várias vezes.
        using var ana = CreateClient(net, "Ana");
        Pump(host, ana);

        net.PacketLoss = 1f;
        Pump(host, ana, dt: 0.3f, steps: 6);
        net.PacketLoss = 0f;

        Pump(host, ana, dt: 0.3f);

        Assert.Equal(2, host.PlayerCount);
        Assert.Equal(NetClientState.Connected, ana.State);
        Assert.Equal(1, ana.SelfId);
    }

    [Fact]
    public void ClienteDesisteQuandoNinguemResponde()
    {
        var net = new LoopbackNetwork();
        using var host = CreateHost(net);

        net.PacketLoss = 1f;
        using var ana = CreateClient(net, "Ana");

        NetDisconnectReason motivo = default;
        ana.Disconnected += r => motivo = r;

        Pump(host, ana, dt: 0.5f, steps: 12);

        Assert.Equal(NetClientState.Disconnected, ana.State);
        Assert.Equal(NetDisconnectReason.ConnectFailed, motivo);
    }

    [Fact]
    public void ShutdownDoHostDerrubaOsClientes()
    {
        var net = new LoopbackNetwork();
        var host = CreateHost(net);
        using var ana = CreateClient(net, "Ana");
        Pump(host, ana);

        NetDisconnectReason motivo = default;
        ana.Disconnected += r => motivo = r;

        host.Shutdown();
        ana.Update(0f);

        Assert.Equal(NetClientState.Disconnected, ana.State);
        Assert.Equal(NetDisconnectReason.HostShutdown, motivo);

        host.Dispose();
    }

    [Fact]
    public void LixoNaPortaNaoCriaJogadorNemDerrubaOHost()
    {
        var net = new LoopbackNetwork();
        using var host = CreateHost(net);
        using var intruso = net.CreateTransport();

        var alvo = new IPEndPoint(IPAddress.Loopback, HostPort);
        intruso.Send([0xDE, 0xAD, 0xBE, 0xEF], alvo);
        intruso.Send([NetProtocol.Magic0, NetProtocol.Magic1], alvo);
        intruso.Send([NetProtocol.Magic0, NetProtocol.Magic1, NetProtocol.Version, 99], alvo);

        host.Update(0f);

        Assert.Equal(1, host.PlayerCount);
    }

    [Fact]
    public void BuildDeOutraVersaoERecusadoComMotivoClaro()
    {
        var net = new LoopbackNetwork();
        using var host = CreateHost(net);
        using var intruso = net.CreateTransport();

        NetRejectReason motivo = default;
        host.JoinRejected += (_, r) => motivo = r;

        intruso.Send(
            [NetProtocol.Magic0, NetProtocol.Magic1, (byte)(NetProtocol.Version + 1), (byte)NetMessageType.Join],
            new IPEndPoint(IPAddress.Loopback, HostPort));

        host.Update(0f);

        Assert.Equal(NetRejectReason.VersionMismatch, motivo);
        Assert.Equal(1, host.PlayerCount);
    }

    [Fact]
    public void ClienteIgnoraPacoteDeQuemNaoEOHost()
    {
        var net = new LoopbackNetwork();
        using var host = CreateHost(net);
        using var ana = CreateClient(net, "Ana");
        using var bruno = CreateClient(net, "Bruno");
        Pump(host, [ana, bruno]);

        Assert.Equal(3, ana.Peers.Count);

        // Terceiro forja um "o Bruno saiu" direto pro cliente da Ana. Só o host tem autoridade
        // sobre a lista de jogadores; qualquer outra origem tem que ser descartada.
        Span<byte> buffer = stackalloc byte[NetProtocol.MaxPacketSize];
        var writer = new NetWriter(buffer, NetMessageType.PeerLeft);
        writer.WriteByte(bruno.SelfId);

        using var intruso = net.CreateTransport();
        intruso.Send(writer.Written, new IPEndPoint(IPAddress.Loopback, ana.LocalPort));

        ana.Update(0f);

        Assert.Equal(3, ana.Peers.Count);
    }

    [Fact]
    public void NomeVazioViraFallback()
    {
        var net = new LoopbackNetwork();
        using var host = CreateHost(net);
        using var anonimo = CreateClient(net, "   ");
        Pump(host, anonimo);

        Assert.Equal("Jogador", host.Peers[1].Name);
    }

    [Fact]
    public void SessaoOfflineNaoAbreNadaENaoTemJogadores()
    {
        using var session = new NetSession();

        Assert.True(session.IsOffline);
        Assert.False(session.IsReady);
        Assert.Empty(session.Peers);

        session.Update(0.016f);
    }

    [Fact]
    public void SessaoRepassaOsEventosDosDoisLados()
    {
        var net = new LoopbackNetwork();

        using var hostSession = new NetSession();
        var host = hostSession.StartHost(CreateHost(net));

        NetPeer? entrouNoHost = null;
        hostSession.PlayerJoined += p => entrouNoHost = p;

        using var clientSession = new NetSession();
        var client = clientSession.Join(new NetClient(net.CreateTransport()));

        byte idRecebido = 255;
        clientSession.JoinedRoom += id => idRecebido = id;

        client.Connect(new IPEndPoint(IPAddress.Loopback, HostPort), "Ana");

        for (int i = 0; i < 3; i++)
        {
            hostSession.Update(0f);
            clientSession.Update(0f);
        }

        Assert.True(hostSession.IsHost);
        Assert.Equal(NetProtocol.HostId, hostSession.SelfId);
        Assert.Equal(2, hostSession.PlayerCount);
        Assert.NotNull(entrouNoHost);
        Assert.Equal("Ana", entrouNoHost!.Name);

        Assert.True(clientSession.IsReady);
        Assert.Equal(1, idRecebido);
        Assert.Equal(1, clientSession.SelfId);
    }
}
