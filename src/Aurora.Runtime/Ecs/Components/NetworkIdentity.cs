namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Marca uma entidade como sincronizada pela rede. Só quem tem este componente entra no
/// snapshot — o cenário (tilemap, decoração) é idêntico em todas as máquinas porque cada uma
/// carrega o mesmo JSON, e mandá-lo pela rede seria desperdício puro.
/// </summary>
public sealed class NetworkIdentity : IComponent
{
    /// <summary>Identificador da entidade na sala, igual em todas as máquinas. 0 significa
    /// "ainda não registrada" — o host atribui o número na hora de sincronizar.</summary>
    public ushort NetId;

    /// <summary>Jogador dono desta entidade (0 = host). Dono é quem manda na posição:
    /// a máquina dele decide onde ela está e as outras só reproduzem.</summary>
    public byte OwnerId;

    /// <summary>Qual receita do <see cref="Net.NetSpawnRegistry"/> recria esta entidade nas
    /// outras máquinas. 0 = não recriável: a entidade tem que já existir na cena dos dois
    /// lados (um caixote posicionado no JSON, por exemplo).</summary>
    public byte PrefabId;

    /// <summary>True quando esta máquina é a dona. É o que separa "eu movo isso e transmito"
    /// de "eu só reproduzo o que chega". Preenchido pelo <see cref="Net.NetSyncSystem"/> ao
    /// registrar a entidade — não mexa na mão.</summary>
    public bool IsMine;
}
