using System.Net;
using System.Net.Sockets;

namespace Aurora.Runtime.Net;

/// <summary>Transporte UDP de verdade: LAN, Wi-Fi, cabo.</summary>
public sealed class UdpNetTransport : INetTransport
{
    /// <summary>SIO_UDP_CONNRESET. Ver o motivo em <see cref="UdpNetTransport(int)"/>.</summary>
    private const int SioUdpConnReset = -1744830452;

    private readonly Socket _socket;
    private EndPoint _receiveFrom = new IPEndPoint(IPAddress.Any, 0);
    private bool _disposed;

    /// <summary>
    /// Abre o socket. <paramref name="port"/> 0 pede uma porta livre ao SO — é o que o cliente
    /// usa (só o host precisa de porta previsível, porque é ela que o outro jogador digita).
    /// </summary>
    public UdpNetTransport(int port)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            Blocking = false,
            EnableBroadcast = true,
        };

        // No Windows, se um pacote nosso chega numa porta sem ninguém escutando, o outro lado
        // responde ICMP Port Unreachable e o socket passa a lançar ConnectionReset no PRÓXIMO
        // ReceiveFrom — mesmo que esse Receive não tenha relação com aquele envio. Em UDP isso
        // não faz sentido (não há conexão pra resetar) e derruba o host inteiro quando um único
        // cliente fecha o jogo. Desligar é o procedimento padrão pra socket UDP no Windows.
        if (OperatingSystem.IsWindows())
            _socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);

        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        LocalEndPoint = (IPEndPoint)_socket.LocalEndPoint!;
    }

    public IPEndPoint LocalEndPoint { get; }

    /// <summary>Porta em que este socket escuta.</summary>
    public int Port => LocalEndPoint.Port;

    public void Send(ReadOnlySpan<byte> data, IPEndPoint to)
    {
        if (_disposed) return;

        try
        {
            _socket.SendTo(data, SocketFlags.None, to);
        }
        catch (SocketException)
        {
            // Destino sumiu da rede, buffer do SO cheio, cabo arrancado. Nada a fazer por
            // pacote: quem depende de entrega reenvia, e quem sumiu de vez cai no timeout.
        }
    }

    public bool TryReceive(Span<byte> buffer, out int length, out IPEndPoint from)
    {
        length = 0;
        from = LocalEndPoint;

        if (_disposed || _socket.Available == 0) return false;

        try
        {
            length = _socket.ReceiveFrom(buffer, SocketFlags.None, ref _receiveFrom);
            from = (IPEndPoint)_receiveFrom;
            return true;
        }
        catch (SocketException)
        {
            length = 0;
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _socket.Dispose();
    }

    /// <summary>
    /// Melhor palpite do IP desta máquina na LAN — é o número que o host mostra na tela pros
    /// outros digitarem. Abre um socket UDP "conectado" a um IP externo: nada é enviado
    /// (UDP não faz handshake), mas o SO escolhe a interface de saída e revela qual endereço
    /// local usaria. Isso acerta a placa certa quando há várias (Wi-Fi + Ethernet + VPN +
    /// adaptador do Docker/WSL), coisa que varrer a lista de interfaces erra com frequência.
    /// </summary>
    public static string GetLocalAddress()
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect("8.8.8.8", 65530);
            return ((IPEndPoint)probe.LocalEndPoint!).Address.ToString();
        }
        catch (SocketException)
        {
            return IPAddress.Loopback.ToString();
        }
    }
}
