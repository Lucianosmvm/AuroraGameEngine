using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;

namespace Aurora.Runtime.Net;

/// <summary>Quem decide onde as entidades estão.</summary>
public enum NetAuthority
{
    /// <summary>
    /// Cada máquina manda nas próprias entidades e transmite a posição pronta; o host junta e
    /// reenvia. Movimento de resposta imediata e quase nenhum código: seu boneco nunca espera
    /// resposta pra andar. Em troca, um cliente modificado consegue se teletransportar —
    /// aceitável em jogo com amigos na mesma rede. É o padrão.
    /// </summary>
    Owner = 0,

    /// <summary>
    /// O cliente manda o que está apertando, o host simula e devolve o resultado. Um cliente
    /// modificado não consegue mais inventar posição, mas o jogo precisa de uma função de
    /// movimento (<see cref="NetMoveFunc"/>) que rode igual nos dois lados, porque é ela que
    /// o cliente usa pra prever localmente e não sentir a viagem de ida e volta.
    /// </summary>
    Host = 1,
}

/// <summary>
/// Mantém as entidades marcadas com <see cref="NetworkIdentity"/> parecidas em todas as
/// máquinas: transmite as que este jogador controla e reproduz, interpoladas, as dos outros.
///
/// <para>Quem decide a posição depende de <see cref="Authority"/> — ver
/// <see cref="NetAuthority"/>. Nos dois modos o host transmite o estado completo da sala 20
/// vezes por segundo e as entidades dos outros jogadores são mostradas interpoladas.</para>
///
/// <para>O que trafega é só posição e rotação (e, no modo autoritativo, o input). Tilemap e
/// decoração não entram: cada máquina carrega o mesmo JSON.</para>
/// </summary>
public sealed class NetSyncSystem
{
    private sealed class Synced
    {
        public required int EntityId;
        public required NetworkIdentity Identity;

        /// <summary>True quando esta máquina criou a entidade a partir do snapshot — é o que
        /// pode ser destruído com segurança ao sair da sala. Entidade que veio da cena não.</summary>
        public required bool SpawnedByNet;

        public readonly NetInterpolator Interpolator = new();
    }

    /// <summary>Input recebido de um jogador e ainda não simulado.</summary>
    private sealed class InputQueue
    {
        public readonly List<NetInput> Pending = [];

        /// <summary>Último número de ordem já simulado. Viaja de volta no snapshot pro cliente
        /// saber o que pode descartar da própria previsão.</summary>
        public uint LastProcessed;
    }

    private readonly record struct EntityState(
        ushort NetId, byte OwnerId, byte PrefabId, Vector2 Position, float Rotation, byte AnimClip);

    /// <summary>Valor de <see cref="EntityState.AnimClip"/> pra "esta entidade não tem
    /// animação em rede" — sem <see cref="Animator"/>, ou tocando um clipe que não está na
    /// lista. Custa 1 byte por entidade mandar sempre, e a alternativa (um campo opcional)
    /// custaria mais em complexidade de formato do que economiza em banda.</summary>
    private const byte NoAnimClip = 255;

    private static readonly Comparison<NetInput> BySequence = static (a, b) => a.Sequence.CompareTo(b.Sequence);

    /// <summary>Teto de frames de input guardados no cliente à espera de confirmação (2 s a
    /// 60 FPS). Só enche se o host parar de responder — e nesse caso a partida já caiu.</summary>
    private const int MaxPendingInputs = 120;

    /// <summary>Teto de frames de input enfileirados por jogador no host. Barra cliente
    /// despejando input mais rápido que o normal pra ganhar movimento extra.</summary>
    private const int MaxQueuedInputs = 64;

    private readonly NetSession _session;
    private readonly World _world;

    private readonly Dictionary<ushort, Synced> _synced = [];
    private readonly Dictionary<byte, InputQueue> _inputQueues = [];
    private readonly List<NetInput> _pendingInputs = [];
    private readonly List<Synced> _outgoing = [];
    private readonly List<EntityState> _incoming = [];
    private readonly List<ushort> _removals = [];
    private readonly HashSet<ushort> _present = [];
    private readonly HashSet<byte> _missingPrefabsLogged = [];
    private readonly byte[] _sendBuffer = new byte[NetProtocol.MaxPacketSize];

