namespace Aurora.Runtime.Net;

/// <summary>
/// Eventos nomeados entre as máquinas — som, dano, porta abrindo, fim de partida. Tudo que é
/// "aconteceu isso agora" em vez de "esta é a posição atual".
///
/// <para>Vai pelo canal confiável: snapshot perdido se conserta sozinho no próximo, mas um
/// "levou 30 de dano" perdido nunca volta, e as duas máquinas passam o resto da partida
/// contando histórias diferentes.</para>
///
/// <para>Offline funciona: a chamada é entregue localmente e nada vai pro fio. Isso deixa o
/// mesmo código rodar em jogo single player sem nenhum <c>if</c>.</para>
/// </summary>
public sealed class NetRpcSystem
{
    private readonly record struct Registration(string Name, NetRpcHandler Handler);

    private readonly NetSession _session;
    private readonly Dictionary<uint, Registration> _handlers = [];
    private readonly byte[] _sendBuffer = new byte[NetProtocol.MaxPacketSize];

    private NetHost? _host;
    private NetClient? _client;

    internal NetRpcSystem(NetSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Permite que um cliente peça um RPC pra sala inteira (alvos <see cref="NetRpcTarget.All"/>
    /// e <see cref="NetRpcTarget.Others"/>). Ligado por padrão, que é o certo pra jogo
    /// cooperativo. Desligue e só o host consegue falar com todo mundo — clientes ficam
    /// limitados a <see cref="NetRpcTarget.Host"/>, e é o host que decide o que retransmitir.
    /// <para>Mesmo ligado, <c>args.SenderId</c> no host é o id verdadeiro do remetente, então
    /// dá pra validar caso a caso em vez de proibir tudo.</para>
    /// </summary>
    public bool AllowClientBroadcast { get; set; } = true;

    /// <summary>Registra o que fazer quando este RPC chegar. Registrar de novo o mesmo nome
    /// substitui o handler anterior.</summary>
    public void On(string name, NetRpcHandler handler)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("RPC precisa de nome.", nameof(name));

        uint hash = Hash(name);

        // O nome viaja como número de 4 bytes. Duas chamadas diferentes com o mesmo número
        // seriam confundidas em silêncio — improvável, mas o custo de checar é zero e o custo
        // de descobrir isso em produção não é.
        if (_handlers.TryGetValue(hash, out var existing) && existing.Name != name)
            throw new InvalidOperationException($"Colisão de nome de RPC entre \"{name}\" e \"{existing.Name}\" — renomeie um dos dois.");

        _handlers[hash] = new Registration(name, handler);
    }

    /// <summary>Tira o handler. Devolve false se não havia nada registrado com esse nome.</summary>
    public bool Off(string name) => _handlers.Remove(Hash(name));

    /// <summary>Dispara pra sala inteira, incluindo esta máquina.</summary>
    public void Send(string name, params object?[] args) => Send(NetRpcTarget.All, name, args);

    /// <summary>Dispara pro alvo escolhido.</summary>
    public void Send(NetRpcTarget target, string name, params object?[] args)
        => Dispatch(target, targetPlayer: 0, name, ToValues(args));

    /// <summary>Dispara pra um jogador específico.</summary>
    public void SendToPlayer(byte playerId, string name, params object?[] args)
        => Dispatch(NetRpcTarget.Player, playerId, name, ToValues(args));

    private void Dispatch(NetRpcTarget target, byte targetPlayer, string name, NetRpcValue[] values)
    {
        byte self = _session.SelfId;

        // Offline: só entrega local. Mesmo código, jogo de um jogador só.
        if (!_session.IsReady)
        {
            if (target is NetRpcTarget.All or NetRpcTarget.Host
                || (target == NetRpcTarget.Player && targetPlayer == self))
            {
                Invoke(name, self, values);
            }

            return;
        }

        if (_session.IsHost)
        {
            DispatchFromHost(target, targetPlayer, name, values, self);
            return;
        }

        DispatchFromClient(target, targetPlayer, name, values, self);
    }

    private void DispatchFromHost(NetRpcTarget target, byte targetPlayer, string name, NetRpcValue[] values, byte self)
    {
        if (_host is null) return;

        switch (target)
        {
            case NetRpcTarget.All:
                Invoke(name, self, values);
                BroadcastReliable(target, targetPlayer, name, values, self, except: null);
                break;

            case NetRpcTarget.Others:
                BroadcastReliable(target, targetPlayer, name, values, self, except: null);
                break;

            case NetRpcTarget.Host:
                Invoke(name, self, values);
                break;

            case NetRpcTarget.Player:
                if (targetPlayer == NetProtocol.HostId)
                {
                    Invoke(name, self, values);
                    break;
                }

                if (FindPeer(targetPlayer) is { } peer)
                    _host.SendReliableTo(peer, Build(target, targetPlayer, name, values, self));

                break;
        }
    }

    private void DispatchFromClient(NetRpcTarget target, byte targetPlayer, string name, NetRpcValue[] values, byte self)
    {
        if (_client is null) return;

        // Alvo somos nós mesmos: entrega direta, sem gastar viagem até o host e de volta.
        if (target == NetRpcTarget.Player && targetPlayer == self)
        {
            Invoke(name, self, values);
            return;
        }

        // "Todo mundo" inclui quem mandou, e o host não retransmite de volta pro remetente —
        // então a entrega local é por nossa conta.
        if (target == NetRpcTarget.All)
            Invoke(name, self, values);

        _client.SendReliable(Build(target, targetPlayer, name, values, self));
    }

    private void BroadcastReliable(NetRpcTarget target, byte targetPlayer, string name,
        NetRpcValue[] values, byte senderId, NetPeer? except)
    {
        if (_host is null) return;

        var packet = Build(target, targetPlayer, name, values, senderId);
        if (packet.IsEmpty) return;

        _host.BroadcastReliable(packet, except);
    }

    /// <summary>Host recebendo um RPC de um cliente: entrega aqui e/ou retransmite.</summary>
    private void OnHostPacket(NetPeer from, ref NetReader reader)
    {
        if (reader.Type != NetMessageType.Rpc) return;
        if (!TryRead(ref reader, out uint hash, out _, out var target, out byte targetPlayer, out var values)) return;

        // O id do remetente é o do peer que mandou o pacote, não o que o pacote afirma. Sem
        // isso, um cliente se passaria por outro (ou pelo host) só mudando um byte.
        byte senderId = from.Id;
        string name = NameOf(hash);

        bool broadcast = target is NetRpcTarget.All or NetRpcTarget.Others;
        if (broadcast && !AllowClientBroadcast) return;

        switch (target)
        {
            case NetRpcTarget.All:
            case NetRpcTarget.Others:
                // Nos dois casos o host é destinatário: em All porque "todo mundo" inclui ele,
                // em Others porque ele é "outro" em relação a quem mandou.
                Invoke(name, senderId, values, hash);
                BroadcastReliable(target, targetPlayer, name, values, senderId, except: from);
                break;

            case NetRpcTarget.Host:
                Invoke(name, senderId, values, hash);
                break;

            case NetRpcTarget.Player:
                if (targetPlayer == NetProtocol.HostId)
                {
                    Invoke(name, senderId, values, hash);
                    break;
                }

                if (_host is not null && FindPeer(targetPlayer) is { } peer)
                    _host.SendReliableTo(peer, Build(target, targetPlayer, name, values, senderId));

                break;
        }
    }

    /// <summary>Cliente recebendo um RPC (sempre vindo do host, direto ou retransmitido).</summary>
    private void OnClientPacket(ref NetReader reader)
    {
        if (reader.Type != NetMessageType.Rpc) return;
        if (!TryRead(ref reader, out uint hash, out byte senderId, out _, out _, out var values)) return;

        Invoke(NameOf(hash), senderId, values, hash);
    }

    private void Invoke(string name, byte senderId, NetRpcValue[] values, uint? knownHash = null)
    {
        uint hash = knownHash ?? Hash(name);

        // RPC que esta máquina não conhece: build diferente, ou evento que só o outro lado
        // trata. Ignorar é o comportamento certo — não é erro.
        if (!_handlers.TryGetValue(hash, out var registration)) return;

        registration.Handler(new NetRpcArgs(registration.Name, senderId, values));
    }

    /// <summary>Monta o pacote. Devolve vazio quando os argumentos não coubessem — melhor não
    /// mandar nada que mandar um pacote cortado que o outro lado descartaria de qualquer jeito.</summary>
    private ReadOnlySpan<byte> Build(NetRpcTarget target, byte targetPlayer, string name,
        NetRpcValue[] values, byte senderId)
    {
        var writer = new NetWriter(_sendBuffer, NetMessageType.Rpc);
        writer.WriteUInt32(Hash(name));
        writer.WriteByte(senderId);
        writer.WriteByte((byte)target);
        writer.WriteByte(targetPlayer);
        writer.WriteByte((byte)values.Length);

        foreach (var value in values)
        {
            writer.WriteByte((byte)value.Kind);

            switch (value.Kind)
            {
                case NetRpcArgKind.Int:
                    writer.WriteUInt32(unchecked((uint)value.IntValue));
                    break;

                case NetRpcArgKind.Float:
                    writer.WriteSingle(value.FloatValue);
                    break;

                case NetRpcArgKind.Bool:
                    writer.WriteByte(value.IntValue != 0 ? (byte)1 : (byte)0);
                    break;

                case NetRpcArgKind.String:
                    writer.WriteString(value.StringValue);
                    break;
            }
        }

        return writer.Overflowed ? ReadOnlySpan<byte>.Empty : writer.Written;
    }

    private static bool TryRead(ref NetReader reader, out uint hash, out byte senderId,
        out NetRpcTarget target, out byte targetPlayer, out NetRpcValue[] values)
    {
        hash = 0;
        senderId = 0;
        target = NetRpcTarget.All;
        targetPlayer = 0;
        values = [];

        if (!reader.TryReadUInt32(out hash)) return false;
        if (!reader.TryReadByte(out senderId)) return false;
        if (!reader.TryReadByte(out byte rawTarget)) return false;
        if (!reader.TryReadByte(out targetPlayer)) return false;
        if (!reader.TryReadByte(out byte count)) return false;
        if (count > NetProtocol.MaxRpcArgs) return false;

        target = (NetRpcTarget)rawTarget;

        var parsed = new NetRpcValue[count];
        for (int i = 0; i < count; i++)
        {
            if (!reader.TryReadByte(out byte kind)) return false;

            switch ((NetRpcArgKind)kind)
            {
                case NetRpcArgKind.Int:
                    if (!reader.TryReadUInt32(out uint raw)) return false;
                    parsed[i] = NetRpcValue.FromInt(unchecked((int)raw));
                    break;

                case NetRpcArgKind.Float:
                    if (!reader.TryReadSingle(out float f)) return false;
                    parsed[i] = NetRpcValue.FromFloat(f);
                    break;

                case NetRpcArgKind.Bool:
                    if (!reader.TryReadByte(out byte b)) return false;
                    parsed[i] = NetRpcValue.FromBool(b != 0);
                    break;

                case NetRpcArgKind.String:
                    if (!reader.TryReadString(out string s)) return false;
                    parsed[i] = NetRpcValue.FromString(s);
                    break;

                default:
                    return false;
            }
        }

        values = parsed;
        return true;
    }

    private static NetRpcValue[] ToValues(object?[] args)
    {
        if (args.Length > NetProtocol.MaxRpcArgs)
            throw new ArgumentException($"RPC aceita no máximo {NetProtocol.MaxRpcArgs} argumentos.", nameof(args));

        var values = new NetRpcValue[args.Length];
        for (int i = 0; i < args.Length; i++)
            values[i] = NetRpcValue.From(args[i]);

        return values;
    }

    private NetPeer? FindPeer(byte playerId)
    {
        if (_host is null) return null;

        foreach (var peer in _host.Peers)
        {
            if (peer.Id == playerId) return peer;
        }

        return null;
    }

    private string NameOf(uint hash) => _handlers.TryGetValue(hash, out var r) ? r.Name : "<desconhecido>";

    /// <summary>FNV-1a de 32 bits. O nome não viaja: 4 bytes em vez de dezenas, num pacote que
    /// já é o mais caro do protocolo por ser confiável.</summary>
    private static uint Hash(string name)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;

        uint hash = offset;
        foreach (char c in name)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash;
    }

    internal void Attach(NetHost host)
    {
        Detach();

        _host = host;
        host.PacketReceived += OnHostPacket;
    }

    internal void Attach(NetClient client)
    {
        Detach();

        _client = client;
        client.PacketReceived += OnClientPacket;
    }

    internal void Detach()
    {
        if (_host is not null)
        {
            _host.PacketReceived -= OnHostPacket;
            _host = null;
        }

        if (_client is null) return;

        _client.PacketReceived -= OnClientPacket;
        _client = null;
    }
}
