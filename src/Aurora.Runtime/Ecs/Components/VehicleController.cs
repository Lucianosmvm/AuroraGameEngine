using System.Numerics;
using Aurora.Runtime.UI;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Veículo com volante: acelera na direção pra onde está apontado e vira girando, em vez de
/// andar direto pro lado como um personagem. Serve pro carro visto de cima, pro barco, pro
/// tanque e pra nave estilo Asteroids.
///
/// <para>Carro e nave são o mesmo movimento com uma diferença só — o quanto a velocidade
/// obedece o bico. Por isso são um campo (<see cref="Mode"/>) e não dois componentes: no carro o
/// pneu agarra o chão e ele vai pra onde aponta; na nave não há atrito, então ela continua
/// deslizando pro lado enquanto gira. Essa é literalmente a única conta diferente.</para>
///
/// <para>O <see cref="Transform"/>.Rotation é a direção do bico, e o sprite deve ser desenhado
/// apontando pra DIREITA (ângulo 0) pra bater com ela.</para>
/// </summary>
public sealed class VehicleController : Behavior
{
    /// <summary>
    /// <c>Car</c> (padrão) — o pneu agarra: a velocidade se alinha ao bico na força do
    /// <see cref="Grip"/>, e virar muda pra onde se vai. Carro, barco, tanque.
    /// <c>Ship</c> — sem atrito: girar muda só pra onde se aponta, o movimento continua no
    /// rumo antigo até você acelerar pro outro lado. Nave espacial.
    /// </summary>
    public string Mode = "Car";

    /// <summary>Aceleração ao acelerar, em pixels/s².</summary>
    public float Acceleration = 420f;

    /// <summary>Velocidade máxima pra frente, em pixels/s.</summary>
    public float MaxSpeed = 320f;

    /// <summary>Velocidade máxima de ré (ou de freio, no carro), em pixels/s. 0 = não anda de
    /// ré — o input pra trás vira só freio.</summary>
    public float ReverseSpeed = 120f;

    /// <summary>Quanto gira por segundo, em graus.</summary>
    public float TurnSpeed = 180f;

    /// <summary>
    /// Se o veículo só vira andando. Ligado (padrão no carro) impede o giro parado, que é o que
    /// dá peso de carro em vez de pião. Desligue pra tanque e pra nave.
    /// </summary>
    public bool TurnRequiresMovement = true;

    /// <summary>Desaceleração natural sem acelerador (px/s²) — atrito do chão, resistência do ar.</summary>
    public float Drag = 260f;

    /// <summary>
    /// Só no modo Car: quanto por segundo a velocidade se alinha ao bico, de 0 a 1 por unidade
    /// de tempo. 1 gruda (kart arcade), 0.2 derrapa em curva fechada, 0 vira nave.
    /// </summary>
    public float Grip = 0.9f;

    /// <summary>Lê W/S e setas pro acelerador, A/D pro volante.</summary>
    public bool UseKeyboard = true;

    /// <summary>Tela e nome do <c>UiJoystick</c> de toque: eixo Y acelera, eixo X vira.</summary>
    public string JoystickScreen = "";
    public string JoystickName = "";

    /// <summary>Parâmetro float do <see cref="Animator"/> com a velocidade — pra roda girando,
    /// fogo do motor. Vazio = não mexe.</summary>
    public string AnimatorSpeedParameter = "Speed";

    /// <summary>Velocidade atual em pixels/s.</summary>
    public Vector2 Velocity => _velocity;

    /// <summary>Quanto está andando, com sinal: negativo é ré.</summary>
    public float ForwardSpeed => Vector2.Dot(_velocity, Heading);

    /// <summary>Vetor unitário pra onde o bico aponta.</summary>
    public Vector2 Heading
    {
        get
        {
            float rotation = Get<Transform>()?.Rotation ?? 0f;
            return new Vector2(MathF.Cos(rotation), MathF.Sin(rotation));
        }
    }

    private Vector2 _velocity;