    /// <summary>Última sequência aceita de cada origem (índice = id do jogador; o host é 0).
    /// UDP reordena, e aplicar um pacote velho depois de um novo faria a entidade voltar
    /// no tempo por um frame.</summary>
    private readonly ushort[] _lastSequence = new ushort[NetProtocol.MaxPlayersLimit];
    private readonly bool[] _hasSequence = new bool[NetProtocol.MaxPlayersLimit];

    private NetHost? _hookedHost;
    private NetClient? _hookedClient;

    private float _time;
    private float _sinceSend;
    private ushort _sequence;
    private ushort _nextNetId = 1;
    private uint _inputSequence;

    public NetSyncSystem(NetSession session, World world)
    {
        _session = session;
        _world = world;
    }

    /// <summary>Receitas pra recriar entidades nas outras máquinas. Registre no <c>OnLoad</c>.</summary>
    public NetSpawnRegistry Prefabs { get; } = new();

    /// <summary>Quem decide a posição das entidades. Defina antes de hospedar/entrar, e igual
    /// em todas as máquinas — modos diferentes na mesma sala fazem os dois lados esperarem
    /// dados que o outro nunca manda.</summary>
    public NetAuthority Authority { get; set; } = NetAuthority.Owner;

    /// <summary>Lê o input deste jogador. Obrigatório em <see cref="NetAuthority.Host"/>,
    /// ignorado em <see cref="NetAuthority.Owner"/>.</summary>
    public NetInputSampler? SampleInput { get; set; }

    /// <summary>Pacotes de estado por segundo. 20 Hz é o padrão de jogo de ação em LAN:
    /// abaixo disso a interpolação precisa de atraso maior pra continuar suave, acima gasta
    /// banda sem melhora visível, porque a suavidade vem da interpolação e não da taxa.</summary>
    public float SnapshotRate { get; set; } = 20f;

    /// <summary>
    /// Quanto tempo, em segundos, as entidades dos outros são mostradas no passado. Tem que
    /// ser pelo menos um intervalo de snapshot, senão falta a amostra "da frente" e o
    /// movimento volta a engasgar. 0,1 s cobre 20 Hz com folga pra um pacote perdido.
    /// </summary>
    public float InterpolationDelay { get; set; } = 0.1f;

    /// <summary>Quantos frames de input anteriores viajam repetidos em cada pacote.
    /// Ver <see cref="NetProtocol.MaxInputRedundancy"/>.</summary>
    public int InputRedundancy { get; set; } = 3;

    /// <summary>Duração máxima aceita num frame de input. Mesmo espírito do
    /// <c>Game.MaxDeltaTime</c>, e a mesma trava contra cliente pedindo um frame de 10
    /// segundos pra atravessar o mapa de uma vez.</summary>
    public float MaxInputDelta { get; set; } = 0.05f;

    /// <summary>
    /// Distância, em pixels, a partir da qual a correção do host é aceita visualmente. Host e
    /// cliente somam os mesmos passos em ordens ligeiramente diferentes e divergem por frações
    /// de pixel; aceitar essa diferença a 20 Hz faria o boneco vibrar parado. Acima do limite
    /// a divergência é real (colidiu com algo que o cliente não previu) e a posição do host vale.
    /// </summary>
    public float ReconcileThreshold { get; set; } = 0.5f;

    /// <summary>Quantas entidades estão sendo sincronizadas agora.</summary>
    public int SyncedCount => _synced.Count;

    /// <summary>Último input deste cliente que o host confirmou ter simulado.</summary>
    public uint LastAcknowledgedInput { get; private set; }

    /// <summary>Frames de input já previstos localmente e ainda não confirmados. Em rede
    /// saudável fica em 1 ou 2; subindo, é sinal de atraso ou perda.</summary>
    public int PendingInputCount => _pendingInputs.Count;

    private float SendInterval => 1f / MathF.Max(SnapshotRate, 1f);

