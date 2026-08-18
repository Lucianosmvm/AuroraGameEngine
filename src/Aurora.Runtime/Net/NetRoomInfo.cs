using System.Net;

namespace Aurora.Runtime.Net;

/// <summary>Uma partida encontrada na rede local.</summary>
public sealed class NetRoomInfo
{
    internal NetRoomInfo(uint roomId, IPEndPoint address, string roomName, string hostName,
        byte playerCount, byte maxPlayers)
    {
        RoomId = roomId;
        Address = address;
        RoomName = roomName;
        HostName = hostName;
        PlayerCount = playerCount;
        MaxPlayers = maxPlayers;
    }

    /// <summary>Identifica a sala independente do caminho de rede por onde a resposta veio.
    /// Ver <see cref="NetHost.RoomId"/>.</summary>
    public uint RoomId { get; }

    /// <summary>IP e porta pra entrar. Vem de onde a resposta chegou, não do que o pacote diz —
    /// um host atrás de NAT não sabe o próprio endereço visível, e quem sabe é quem recebeu.</summary>
    public IPEndPoint Address { get; }

    /// <summary>Nome da sala, definido por quem hospeda.</summary>
    public string RoomName { get; internal set; }

    /// <summary>Nome do jogador que hospeda.</summary>
    public string HostName { get; internal set; }

    public byte PlayerCount { get; internal set; }

    public byte MaxPlayers { get; internal set; }

    public bool IsFull => PlayerCount >= MaxPlayers;

    /// <summary>Segundos desde a última resposta desta sala. Passando de
    /// <see cref="NetBrowser.RoomTimeout"/>, ela some da lista — o host fechou o jogo, ou
    /// alguém desligou o Wi-Fi.</summary>
    internal float SilentFor;

    public override string ToString() => $"{RoomName} ({PlayerCount}/{MaxPlayers}) — {Address}";
}
