using Aurora.Runtime.Ecs;

namespace Aurora.Runtime.Net;

/// <summary>
/// O que o jogador está pedindo neste frame, sem nada de posição. Só isso trafega no modo
/// <see cref="NetAuthority.Host"/>: a posição é consequência, calculada por quem tem
/// autoridade, e mandar o resultado em vez do pedido é o que permitiria a um cliente
/// modificado se teletransportar.
/// </summary>
/// <param name="AxisX">Eixo horizontal, -1 a 1.</param>
/// <param name="AxisY">Eixo vertical, -1 a 1.</param>
/// <param name="Buttons">Até 32 ações ligadas/desligadas, uma por bit. O significado de cada
/// bit é do jogo — a engine só transporta.</param>
public readonly record struct NetInputState(float AxisX, float AxisY, uint Buttons)
{
    /// <summary>Testa uma ação. <paramref name="bit"/> de 0 a 31.</summary>
    public bool IsPressed(int bit) => (Buttons & (1u << bit)) != 0;

    /// <summary>Liga um bit de ação. Use na montagem do estado, no <see cref="NetInputSampler"/>.</summary>
    public NetInputState With(int bit, bool pressed)
        => this with { Buttons = pressed ? Buttons | (1u << bit) : Buttons & ~(1u << bit) };
}

/// <summary>
/// Um frame de input carimbado: o pedido do jogador mais o número de ordem e a duração do
/// frame. O número identifica o input do começo ao fim (o host confirma qual já processou);
/// a duração viaja junto porque host e cliente rodam em FPS diferentes, e replicar o
/// movimento exige o mesmo passo de tempo dos dois lados.
/// </summary>
public readonly record struct NetInput(uint Sequence, float DeltaTime, NetInputState State)
{
    public float AxisX => State.AxisX;
    public float AxisY => State.AxisY;
    public uint Buttons => State.Buttons;

    public bool IsPressed(int bit) => State.IsPressed(bit);

    /// <summary>
    /// Aparência dentro dos limites. Aplicada nos DOIS lados com os mesmos valores, e por dois
    /// motivos diferentes: no host porque um cliente modificado mandaria eixo 900 e frame de
    /// 10 segundos pra atravessar o mapa num pacote; no cliente porque, se só o host limitasse,
    /// a previsão local usaria números maiores que os simulados de verdade e cada snapshot
    /// chegaria corrigindo a posição.
    /// </summary>
    public NetInput Sanitized(float maxDeltaTime) => this with
    {
        DeltaTime = Math.Clamp(DeltaTime, 0f, maxDeltaTime),
        State = State with
        {
            AxisX = Math.Clamp(State.AxisX, -1f, 1f),
            AxisY = Math.Clamp(State.AxisY, -1f, 1f),
        },
    };
}

/// <summary>Lê o input deste jogador agora. Registrado em <see cref="NetSyncSystem.SampleInput"/>
/// e chamado uma vez por frame nas máquinas que controlam alguma entidade.</summary>
public delegate NetInputState NetInputSampler();

/// <summary>
/// Move a entidade por um frame de input. É o coração do modo autoritativo: roda no host pra
/// valer e no cliente pra prever, então tem que depender só do estado da entidade e do input
/// recebido — nada de ler teclado, relógio ou aleatório aqui dentro, senão as duas máquinas
/// chegam a posições diferentes e o jogador vê correção a cada pacote.
/// </summary>
public delegate void NetMoveFunc(Entity entity, in NetInput input);