    /// <summary>
    /// Cria uma entidade em rede. Só o host — é ele que distribui os
    /// <see cref="NetworkIdentity.NetId"/>, e dois donos numerando ao mesmo tempo dariam
    /// entidades diferentes com o mesmo número.
    /// </summary>
    /// <param name="prefabId">Receita registrada em <see cref="Prefabs"/>.</param>
    /// <param name="ownerId">Jogador que controla a entidade (0 = host).</param>
    public Entity Spawn(byte prefabId, byte ownerId)
    {
        if (!_session.IsHost)
            throw new InvalidOperationException("Só o host pode criar entidades em rede.");

        var identity = new NetworkIdentity
        {
            NetId = NextNetId(),
            OwnerId = ownerId,
            PrefabId = prefabId,
            IsMine = ownerId == NetProtocol.HostId,
        };

        if (!Prefabs.TrySpawn(_world, identity, out var entity))
            throw new InvalidOperationException($"PrefabId {prefabId} não está registrado em Prefabs.");

        Register(entity.Id, identity, spawnedByNet: true);
        return entity;
    }

    /// <summary>Destrói uma entidade em rede. Só o host — nos clientes ela some sozinha
    /// quando deixa de aparecer no snapshot.</summary>
    public void Despawn(ushort netId)
    {
        if (!_session.IsHost)
            throw new InvalidOperationException("Só o host pode destruir entidades em rede.");

        if (!_synced.Remove(netId, out var synced)) return;
        if (_world.IsAlive(synced.EntityId))
            _world.Destroy(synced.EntityId);
    }

    public bool TryGetEntity(ushort netId, out Entity entity)
    {
        if (_synced.TryGetValue(netId, out var synced) && _world.IsAlive(synced.EntityId))
        {
            entity = _world.GetEntity(synced.EntityId);
            return true;
        }

        entity = default;
        return false;
    }

    /// <summary>Bombeia a sincronização. Chamado por <see cref="NetSession.Update"/> logo
    /// depois do host/cliente terem consumido os pacotes do frame.</summary>
    public void Update(float deltaTime)
    {
        _time += deltaTime;
        RefreshHooks();

        if (!_session.IsReady)
        {
            // Saiu da sala (ou ainda não entrou): tira da cena os bonecos que só existiam
            // porque a rede mandou, senão eles ficam congelados no mapa pra sempre.
            if (_synced.Count > 0) Reset();
            return;
        }

        PruneDead();

        if (Authority == NetAuthority.Host)
            Simulate(deltaTime);

        ApplyRemoteState();

        if (_session.IsHost)
        {
            _sinceSend += deltaTime;
            if (_sinceSend < SendInterval) return;

            _sinceSend = 0f;
            AdoptUnregistered();
            BroadcastSnapshot();
            return;
        }

        if (Authority == NetAuthority.Host) return;

        _sinceSend += deltaTime;
        if (_sinceSend < SendInterval) return;

        _sinceSend = 0f;
        SendOwnedState();
    }

    /// <summary>Modo autoritativo: transforma input em movimento. No host vale pra valer;
    /// no cliente é previsão local, corrigida depois pelo snapshot.</summary>
    private void Simulate(float deltaTime)
    {
        if (_session.IsHost)
        {
            ApplyQueuedInputs();
            ApplyLocalInput(deltaTime, NetProtocol.HostId, predict: false);
            return;
        }

        ApplyLocalInput(deltaTime, _session.SelfId, predict: true);
    }

    /// <summary>Host simulando o que os clientes pediram, na ordem em que pediram.</summary>
    private void ApplyQueuedInputs()
    {
        foreach (var (playerId, queue) in _inputQueues)
        {
            if (queue.Pending.Count == 0) continue;

            // Pacote com repetição e reordenação de UDP deixam a fila fora de ordem, e input
            // aplicado fora de ordem produz um caminho diferente do que o jogador percorreu.
            queue.Pending.Sort(BySequence);

            foreach (var input in queue.Pending)
            {
                if (input.Sequence <= queue.LastProcessed) continue;

                MoveEntitiesOf(playerId, input);
                queue.LastProcessed = input.Sequence;
            }

            queue.Pending.Clear();
        }
    }

    private void ApplyLocalInput(float deltaTime, byte playerId, bool predict)
    {
        if (SampleInput is null) return;
        if (!HasMovableEntity(playerId)) return;

        var input = new NetInput(++_inputSequence, deltaTime, SampleInput()).Sanitized(MaxInputDelta);
        MoveEntitiesOf(playerId, input);

        if (!predict) return;

        // Guarda pra poder refazer em cima da posição que o host mandar: entre o envio deste
        // input e a resposta chegar, o jogador já apertou mais uma dúzia de frames, e sem
        // refazê-los a correção jogaria o boneco de volta pro passado.
        _pendingInputs.Add(input);
        if (_pendingInputs.Count > MaxPendingInputs)
            _pendingInputs.RemoveAt(0);

        SendInput();
    }

