namespace Aurora.Runtime.Net;

/// <summary>Tipo de pacote no fio. Fase 1 cobre só o handshake e o keepalive —
/// snapshot/input entram na fase 2 com números novos, sem mexer nestes.</summary>
public enum NetMessageType : byte
{
    /// <summary>Cliente → host: "quero entrar", com o nome do jogador.</summary>
    Join = 1,

    /// <summary>Host → cliente: entrou. Traz o id dele e a lista de quem já estava dentro.</summary>
    JoinAccepted = 2,

    /// <summary>Host → cliente: recusado (sala cheia, versão errada).</summary>
    JoinRejected = 3,

    /// <summary>Host → todos: um jogador novo entrou.</summary>
    PeerJoined = 4,

    /// <summary>Host → todos: um jogador saiu (Bye ou timeout).</summary>
    PeerLeft = 5,

    /// <summary>Cliente → host: keepalive. Sem ele o host não distingue "parado" de "morreu".</summary>
    Ping = 6,

    /// <summary>Host → cliente: resposta do Ping. Também é o keepalive na direção contrária.</summary>
    Pong = 7,

    /// <summary>Desconexão limpa, nas duas direções.</summary>
    Bye = 8,

    /// <summary>Host → todos: estado de todas as entidades sincronizadas neste instante.</summary>
    Snapshot = 9,

    /// <summary>Cliente → host: estado das entidades que ele controla.
    /// Só vale em <see cref="NetAuthority.Owner"/>.</summary>
    OwnedState = 10,

    /// <summary>Cliente → host: o que o jogador está pedindo.
    /// Só vale em <see cref="NetAuthority.Host"/>.</summary>
    Input = 11,

    /// <summary>Envelope de entrega garantida — carrega outro pacote dentro.</summary>
    Reliable = 12,

    /// <summary>"Recebi tudo até a sequência N" do canal confiável.</summary>
    ReliableAck = 13,

    /// <summary>Evento nomeado do jogo (som, dano, porta abrindo). Viaja no canal confiável.</summary>
    Rpc = 14,

    /// <summary>Broadcast na LAN: "tem alguém hospedando este jogo?".</summary>
    Discover = 15,

    /// <summary>Resposta de um host a um <see cref="Discover"/>: nome da sala e lotação.</summary>
    RoomInfo = 16,
}

/// <summary>Motivo de um <see cref="NetMessageType.JoinRejected"/> — vira mensagem de tela no cliente.</summary>
public enum NetRejectReason : byte
{
    Unknown = 0,
    Full = 1,
    VersionMismatch = 2,
}

/// <summary>Constantes compartilhadas por host e cliente.</summary>
public static class NetProtocol
{
    /// <summary>Marca no começo de todo pacote. UDP entrega qualquer lixo que chegue na porta
    /// (scanner de rede, pacote atrasado de outro jogo); sem a marca, esse lixo seria
    /// interpretado como mensagem e derrubaria a sessão.</summary>
    public const byte Magic0 = (byte)'A';
    public const byte Magic1 = (byte)'U';

    /// <summary>Suba a cada mudança no formato do fio. Versões diferentes se recusam no join —
    /// bem melhor que dois builds se conectando e dessincronizando em silêncio.
    /// <para>2: snapshot ganhou o clipe de animação por entidade.</para>
    /// <para>3: busca de salas na rede local (Discover/RoomInfo).</para></summary>
    public const byte Version = 3;

    /// <summary>Cabeçalho: magic0, magic1, versão, tipo.</summary>
    public const int HeaderSize = 4;

    /// <summary>Teto de payload. Fica abaixo do MTU típico de 1500 com folga pra cabeçalho
    /// IP+UDP e qualquer encapsulamento (VPN, PPPoE) — pacote maior fragmenta, e fragmento
    /// perdido em UDP significa o pacote inteiro perdido.</summary>
    public const int MaxPacketSize = 1200;

    /// <summary>Porta padrão do host. Sem significado especial, só fora das faixas comuns.</summary>
    public const int DefaultPort = 7777;

    /// <summary>Teto de jogadores numa sala, host incluído.</summary>
    public const int MaxPlayersLimit = 8;

    /// <summary>Id do host. Sempre 0 — quem hospeda é jogador também.</summary>
    public const byte HostId = 0;

    /// <summary>Teto de entidades num snapshot. Cada uma ocupa 16 bytes; o limite existe pro
    /// snapshot inteiro caber num datagrama só. Um snapshot partido em vários pacotes
    /// quebraria a regra de "quem sumiu da lista foi destruído" — o cliente veria metade da
    /// cena desaparecer a cada frame.</summary>
    public const int MaxSyncedEntities = 64;

    /// <summary>Frames de input repetidos em cada pacote. Input perdido não pode ser reenviado
    /// depois (chegaria fora de hora e o host já teria seguido em frente), então cada pacote
    /// carrega também os anteriores: perder 2 pacotes seguidos ainda não perde nenhum frame.
    /// Custa 20 bytes por frame repetido, num pacote que sai 60 vezes por segundo.</summary>
    public const int MaxInputRedundancy = 8;

    /// <summary>Teto de argumentos num RPC. Chamada que precisa de mais que isso quase sempre
    /// quer mandar um objeto — e aí o certo é mandar o id da entidade e deixar o outro lado
    /// olhar o resto no próprio mundo.</summary>
    public const int MaxRpcArgs = 8;

    /// <summary>Identificador padrão do jogo na busca por salas. Só aparecem na lista os hosts
    /// que declararam o mesmo — sem isso, dois jogos Aurora diferentes na mesma rede
    /// apareceriam um na lista do outro e o join falharia sem explicação.</summary>
    public const string DefaultGameId = "Aurora";

    /// <summary>Nome de jogador maior que isso é cortado. Limite existe pra caber no pacote:
    /// JoinAccepted carrega a lista inteira de peers de uma vez.</summary>
    public const int MaxNameLength = 32;
}
