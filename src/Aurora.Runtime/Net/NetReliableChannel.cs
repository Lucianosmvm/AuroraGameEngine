namespace Aurora.Runtime.Net;

/// <summary>
/// Entrega garantida e em ordem por cima do UDP, para UMA direção de UM par.
///
/// <para>Posição de boneco pode se perder à vontade — o próximo snapshot conserta 50 ms
/// depois. Evento não: "levou 30 de dano" ou "a porta abriu" que some não volta, e as duas
/// máquinas ficam contando histórias diferentes pro resto da partida. Daí este canal, usado
/// só pelos RPCs.</para>
///
/// <para>Ordem importa tanto quanto a entrega: "nasceu" chegando depois de "morreu" deixaria
/// um cadáver andando. Mensagem que chega adiantada espera a anterior antes de ser entregue.</para>
/// </summary>
public sealed class NetReliableChannel
{
    private sealed class Unacked
    {
        public required uint Sequence;
        public required byte[] Data;
        public float SinceSent;
    }

    /// <summary>Teto de mensagens à espera de confirmação. Só enche se o outro lado parou de
    /// responder — e aí a conexão cai por timeout de qualquer jeito.</summary>
    private const int MaxUnacked = 256;

    /// <summary>Teto de mensagens adiantadas guardadas esperando a anterior.</summary>
    private const int MaxBuffered = 64;

    private readonly List<Unacked> _unacked = [];
    private readonly Dictionary<uint, byte[]> _buffered = [];
    private readonly byte[] _sendBuffer = new byte[NetProtocol.MaxPacketSize];

    private uint _nextSequence = 1;
    private uint _expected = 1;

    /// <summary>Como pôr um pacote no fio. Vem do <see cref="NetHost"/> (para um peer) ou do
    /// <see cref="NetClient"/> (para o host).</summary>
    public required Action<ReadOnlyMemory<byte>> Transmit { get; init; }

    /// <summary>Chegou uma mensagem completa e na ordem certa. O conteúdo é um pacote inteiro,
    /// pronto pra passar pelo <see cref="NetReader.TryParse"/> como qualquer outro.</summary>
    public event Action<byte[]>? Delivered;

    /// <summary>Tempo entre reenvios de uma mensagem ainda não confirmada. Precisa ser maior
    /// que o ida-e-volta típico, senão reenvia por cima de uma confirmação que já está a
    /// caminho; em LAN o ida-e-volta é ~1 ms, então 0,25 s é folga larga de propósito.</summary>
    public float ResendInterval { get; set; } = 0.25f;

    /// <summary>Mensagens ainda não confirmadas pelo outro lado.</summary>
    public int UnackedCount => _unacked.Count;

    /// <summary>Envia com garantia. <paramref name="packet"/> é um pacote já montado
    /// (cabeçalho e tudo) — vai empacotado dentro do envelope confiável e sai do outro lado
    /// igualzinho.</summary>
    public void Send(ReadOnlySpan<byte> packet)
    {
        if (_unacked.Count >= MaxUnacked)
        {
            // O outro lado não confirma nada há muito tempo. Descartar a mais antiga mantém a
            // memória limitada; a conexão em si já vai cair no timeout do keepalive.
            _unacked.RemoveAt(0);
        }

        uint sequence = _nextSequence++;
        byte[] envelope = BuildEnvelope(sequence, packet);

        _unacked.Add(new Unacked { Sequence = sequence, Data = envelope });
        Transmit(envelope);
    }

    /// <summary>Reenvia o que ainda não foi confirmado. Chame uma vez por frame.</summary>
    public void Update(float deltaTime)
    {
        foreach (var pending in _unacked)
        {
            pending.SinceSent += deltaTime;
            if (pending.SinceSent < ResendInterval) continue;

            pending.SinceSent = 0f;
            Transmit(pending.Data);
        }
    }

    /// <summary>Chegou um envelope confiável.</summary>
    public void OnReliable(ref NetReader reader)
    {
        if (!reader.TryReadUInt32(out uint sequence)) return;
        if (!reader.TryReadRemaining(out var payload)) return;

        if (sequence == _expected)
        {
            Deliver(payload.ToArray());
            _expected++;

            // A que faltava chegou: solta o que estava represado atrás dela.
            while (_buffered.Remove(_expected, out var next))
            {
                Deliver(next);
                _expected++;
            }
        }
        else if (sequence > _expected)
        {
            // Adiantada: guarda até a anterior chegar.
            if (_buffered.Count < MaxBuffered)
                _buffered.TryAdd(sequence, payload.ToArray());
        }

        // Por último, pra a confirmação já refletir o que acabou de ser entregue — mandada
        // antes, ela confirmaria sempre uma mensagem a menos e o remetente reenviaria pra
        // sempre a última de cada rajada.
        // Vai também para repetição e para adiantada: repetição significa justamente que a
        // confirmação anterior se perdeu, e ficar calado manteria o outro lado reenviando.
        SendAck();
    }

    /// <summary>Chegou uma confirmação.</summary>
    public void OnAck(ref NetReader reader)
    {
        if (!reader.TryReadUInt32(out uint lastContiguous)) return;

        // Confirmação é acumulada: "recebi tudo até aqui". Uma só limpa todas as anteriores,
        // então perder confirmações não trava nada — a próxima resolve.
        for (int i = _unacked.Count - 1; i >= 0; i--)
        {
            if (_unacked[i].Sequence <= lastContiguous)
                _unacked.RemoveAt(i);
        }
    }

    /// <summary>Zera o canal. Usado quando a conexão daquele par termina — sequências de uma
    /// sessão velha não podem ser confundidas com as da nova.</summary>
    public void Reset()
    {
        _unacked.Clear();
        _buffered.Clear();
        _nextSequence = 1;
        _expected = 1;
    }

    private void Deliver(byte[] packet) => Delivered?.Invoke(packet);

    private void SendAck()
    {
        var writer = new NetWriter(_sendBuffer, NetMessageType.ReliableAck);
        writer.WriteUInt32(_expected - 1);

        if (writer.Overflowed) return;

        Transmit(writer.Written.ToArray());
    }

    private static byte[] BuildEnvelope(uint sequence, ReadOnlySpan<byte> packet)
    {
        var envelope = new byte[NetProtocol.HeaderSize + 4 + packet.Length];

        var writer = new NetWriter(envelope, NetMessageType.Reliable);
        writer.WriteUInt32(sequence);
        writer.WriteBytes(packet);

        return envelope;
    }
}