    private bool HasMovableEntity(byte playerId)
    {
        foreach (var synced in _synced.Values)
        {
            if (synced.Identity.OwnerId == playerId && MoveOf(synced) is not null)
                return true;
        }

        return false;
    }

    private void MoveEntitiesOf(byte playerId, in NetInput input)
    {
        foreach (var synced in _synced.Values)
        {
            if (synced.Identity.OwnerId != playerId) continue;
            if (MoveOf(synced) is not { } move) continue;
            if (!_world.IsAlive(synced.EntityId)) continue;

            move(_world.GetEntity(synced.EntityId), in input);
        }
    }

    private NetMoveFunc? MoveOf(Synced synced) => Prefabs.GetMove(synced.Identity.PrefabId);

    /// <summary>True quando a posição desta entidade é calculada aqui — pela lógica do jogo
    /// (modo <see cref="NetAuthority.Owner"/>) ou pelo input (modo
    /// <see cref="NetAuthority.Host"/>). O que não é calculado aqui é interpolado.</summary>
    private bool SimulatesLocally(Synced synced)
        => Authority == NetAuthority.Owner
            ? synced.Identity.IsMine
            : _session.IsHost || (synced.Identity.IsMine && MoveOf(synced) is not null);

    /// <summary>
    /// Entidades com <see cref="NetworkIdentity"/> que ainda não têm número — vieram da cena
    /// ou foram criadas direto pelo jogo. Só o host numera.
    /// </summary>
    private void AdoptUnregistered()
    {
        foreach (var (entity, _, identity) in _world.Query<Transform, NetworkIdentity>())
        {
            if (identity.NetId != 0) continue;

            identity.NetId = NextNetId();
            identity.IsMine = identity.OwnerId == NetProtocol.HostId;
            Register(entity.Id, identity, spawnedByNet: false);
        }
    }

    /// <summary>Aplica a posição interpolada em tudo que não é simulado aqui.</summary>
    private void ApplyRemoteState()
    {
        float renderTime = _time - InterpolationDelay;

        foreach (var synced in _synced.Values)
        {
            if (SimulatesLocally(synced)) continue;
            if (_world.Get<Transform>(synced.EntityId) is not { } transform) continue;
            if (!synced.Interpolator.Sample(renderTime, out var position, out float rotation)) continue;

            transform.Position = position;
            transform.Rotation = rotation;
        }
    }

    private void PruneDead()
    {
        _removals.Clear();

        foreach (var (netId, synced) in _synced)
        {
            if (!_world.IsAlive(synced.EntityId))
                _removals.Add(netId);
        }

        foreach (ushort netId in _removals)
            _synced.Remove(netId);
    }

    private void BroadcastSnapshot()
    {
        if (_session.Host is not { } host) return;

        _outgoing.Clear();
        foreach (var synced in _synced.Values)
        {
            if (_outgoing.Count >= NetProtocol.MaxSyncedEntities) break;
            if (_world.Get<Transform>(synced.EntityId) is null) continue;

            _outgoing.Add(synced);
        }

        var writer = new NetWriter(_sendBuffer, NetMessageType.Snapshot);
        writer.WriteUInt16(++_sequence);

        // Confirmação de input junto do estado, no mesmo pacote: separados, poderiam chegar
        // fora de ordem e o cliente refaria a previsão em cima de um estado que não casa com
        // o input confirmado — erro pequeno, mas que aparece como tremor constante.
        WriteInputAcks(ref writer);

        writer.WriteByte((byte)_outgoing.Count);
        foreach (var synced in _outgoing)
        {
            var transform = _world.Get<Transform>(synced.EntityId)!;

            writer.WriteUInt16(synced.Identity.NetId);
            writer.WriteByte(synced.Identity.OwnerId);
            writer.WriteByte(synced.Identity.PrefabId);
            writer.WriteSingle(transform.Position.X);
            writer.WriteSingle(transform.Position.Y);
            writer.WriteSingle(transform.Rotation);
            writer.WriteByte(AnimClipOf(synced.EntityId));
        }

        if (writer.Overflowed) return;

        host.Broadcast(writer.Written);
    }