    public override void Update(float deltaTime)
    {
        if (Get<Transform>() is not { } transform || World is null)
            return;

        var (throttle, steer) = ReadInput();

        Steer(transform, steer, deltaTime);
        Accelerate(throttle, deltaTime);
        ApplyGrip(deltaTime);

        transform.Position += _velocity * deltaTime;

        if (AnimatorSpeedParameter.Length > 0)
            Get<Animator>()?.SetFloat(AnimatorSpeedParameter, _velocity.Length());
    }

    private (float Throttle, float Steer) ReadInput()
    {
        if (World!.Dialogue?.ShouldBlockPlayer == true)
            return (0f, 0f);

        if (JoystickName.Length > 0
            && World!.UI?.Find<UiJoystick>(JoystickScreen, JoystickName)?.Value is { } stick
            && stick.LengthSquared() > 0.0001f)
        {
            // Y do joystick é positivo pra baixo; acelerar é empurrar pra cima.
            return (Math.Clamp(-stick.Y, -1f, 1f), Math.Clamp(stick.X, -1f, 1f));
        }

        if (UseKeyboard && World!.Input is { } input)
            return (-input.AxisY, input.AxisX);

        return (0f, 0f);
    }

    private void Steer(Transform transform, float steer, float deltaTime)
    {
        if (MathF.Abs(steer) <= 0.001f)
            return;

        // Volante proporcional à velocidade: parado o carro não gira, devagar gira pouco. Sem
        // isso o carro pivota no lugar, que é o que faz um controle de veículo parecer boneco.
        float authority = 1f;

        if (TurnRequiresMovement)
        {
            if (MaxSpeed <= 0f)
                return;

            authority = MathF.Min(1f, MathF.Abs(ForwardSpeed) / MaxSpeed);
            if (authority <= 0.001f)
                return;
        }

        // Andando de ré o volante inverte, como num carro de verdade.
        float direction = ForwardSpeed < -0.001f ? -1f : 1f;

        transform.Rotation += steer * direction * authority
                              * TurnSpeed * (MathF.PI / 180f) * deltaTime;
    }

    private void Accelerate(float throttle, float deltaTime)
    {
        var heading = Heading;

        if (MathF.Abs(throttle) > 0.001f)
        {
            _velocity += heading * throttle * Acceleration * deltaTime;
        }
        else
        {
            // Sem acelerador o veículo perde velocidade sozinho, até parar de vez.
            float speed = _velocity.Length();
            if (speed > 0.001f)
            {
                float braked = MathF.Max(0f, speed - Drag * deltaTime);
                _velocity = _velocity / speed * braked;
            }
        }

        // Teto separado pra frente e pra ré: ré tem que ser mais lenta, e um teto só não sabe
        // distinguir os dois.
        float forward = Vector2.Dot(_velocity, heading);
        float limit = forward >= 0f ? MaxSpeed : ReverseSpeed;

        if (limit <= 0f && forward < 0f)
        {
            // ReverseSpeed 0 = não anda de ré: o input pra trás vira freio e para em zero.
            _velocity -= heading * forward;
            return;
        }

        if (MathF.Abs(forward) > limit)
            _velocity -= heading * (forward - MathF.Sign(forward) * limit);
    }

    private void ApplyGrip(float deltaTime)
    {
        if (Mode.Equals("Ship", StringComparison.OrdinalIgnoreCase))
            return;   // nave não tem pneu: a inércia lateral é o ponto

        float grip = Math.Clamp(Grip, 0f, 1f);
        if (grip <= 0f)
            return;

        float speed = _velocity.Length();
        if (speed < 0.001f)
            return;

        // GIRA a velocidade até o bico, preservando o módulo. Interpolar pro vetor projetado
        // (heading * forward) parece equivalente e não é: com a velocidade perpendicular ao bico
        // a projeção é ZERO, e o carro frearia até parar numa curva de 90° em vez de virar.
        var heading = Heading;
        var desired = (Vector2.Dot(_velocity, heading) < 0f ? -heading : heading) * speed;

        // Exponencial e não linear pra ficar independente do framerate: com passo linear, um
        // jogo a 30fps derraparia mais que o mesmo a 60.
        float t = 1f - MathF.Exp(-grip * 12f * deltaTime);
        _velocity = Vector2.Lerp(_velocity, desired, t);
    }
}
