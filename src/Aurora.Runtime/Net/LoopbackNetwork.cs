using System.Net;

namespace Aurora.Runtime.Net;

/// <summary>
/// Rede falsa em memória: entrega os pacotes dentro do próprio processo, sem socket.
/// Serve pros testes (host e 8 clientes num teste só, sem porta, sem firewall, sem espera)
/// e pra depurar multiplayer rodando tudo num executável só.
/// <para>Entrega é síncrona e em ordem, então nenhum teste depende de tempo real. Pra exercitar
/// o caminho de perda de pacote, use <see cref="PacketLoss"/>.</para>
/// </summary>
public sealed class LoopbackNetwork
{
    private readonly Dictionary<int, Endpoint> _endpoints = [];
    private readonly Random _random;
    private int _nextPort = 40000;

    /// <param name="seed">Semente do sorteio de perda — fixa por padrão pra teste com
    /// <see cref="PacketLoss"/> dar sempre o mesmo resultado.</param>
    public LoopbackNetwork(int seed = 1234)
    {
        _random = new Random(seed);
    }

    /// <summary>Fração de pacotes descartados no envio, de 0 a 1. Padrão 0 (rede perfeita).</summary>
    public float PacketLoss { get; set; }

    /// <summary>Total de pacotes entregues desde o começo — atalho pra assertar tráfego em teste.</summary>
    public int DeliveredPackets { get; private set; }

    /// <summary>Cria um transporte nesta rede. <paramref name="port"/> 0 recebe uma porta livre,
    /// igual ao socket de verdade.</summary>
    public INetTransport CreateTransport(int port = 0)
    {
        if (port == 0)
            port = _nextPort++;

        if (_endpoints.ContainsKey(port))
            throw new InvalidOperationException($"Porta {port} já está em uso nesta LoopbackNetwork.");

        var endpoint = new Endpoint(this, new IPEndPoint(IPAddress.Loopback, port));
        _endpoints[port] = endpoint;
        return endpoint;
    }

    private void Deliver(ReadOnlySpan<byte> data, IPEndPoint to, IPEndPoint from)
    {
        if (PacketLoss > 0f && _random.NextDouble() < PacketLoss) return;
        if (!_endpoints.TryGetValue(to.Port, out var target)) return;

        target.Enqueue(data.ToArray(), from);
        DeliveredPackets++;
    }

    private void Remove(int port) => _endpoints.Remove(port);

    private sealed class Endpoint(LoopbackNetwork network, IPEndPoint address) : INetTransport
    {
        private readonly Queue<(byte[] Data, IPEndPoint From)> _inbox = new();
        private bool _disposed;

        public IPEndPoint LocalEndPoint { get; } = address;

        public void Enqueue(byte[] packet, IPEndPoint from) => _inbox.Enqueue((packet, from));

        public void Send(ReadOnlySpan<byte> data, IPEndPoint to)
        {
            if (_disposed) return;
            network.Deliver(data, to, LocalEndPoint);
        }

        public bool TryReceive(Span<byte> buffer, out int length, out IPEndPoint from)
        {
            length = 0;
            from = LocalEndPoint;

            if (_disposed || _inbox.Count == 0) return false;

            var (packet, sender) = _inbox.Dequeue();

            // Truncar em vez de crescer o buffer imita o socket real: datagrama maior que o
            // buffer chega cortado, e é justamente esse caso que o NetReader precisa recusar.
            length = Math.Min(packet.Length, buffer.Length);
            packet.AsSpan(0, length).CopyTo(buffer);
            from = sender;
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _inbox.Clear();
            network.Remove(LocalEndPoint.Port);
        }
    }
}