    private void WriteInputAcks(ref NetWriter writer)
    {
        if (Authority != NetAuthority.Host || _inputQueues.Count == 0)
        {
            writer.WriteByte(0);
            return;
        }

        int count = Math.Min(_inputQueues.Count, NetProtocol.MaxPlayersLimit);
        writer.WriteByte((byte)count);

        int written = 0;
        foreach (var (playerId, queue) in _inputQueues)
        {
            if (written++ >= count) break;

            writer.WriteByte(playerId);
            writer.WriteUInt32(queue.LastProcessed);
        }
    }

    private void SendOwnedState()
    {
        if (_session.Client is not { } client) return;

        _outgoing.Clear();
        foreach (var synced in _synced.Values)
        {
            if (!synced.Identity.IsMine) continue;
            if (_outgoing.Count >= NetProtocol.MaxSyncedEntities) break;
            if (_world.Get<Transform>(synced.EntityId) is null) continue;

            _outgoing.Add(synced);
        }

        // Nada a dizer ainda (o host ainda não criou o boneco deste jogador): não gasta pacote.
        if (_outgoing.Count == 0) return;

        var writer = new NetWriter(_sendBuffer, NetMessageType.OwnedState);
        writer.WriteUInt16(++_sequence);
        writer.WriteByte((byte)_outgoing.Count);

        foreach (var synced in _outgoing)
        {
            var transform = _world.Get<Transform>(synced.EntityId)!;

            writer.WriteUInt16(synced.Identity.NetId);
            writer.WriteSingle(transform.Position.X);
            writer.WriteSingle(transform.Position.Y);
            writer.WriteSingle(transform.Rotation);
            writer.WriteByte(AnimClipOf(synced.EntityId));
        }

        if (writer.Overflowed) return;

        client.Send(writer.Written);
    }

    /// <summary>Manda os últimos frames de input. Sai todo frame, não na taxa de snapshot:
    /// input é o que dá a sensação de resposta, e segurá-lo pra economizar pacote atrasaria
    /// exatamente o que não pode atrasar.</summary>
    private void SendInput()
    {
        if (_session.Client is not { } client) return;
        if (_pendingInputs.Count == 0) return;

        int redundancy = Math.Clamp(InputRedundancy, 1, NetProtocol.MaxInputRedundancy);
        int count = Math.Min(redundancy, _pendingInputs.Count);
        int start = _pendingInputs.Count - count;

        var writer = new NetWriter(_sendBuffer, NetMessageType.Input);
        writer.WriteByte((byte)count);

        for (int i = start; i < _pendingInputs.Count; i++)
        {
            var input = _pendingInputs[i];

            writer.WriteUInt32(input.Sequence);
            writer.WriteSingle(input.DeltaTime);
            writer.WriteSingle(input.AxisX);
            writer.WriteSingle(input.AxisY);
            writer.WriteUInt32(input.Buttons);
        }

        if (writer.Overflowed) return;

        client.Send(writer.Written);
    }

    /// <summary>Host recebendo dos clientes.</summary>
    private void OnHostPacket(NetPeer from, ref NetReader reader)
    {
        switch (reader.Type)
        {
            case NetMessageType.OwnedState when Authority == NetAuthority.Owner:
                ReadOwnedState(from, ref reader);
                break;

            case NetMessageType.Input when Authority == NetAuthority.Host:
                ReadInput(from, ref reader);
                break;

            // Mensagem do outro modo de autoridade: em Host, um OwnedState é exatamente a
            // tentativa de burlar a autoridade que o modo existe pra impedir.
            default:
                break;
        }
    }

    private void ReadOwnedState(NetPeer from, ref NetReader reader)
    {
        if (!reader.TryReadUInt16(out ushort sequence)) return;
        if (!AcceptSequence(from.Id, sequence)) return;
        if (!reader.TryReadByte(out byte count)) return;

        for (int i = 0; i < count; i++)
        {
            if (!reader.TryReadUInt16(out ushort netId)) return;
            if (!reader.TryReadSingle(out float x)) return;
            if (!reader.TryReadSingle(out float y)) return;
            if (!reader.TryReadSingle(out float rotation)) return;
            if (!reader.TryReadByte(out byte animClip)) return;

            if (!_synced.TryGetValue(netId, out var synced)) continue;

            // Cliente só manda nas entidades dele. Sem esta checagem, um jogador conseguiria
            // arrastar o boneco de qualquer outro pelo mapa.
            if (synced.Identity.OwnerId != from.Id) continue;

            synced.Interpolator.Push(_time, new Vector2(x, y), rotation);

            // A animação do boneco de um cliente é decidida na máquina dele. Sem isto o host
            // nunca saberia qual clipe está tocando, e reenviaria pros outros clientes o clipe
            // parado da própria cópia — todo mundo veria o jogador deslizando em pé.
            ApplyAnimClip(synced.EntityId, animClip);
        }
    }

