using System.Net;

namespace Aurora.Runtime.Net;

/// <summary>Por que um jogador saiu da sala.</summary>
public enum NetDisconnectReason
{
    /// <summary>Saiu de propósito (fechou o jogo, botão de sair).</summary>
    Requested = 0,

    /// <summary>Parou de responder. Queda de Wi-Fi, travamento, cabo arrancado.</summary>
    TimedOut = 1,

    /// <summary>O host encerrou a partida.</summary>
    HostShutdown = 2,

    /// <summary>Não foi possível conectar dentro do tempo limite.</summary>
    ConnectFailed = 3,

    /// <summary>O host recusou o join — ver <see cref="NetRejectReason"/>.</summary>
    Rejected = 4,
}

/// <summary>Um jogador na sala, incluindo o próprio host (id 0).</summary>
public sealed class NetPeer
{
    internal NetPeer(byte id, string name, IPEndPoint address)
    {
        Id = id;
        Name = name;
        Address = address;
    }

    /// <summary>Identificador na sala. 0 é sempre o host; clientes recebem 1..MaxPlayers-1.
    /// Um id liberado por quem saiu volta a ser distribuído.</summary>
    public byte Id { get; }

    public string Name { get; }

    /// <summary>De onde os pacotes dele chegam. É a chave que identifica o peer no host —
    /// não o id, que só existe depois do join aceito.
    /// <para>No cliente, só o peer do host tem endereço de verdade: ninguém fala direto com
    /// os outros jogadores, tudo passa pelo host. Os demais recebem
    /// <see cref="UnknownAddress"/>.</para></summary>
    public IPEndPoint Address { get; }

    /// <summary>Marcador de "endereço não sabido" pros peers que o cliente conhece só de nome.
    /// Enviar pra ele não faz nada de útil — é justamente o ponto: nenhum caminho de código
    /// deve tentar falar direto com outro cliente.</summary>
    public static readonly IPEndPoint UnknownAddress = new(IPAddress.None, 0);

    public bool IsHost => Id == NetProtocol.HostId;

    /// <summary>Segundos desde o último pacote recebido deste peer. Zera a cada pacote válido
    /// e é o que dispara o timeout.</summary>
    internal float SilentFor;

    public override string ToString() => $"#{Id} {Name} ({Address})";
}
