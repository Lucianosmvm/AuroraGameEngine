using System.Net;

namespace Aurora.Runtime.Net;

/// <summary>
/// Envio e recepção de datagramas. Existe como interface pra separar o protocolo (handshake,
/// timeout, lista de peers) do socket: os testes rodam host e clientes reais no mesmo processo
/// com <see cref="LoopbackNetwork"/>, sem abrir porta, sem thread e sem depender do firewall
/// da máquina que roda a suíte.
/// </summary>
public interface INetTransport : IDisposable
{
    /// <summary>Endereço local onde este transporte escuta.</summary>
    IPEndPoint LocalEndPoint { get; }

    /// <summary>Envia sem garantia nenhuma — UDP. Perda e reordenação são normais e tratadas
    /// nas camadas de cima (reenvio de Join, keepalive).</summary>
    void Send(ReadOnlySpan<byte> data, IPEndPoint to);

    /// <summary>
    /// Tira um datagrama da fila, se houver. Nunca bloqueia: o loop do jogo chama isso
    /// dentro do frame e não pode parar esperando rede.
    /// </summary>
    bool TryReceive(Span<byte> buffer, out int length, out IPEndPoint from);
}