    private void ReadInput(NetPeer from, ref NetReader reader)
    {
        if (!reader.TryReadByte(out byte count)) return;

        var queue = GetInputQueue(from.Id);

        for (int i = 0; i < count; i++)
        {
            if (!reader.TryReadUInt32(out uint sequence)) return;
            if (!reader.TryReadSingle(out float deltaTime)) return;
            if (!reader.TryReadSingle(out float axisX)) return;
            if (!reader.TryReadSingle(out float axisY)) return;
            if (!reader.TryReadUInt32(out uint buttons)) return;

            // Já simulado: é a cópia redundante que veio junto pra cobrir perda de pacote.
            if (sequence <= queue.LastProcessed) continue;
            if (queue.Pending.Count >= MaxQueuedInputs) continue;
            if (AlreadyQueued(queue, sequence)) continue;

            var input = new NetInput(sequence, deltaTime, new NetInputState(axisX, axisY, buttons));
            queue.Pending.Add(input.Sanitized(MaxInputDelta));
        }
    }

    private static bool AlreadyQueued(InputQueue queue, uint sequence)
    {
        foreach (var pending in queue.Pending)
        {
            if (pending.Sequence == sequence) return true;
        }

        return false;
    }

    private InputQueue GetInputQueue(byte playerId)
    {
        if (!_inputQueues.TryGetValue(playerId, out var queue))
            _inputQueues[playerId] = queue = new InputQueue();

        return queue;
    }

    /// <summary>Cliente recebendo o estado completo da sala.</summary>
    private void OnClientPacket(ref NetReader reader)
    {
        if (reader.Type != NetMessageType.Snapshot) return;
        if (!reader.TryReadUInt16(out ushort sequence)) return;
        if (!AcceptSequence(NetProtocol.HostId, sequence)) return;

        if (!reader.TryReadByte(out byte ackCount)) return;

        uint acknowledged = LastAcknowledgedInput;
        for (int i = 0; i < ackCount; i++)
        {
            if (!reader.TryReadByte(out byte playerId)) return;
            if (!reader.TryReadUInt32(out uint lastProcessed)) return;

            if (playerId == _session.SelfId)
                acknowledged = lastProcessed;
        }

        if (!reader.TryReadByte(out byte count)) return;

        // Lê tudo antes de aplicar: "quem não está na lista foi destruído" só vale pra lista
        // inteira. Aplicar um pacote truncado apagaria da cena metade dos jogadores.
        _incoming.Clear();
        for (int i = 0; i < count; i++)
        {
            if (!reader.TryReadUInt16(out ushort netId)) return;
            if (!reader.TryReadByte(out byte ownerId)) return;
            if (!reader.TryReadByte(out byte prefabId)) return;
            if (!reader.TryReadSingle(out float x)) return;
            if (!reader.TryReadSingle(out float y)) return;
            if (!reader.TryReadSingle(out float rotation)) return;
            if (!reader.TryReadByte(out byte animClip)) return;

            _incoming.Add(new EntityState(netId, ownerId, prefabId, new Vector2(x, y), rotation, animClip));
        }

        LastAcknowledgedInput = acknowledged;
        ApplySnapshot();
    }

    private void ApplySnapshot()
    {
        _present.Clear();

        foreach (var state in _incoming)
        {
            _present.Add(state.NetId);

            if (!_synced.TryGetValue(state.NetId, out var synced))
            {
                if (!SpawnFromSnapshot(state, out synced)) continue;
            }

            if (SimulatesLocally(synced))
            {
                // Animação da entidade deste jogador é decidida aqui, junto com o movimento —
                // aceitar a do host atrasaria a troca de clipe em um ida-e-volta e o boneco
                // ficaria trocando de pose depois do movimento que a provocou.
                Reconcile(synced, state);
                continue;
            }

            synced.Interpolator.Push(_time, state.Position, state.Rotation);
            ApplyAnimClip(synced.EntityId, state.AnimClip);
        }

        // Sumiu do snapshot = foi destruída no host.
        _removals.Clear();
        foreach (ushort netId in _synced.Keys)
        {
            if (!_present.Contains(netId))
                _removals.Add(netId);
        }

        foreach (ushort netId in _removals)
            DestroyLocal(netId);
    }

