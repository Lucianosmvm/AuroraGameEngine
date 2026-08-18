using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;

namespace Aurora.Runtime.Net;

/// <summary>
/// Cria a entidade correspondente a um <see cref="NetworkIdentity.PrefabId"/>. Roda nas duas
/// pontas: no host quando alguém entra, no cliente quando um id desconhecido aparece no
/// snapshot.
/// <para>Use <c>identity.IsMine</c> pra montar coisas diferentes: o boneco deste jogador leva
/// o script de controle e a câmera; os outros levam só sprite e animação, porque a posição
/// deles vem pronta pela rede e um script de movimento local só brigaria com ela.</para>
/// </summary>
public delegate Entity NetSpawnFactory(World world, NetworkIdentity identity);

/// <summary>
/// Tabela de receitas de entidade em rede. Cada jogo registra as suas no <c>OnLoad</c>.
/// <para>O id é um byte no fio, não o nome da classe: nome custaria dezenas de bytes por
/// entidade em todo snapshot, e um número obriga as duas pontas a concordarem explicitamente
/// sobre o que estão criando.</para>
/// </summary>
public sealed class NetSpawnRegistry
{
    private readonly Dictionary<byte, NetSpawnFactory> _factories = [];
    private readonly Dictionary<byte, NetMoveFunc> _movers = [];

    /// <summary>Registra uma receita. <paramref name="prefabId"/> 0 é reservado pra entidades
    /// que já existem na cena dos dois lados e portanto não precisam ser criadas.</summary>
    /// <param name="move">Como esta entidade responde ao input do jogador. Necessária só no
    /// modo <see cref="NetAuthority.Host"/>, e só pra entidade que um jogador controla —
    /// caixote, projétil e inimigo movidos pela lógica do host não precisam.</param>
    public void Register(byte prefabId, NetSpawnFactory factory, NetMoveFunc? move = null)
    {
        if (prefabId == 0)
            throw new ArgumentOutOfRangeException(nameof(prefabId), "PrefabId 0 é reservado para entidades que já existem na cena.");

        _factories[prefabId] = factory;

        if (move is not null)
            _movers[prefabId] = move;
    }

    public bool IsRegistered(byte prefabId) => _factories.ContainsKey(prefabId);

    /// <summary>Função de movimento deste prefab, se ele for controlado por input.</summary>
    public NetMoveFunc? GetMove(byte prefabId) => _movers.GetValueOrDefault(prefabId);

    /// <summary>
    /// Cria a entidade e já pendura o <see cref="NetworkIdentity"/> nela. Devolve false se o
    /// prefab não foi registrado — acontece quando uma máquina está com o jogo desatualizado,
    /// e ignorar a entidade é bem melhor que derrubar a partida.
    /// </summary>
    public bool TrySpawn(World world, NetworkIdentity identity, out Entity entity)
    {
        entity = default;

        if (!_factories.TryGetValue(identity.PrefabId, out var factory))
            return false;

        entity = factory(world, identity);

        // A fábrica pode ou não ter adicionado o identity (o jeito natural é não adicionar e
        // deixar por conta daqui). Adicionar de novo é no-op quando é a mesma instância.
        entity.Add(identity);
        return true;
    }
}
