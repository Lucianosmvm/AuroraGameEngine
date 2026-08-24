using System.Numerics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Perambula sozinho em volta de onde nasceu, com pausas. É o cavalo pastando enquanto ninguém
/// o monta, a galinha do quintal, o aldeão andando pela praça, o bicho ambiente da floresta.
///
/// <para>Diferente do <see cref="PatrolPath"/>, que percorre pontos que você marcou: aqui o
/// destino é sorteado, então dez cópias do mesmo prefab não andam em sincronia como um pelotão.
/// E diferente do <see cref="NavAgent"/> com Follow, que persegue alguém — este não tem alvo.</para>
///
/// <para>Se a entidade tiver um <see cref="NavAgent"/>, ele é usado pra andar e o bicho contorna
/// parede; sem NavAgent, anda reto. Vale a pena saber qual dos dois você tem: um cavalo solto num
/// mapa com tilemap sólido quer o NavAgent, uma borboleta não.</para>
/// </summary>
public sealed class Wander : Behavior
{
    /// <summary>Raio em pixels em volta do ponto onde nasceu. O bicho nunca se afasta mais que
    /// isso — é o que impede a galinha de atravessar o mapa em dez minutos.</summary>
    public float Radius = 80f;

    /// <summary>Velocidade ao andar, em pixels/s. Ignorado quando há <see cref="NavAgent"/>:
    /// lá quem manda na velocidade é o próprio agente.</summary>
    public float Speed = 40f;

    /// <summary>Menor e maior pausa entre uma caminhada e a próxima, em segundos. Pausa é o que
    /// faz parecer bicho e não robô de patrulha.</summary>
    public float PauseMin = 1f;
    public float PauseMax = 4f;

    /// <summary>Distância pra considerar o destino alcançado.</summary>
    public float ArriveThreshold = 4f;

    /// <summary>Espelha o sprite conforme o lado pra onde anda.</summary>
    public bool FlipSpriteByDirection = true;

    /// <summary>Parâmetro float do <see cref="Animator"/> com a velocidade atual — a transição
    /// parado↔andando. Vazio = não mexe no Animator.</summary>
    public string AnimatorSpeedParameter = "Speed";

    /// <summary>Se está andando agora (false durante a pausa).</summary>
    public bool IsMoving { get; private set; }

    private Vector2 _home;
    private Vector2 _target;
    private float _pauseTimer;
    private bool _hasHome;
    private readonly Random _random = new();

    public override void Start()
    {
        if (Get<Transform>() is { } transform)
        {
            _home = transform.Position;
            _hasHome = true;
        }

        // Começa em pausa, com tempo sorteado: sem isso, dez bichos que nasceram no mesmo frame
        // dão o primeiro passo exatamente juntos e a cena parece coreografada.
        _pauseTimer = RandomPause();
        _target = _home;
    }

    public override void Update(float deltaTime)
    {
        if (Get<Transform>() is not { } transform || !_hasHome)
            return;

        var agent = Get<NavAgent>();

        if (_pauseTimer > 0f)
        {
            _pauseTimer -= deltaTime;
            IsMoving = false;

            if (_pauseTimer <= 0f)
                PickNewTarget(agent);

            ReportSpeed(0f);
            return;
        }

        IsMoving = true;

        if (agent is not null)
        {
            // Com NavAgent quem anda é o World: aqui só se decide pra onde e quando parou.
            if (!agent.HasTarget)
                _pauseTimer = RandomPause();

            ReportSpeed(agent.HasTarget ? agent.Speed : 0f);
            return;
        }

        var delta = _target - transform.Position;
        float distance = delta.Length();

        if (distance <= ArriveThreshold)
        {
            _pauseTimer = RandomPause();
            ReportSpeed(0f);
            return;
        }

        var direction = delta / distance;
        transform.Position += direction * MathF.Min(Speed * deltaTime, distance);

        if (FlipSpriteByDirection && MathF.Abs(direction.X) > 0.001f && Get<SpriteRenderer>() is { } sprite)
            sprite.FlipX = direction.X < 0f;

        ReportSpeed(Speed);
    }

    /// <summary>Manda parar onde está e esperar. É o que o <see cref="Rideable"/> chama ao ser
    /// montado — sem isso o cavalo tentaria pastar enquanto você o cavalga.</summary>
    public void Halt()
    {
        _pauseTimer = RandomPause();
        IsMoving = false;
        Get<NavAgent>()?.Stop();
    }

    /// <summary>
    /// Passa a perambular em volta de onde está AGORA, em vez de onde nasceu.
    ///
    /// <para>Existe porque o lar é fixado no Start, e qualquer coisa que carregue a entidade pra
    /// longe (o jogador cavalgando, um teleporte, uma cutscene) deixaria o bicho voltando a pé
    /// pro ponto de nascimento — atravessando o mapa inteiro pra "pastar" onde ninguém está. O
    /// <see cref="Rideable"/> chama isto ao desmontar.</para>
    /// </summary>
    public void ResetHome()
    {
        if (Get<Transform>() is { } transform)
        {
            _home = transform.Position;
            _hasHome = true;
        }

        _target = _home;
    }

    private void PickNewTarget(NavAgent? agent)
    {
        // Ângulo uniforme com raio pela raiz: sortear o raio direto amontoa os destinos perto do
        // centro, porque a área de um anel cresce com a distância.
        float angle = (float)_random.NextDouble() * MathF.Tau;
        float distance = Radius * MathF.Sqrt((float)_random.NextDouble());

        _target = _home + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;

        agent?.SetTarget(_target);
    }

    private float RandomPause()
    {
        float min = MathF.Max(0f, PauseMin);
        float max = MathF.Max(min, PauseMax);
        return min + (float)_random.NextDouble() * (max - min);
    }

    private void ReportSpeed(float speed)
    {
        if (AnimatorSpeedParameter.Length > 0)
            Get<Animator>()?.SetFloat(AnimatorSpeedParameter, speed);
    }
}