    /// <summary>
    /// Encaixa a previsão local na posição que o host confirmou: volta pro estado autoritativo
    /// e refaz por cima os frames de input que ainda não tinham sido processados quando aquele
    /// snapshot saiu. Sem o refazer, o boneco seria jogado de volta ~100 ms no passado a cada
    /// pacote; com ele, previsão certa não muda nada na tela.
    /// </summary>
    private void Reconcile(Synced synced, EntityState state)
    {
        if (Authority != NetAuthority.Host) return;
        if (MoveOf(synced) is not { } move) return;
        if (_world.Get<Transform>(synced.EntityId) is not { } transform) return;

        var predicted = transform.Position;
        float predictedRotation = transform.Rotation;

        DiscardConfirmedInputs();

        transform.Position = state.Position;
        transform.Rotation = state.Rotation;

        var entity = _world.GetEntity(synced.EntityId);
        foreach (var input in _pendingInputs)
            move(entity, in input);

        // Diferença dentro do limite é ruído de ponto flutuante, não discordância de verdade:
        // manter a previsão evita o tremor de reposicionar o boneco 20 vezes por segundo.
        if (Vector2.Distance(transform.Position, predicted) >= ReconcileThreshold) return;

        transform.Position = predicted;
        transform.Rotation = predictedRotation;
    }

    private void DiscardConfirmedInputs()
    {
        int keep = 0;
        for (int i = 0; i < _pendingInputs.Count; i++)
        {
            if (_pendingInputs[i].Sequence <= LastAcknowledgedInput) continue;

            _pendingInputs[keep++] = _pendingInputs[i];
        }

        _pendingInputs.RemoveRange(keep, _pendingInputs.Count - keep);
    }

    private bool SpawnFromSnapshot(EntityState state, out Synced synced)
    {
        synced = null!;

        if (state.PrefabId == 0)
        {
            // Entidade que deveria já existir na cena dos dois lados, mas não existe aqui.
            // Nada a criar — provavelmente as máquinas estão em cenas diferentes.
            return false;
        }

        var identity = new NetworkIdentity
        {
            NetId = state.NetId,
            OwnerId = state.OwnerId,
            PrefabId = state.PrefabId,
            IsMine = state.OwnerId == _session.SelfId,
        };

        if (!Prefabs.TrySpawn(_world, identity, out var entity))
        {
            // Build diferente ou prefab esquecido no registro: ignora a entidade em vez de
            // derrubar a partida, e loga uma vez só pra não inundar o console a 20 Hz.
            if (_missingPrefabsLogged.Add(state.PrefabId))
                Console.Error.WriteLine($"[Net] PrefabId {state.PrefabId} não registrado — entidade {state.NetId} ignorada.");

            return false;
        }

        // Já nasce no lugar certo: sem isso ela apareceria na origem do mundo e deslizaria
        // até a posição real durante o primeiro intervalo de interpolação.
        if (_world.Get<Transform>(entity.Id) is { } transform)
        {
            transform.Position = state.Position;
            transform.Rotation = state.Rotation;
        }

        ApplyAnimClip(entity.Id, state.AnimClip);

        synced = Register(entity.Id, identity, spawnedByNet: true);
        return true;
    }

    /// <summary>Índice do clipe que a entidade está tocando, pra caber num byte.
    /// O índice viaja em vez do nome porque a lista de clipes é a mesma nas duas máquinas —
    /// ela vem do mesmo prefab ou do mesmo JSON de cena.</summary>
    private byte AnimClipOf(int entityId)
    {
        if (_world.Get<Animator>(entityId) is not { CurrentClip: { } current } animator) return NoAnimClip;

        for (int i = 0; i < animator.Clips.Count && i < NoAnimClip; i++)
        {
            if (animator.Clips[i].Name == current) return (byte)i;
        }

        return NoAnimClip;
    }

