using System.Numerics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Faz nascer prefabs num ritmo, com teto de quantos podem estar vivos ao mesmo tempo. É o
/// gerador de ondas, o ninho de inimigos, o spawn de recurso que repõe sozinho.
///
/// <para>Existe porque um <c>EventTrigger</c> com Timer + ação Spawn nasce pra sempre: ele não
/// tem como saber quantos dos que nasceram ainda estão de pé. Em cinco minutos isso vira uma
/// cena com centenas de inimigos e o jogo trava — o teto é justamente o que falta lá.</para>
/// </summary>
public sealed class Spawner : Behavior
{
    /// <summary>
    /// O que nasce: o caminho de um prefab ("prefabs/slime.json") ou o id de uma tabela de spawn
    /// do banco ("inimigos_floresta"). Com a tabela, cada nascimento sorteia entre vários tipos
    /// pelos pesos dela — é assim que uma cena tem slime, zumbi e morcego saindo do mesmo ninho
    /// sem precisar de três spawners. Vazio desliga o componente.
    /// </summary>
    public string Prefab = "";

    /// <summary>Segundos entre tentativas de nascimento.</summary>
    public float Interval = 3f;

    /// <summary>Quantos filhos deste spawner podem estar vivos ao mesmo tempo. Enquanto o teto
    /// está cheio o relógio continua correndo, mas nada nasce — o próximo sai assim que um
    /// morrer. 0 = sem teto (use com cuidado).</summary>
    public int MaxAlive = 5;

    /// <summary>Total que este spawner pode criar durante toda a cena. 0 = infinito. Serve pra
    /// onda fechada ("20 inimigos e acabou") em vez de fluxo contínuo.</summary>
    public int TotalLimit;

    /// <summary>Raio em pixels de um sorteio de posição em volta do spawner. 0 = todos nascem
    /// exatamente em cima dele. Espalhar evita a pilha de inimigos no mesmo pixel.</summary>
    public float Radius;

    /// <summary>Segundos antes da primeira leva. Serve pra dar respiro no começo da fase e pra
    /// escalonar vários spawners que dividem a mesma cena.</summary>
    public float StartDelay;

    /// <summary>
    /// Switch do GameState que liga/desliga este spawner em jogo. Vazio = sempre ativo.
    ///
    /// <para>É o "quando" mais simples e o mais usado: só de noite, só depois do chefe, só na
    /// arena. Condição mais fina (por variável, item ou quest) vai por entrada na tabela de
    /// spawn, onde ela também escolhe QUAL tipo nasce, não só se nasce.</para>
    /// </summary>
    public string RequiredSwitch = "";

    /// <summary>Estado esperado do <see cref="RequiredSwitch"/>. False inverte: nasce enquanto o
    /// switch estiver DESLIGADO.</summary>
    public bool RequiredSwitchOn = true;

    /// <summary>Quantos já nasceram desde o começo da cena.</summary>
    public int TotalSpawned { get; private set; }

    /// <summary>Quantos filhos deste spawner estão vivos agora.</summary>
    public int AliveCount => _spawned.Count;

    // Ids em vez de Entity: id morto é detectável (IsAlive) e não segura nada vivo. Guardado por
    // spawner, não global, pra dois ninhos na mesma cena terem tetos independentes.
    private readonly List<int> _spawned = [];
    private float _timer;
    private readonly Random _random = new();

    public override void Start() => _timer = -StartDelay;

    public override void Update(float deltaTime)
    {
        _spawned.RemoveAll(id => World?.IsAlive(id) != true);

        if (Prefab.Length == 0 || World is null)
            return;

        // Fora da condição o relógio nem corre: sem isso, um spawner desligado por meia hora
        // despejaria a leva inteira de uma vez no instante em que o switch ligasse.
        if (RequiredSwitch.Length > 0
            && (World.State?.GetSwitch(RequiredSwitch) ?? false) != RequiredSwitchOn)
            return;

        _timer += deltaTime;
        if (_timer < Interval)
            return;

        _timer = 0f;

        if (TotalLimit > 0 && TotalSpawned >= TotalLimit)
            return;

        if (MaxAlive > 0 && _spawned.Count >= MaxAlive)
            return;

        var origin = Get<Transform>()?.Position ?? Vector2.Zero;

        if (World.Spawn(Prefab, origin + RandomOffset()) is { } spawned)
        {
            _spawned.Add(spawned.Id);
            TotalSpawned++;
        }
    }

    private Vector2 RandomOffset()
    {
        if (Radius <= 0f)
            return Vector2.Zero;

        // Ângulo uniforme com raio pela raiz: sortear o raio direto amontoa tudo no centro,
        // porque a área de um anel cresce com a distância.
        float angle = (float)_random.NextDouble() * MathF.Tau;
        float distance = Radius * MathF.Sqrt((float)_random.NextDouble());
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
    }
}