    /// <summary>Põe a entidade remota no clipe que o dono dela está tocando. Sem isso o boneco
    /// dos outros anda pelo mapa em pé, parado na pose de descanso.</summary>
    private void ApplyAnimClip(int entityId, byte clipIndex)
    {
        if (clipIndex == NoAnimClip) return;
        if (_world.Get<Animator>(entityId) is not { } animator) return;
        if (clipIndex >= animator.Clips.Count) return;

        // Play já ignora pedido pro clipe atual, então isso não reinicia a animação a cada
        // snapshot — mas checar aqui evita até a chamada, 20 vezes por segundo por entidade.
        string name = animator.Clips[clipIndex].Name;
        if (animator.CurrentClip == name) return;

        animator.Play(name);
    }

    private void DestroyLocal(ushort netId)
    {
        if (!_synced.Remove(netId, out var synced)) return;
        if (!synced.SpawnedByNet) return;
        if (_world.IsAlive(synced.EntityId))
            _world.Destroy(synced.EntityId);
    }

    private Synced Register(int entityId, NetworkIdentity identity, bool spawnedByNet)
    {
        var synced = new Synced
        {
            EntityId = entityId,
            Identity = identity,
            SpawnedByNet = spawnedByNet,
        };

        _synced[identity.NetId] = synced;
        return synced;
    }

    /// <summary>Aceita a sequência se for mais nova que a última daquela origem. A comparação
    /// é por diferença com sinal pra continuar certa quando o contador dá a volta em 65535
    /// (a cada ~55 min a 20 Hz).</summary>
    private bool AcceptSequence(byte sourceId, ushort sequence)
    {
        if (sourceId >= _lastSequence.Length) return false;

        if (_hasSequence[sourceId] && (short)(sequence - _lastSequence[sourceId]) <= 0)
            return false;

        _hasSequence[sourceId] = true;
        _lastSequence[sourceId] = sequence;
        return true;
    }

    private ushort NextNetId()
    {
        // 0 é "sem número": pular no vai-e-volta do contador evita que uma entidade recém
        // criada seja confundida com uma ainda não registrada.
        while (_nextNetId == 0 || _synced.ContainsKey(_nextNetId))
            _nextNetId++;

        return _nextNetId++;
    }

    /// <summary>Assina os eventos do host/cliente atual. A sessão troca de instância a cada
    /// StartHost/Join, então a ligação é conferida por frame em vez de feita uma vez só.</summary>
    private void RefreshHooks()
    {
        var host = _session.Host;
        if (!ReferenceEquals(host, _hookedHost))
        {
            if (_hookedHost is not null)
            {
                _hookedHost.PacketReceived -= OnHostPacket;
                _hookedHost.PeerLeft -= OnPeerLeft;
            }

            if (host is not null)
            {
                host.PacketReceived += OnHostPacket;
                host.PeerLeft += OnPeerLeft;
            }

            _hookedHost = host;
            Reset();
        }

        var client = _session.Client;
        if (ReferenceEquals(client, _hookedClient)) return;

        if (_hookedClient is not null) _hookedClient.PacketReceived -= OnClientPacket;
        if (client is not null) client.PacketReceived += OnClientPacket;

        _hookedClient = client;
        Reset();
    }

    /// <summary>Jogador saiu: a fila de input dele tem que ir junto. Os ids são
    /// reaproveitados, e uma fila esquecida com <c>LastProcessed</c> alto faria o próximo
    /// jogador a receber aquele id ser ignorado por completo — ele entraria numerando do 1 e
    /// tudo seria descartado como "já processado".</summary>
    private void OnPeerLeft(NetPeer peer, NetDisconnectReason reason) => _inputQueues.Remove(peer.Id);

    private void Reset()
    {
        foreach (var synced in _synced.Values)
        {
            if (!synced.SpawnedByNet) continue;
            if (_world.IsAlive(synced.EntityId))
                _world.Destroy(synced.EntityId);
        }

        _synced.Clear();
        _incoming.Clear();
        _outgoing.Clear();
        _present.Clear();
        _inputQueues.Clear();
        _pendingInputs.Clear();
        Array.Clear(_hasSequence);
        Array.Clear(_lastSequence);
        _sequence = 0;
        _nextNetId = 1;
        _inputSequence = 0;
        LastAcknowledgedInput = 0;
        _sinceSend = 0f;
    }
}
