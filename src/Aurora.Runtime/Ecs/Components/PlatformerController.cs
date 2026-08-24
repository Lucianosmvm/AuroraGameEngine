using System.Numerics;
using Aurora.Runtime.UI;
using Silk.NET.Input;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Movimento lateral com gravidade e pulo — o controlador de jogo de plataforma. Anda no eixo X
/// por input, cai sozinho e pula quando está no chão.
///
/// <para>Componente separado do <see cref="TopDownController"/> de propósito: aqui a metade
/// vertical não é input nenhum, é física. Juntar os dois daria um componente com o dobro dos
/// campos em que metade nunca se aplica — e ninguém saberia quais.</para>
///
/// <para>Precisa de <see cref="Collider"/> sólido e não-cinemático: é da colisão que vem a
/// noção de chão. Piso pode ser tilemap com SolidTiles ou qualquer entidade com collider
/// sólido.</para>
/// </summary>
public sealed class PlatformerController : Behavior
{
    /// <summary>Velocidade horizontal máxima, em pixels/s.</summary>
    public float MoveSpeed = 150f;

    /// <summary>Quanto acelera até a velocidade máxima (px/s²). Alto = resposta imediata;
    /// baixo = personagem "pesado" que demora a engrenar.</summary>
    public float Acceleration = 1200f;

    /// <summary>Quanto freia quando ninguém está empurrando (px/s²). Alto = para no lugar;
    /// baixo = derrapa.</summary>
    public float Friction = 1400f;

    /// <summary>Fração do controle horizontal que vale no ar, de 0 a 1. 1 = manobra igual no
    /// chão e no ar; 0 = uma vez no ar, o pulo está decidido.</summary>
    public float AirControl = 0.6f;

    public float Gravity = 1400f;

    /// <summary>Impulso vertical do pulo. A altura sai de <c>JumpSpeed² / (2 · Gravity)</c> —
    /// 420 com gravidade 1400 dá ~63px, uns quatro tiles de 16.</summary>
    public float JumpSpeed = 420f;

    /// <summary>
    /// Quanto da subida sobra ao soltar o botão, de 0 a 1 — o pulo de altura variável. 0.45
    /// corta a subida em menos da metade num toque curto. 1 desliga: todo pulo vai até o topo.
    /// </summary>
    public float JumpCut = 0.45f;

    /// <summary>Teto da velocidade de queda. Sem ele, uma queda longa atravessa o chão entre
    /// dois frames.</summary>
    public float MaxFallSpeed = 600f;

    /// <summary>
    /// Segundos em que o pulo ainda é aceito DEPOIS de sair da beirada. Sem isso, pular no
    /// último instante da plataforma falha e o jogador jura que apertou — é o ajuste que mais
    /// muda a sensação de um plataforma.
    /// </summary>
    public float CoyoteTime = 0.1f;

    /// <summary>Segundos em que um pulo apertado ANTES de tocar o chão fica guardado e sai
    /// assim que aterrissa. O outro lado da mesma moeda.</summary>
    public float JumpBufferTime = 0.12f;

    /// <summary>Tecla de pulo, pelo nome do enum (Space, W, Z…). Vazio = só o botão de toque.</summary>
    public string JumpKey = "Space";

    /// <summary>Lê A/D, setas e o analógico do gamepad no eixo X.</summary>
    public bool UseKeyboard = true;

    /// <summary>Tela de UI com o joystick e o botão de pulo do celular. Vazio = só teclado.</summary>
    public string JoystickScreen = "";
    public string JoystickName = "";
    public string JumpButtonName = "";

    /// <summary>Espelha o sprite conforme o lado pra onde anda.</summary>
    public bool FlipSpriteByDirection = true;

    /// <summary>Parâmetro float do <see cref="Animator"/> com a velocidade horizontal — a
    /// transição parado↔correndo. Vazio = não mexe no Animator.</summary>
    public string AnimatorSpeedParameter = "Speed";

    /// <summary>Parâmetro bool do <see cref="Animator"/> que indica estar no ar — pra trocar pro
    /// clipe de pulo/queda. Vazio = não mexe.</summary>
    public string AnimatorAirborneParameter = "Airborne";

    /// <summary>Velocidade atual (px/s). Leitura pra HUD e script.</summary>
    public Vector2 Velocity => _velocity;

    /// <summary>Se está pisando em algo neste frame.</summary>
    public bool IsGrounded { get; private set; }

    /// <summary>Se o pulo sairia agora (no chão, ou dentro do coyote time).</summary>
    public bool CanJump => _coyote > 0f;

    private Vector2 _velocity;
    private float _coyote;
    private float _jumpBuffer;
    private bool _groundedThisFrame;
    private bool _jumpHeld;

    // O corte de altura vale UMA vez por pulo, na soltada do botão. Reaplicar a cada frame
    // multiplicava a subida por JumpCut repetidamente e o pulo virava um pulinho.
    private bool _jumpCutApplied;

    // Se o pulo saiu com o botão apertado. Pulo pedido por API/IA (RequestJump) não tem botão
    // pra soltar — cortá-lo seria cortar um pulo que ninguém interrompeu.
    private bool _jumpStartedHeld;

    /// <summary>Manda pular de fora (botão de toque customizado, cutscene, IA). Respeita o
    /// coyote time como um pulo normal.</summary>
    public void RequestJump() => _jumpBuffer = JumpBufferTime;

    public override void Update(float deltaTime)
    {
        if (Get<Transform>() is not { } transform || World is null)
            return;

        // O chão vem do OnCollision, que roda DEPOIS do Update dos behaviors. Então o que se lê
        // aqui é o estado do frame passado — e é por isso que o coyote time não é luxo: sem ele,
        // o atraso de um frame já comeria pulos legítimos na beirada.
        IsGrounded = _groundedThisFrame;
        _groundedThisFrame = false;

        ReadJumpInput();
        Move(transform, deltaTime, ReadHorizontal());
        ApplyGravity(deltaTime);
        TryJump(deltaTime);

        transform.Position += _velocity * deltaTime;

        UpdateAnimator();
    }

    public override void OnCollision(Entity other, CollisionInfo info)
    {
        // Normal apontando pra cima (Y negativo, tela) = piso sob os pés. Bater a cabeça no teto
        // ou raspar numa parede não conta como chão, senão dava pulo infinito na parede.
        if (info.Normal.Y < -0.5f)
        {
            _groundedThisFrame = true;
            if (_velocity.Y > 0f)
                _velocity.Y = 0f;
        }
        else if (info.Normal.Y > 0.5f && _velocity.Y < 0f)
        {
            _velocity.Y = 0f;   // teto: corta a subida em vez de deixar grudar
        }
    }

    private float ReadHorizontal()
    {
        if (JoystickName.Length > 0
            && World!.UI?.Find<UiJoystick>(JoystickScreen, JoystickName)?.Value is { } stick
            && MathF.Abs(stick.X) > 0.001f)
            return Math.Clamp(stick.X, -1f, 1f);

        if (UseKeyboard && World!.Input is { } input)
            return input.AxisX;

        return 0f;
    }

    private void ReadJumpInput()
    {
        bool pressed = false;
        bool held = false;

        if (JumpKey.Length > 0 && World!.Input is { } input
            && Enum.TryParse<Key>(JumpKey, ignoreCase: true, out var key))
        {
            pressed = input.WasKeyPressed(key);
            held = input.IsKeyDown(key);
        }

        if (JumpButtonName.Length > 0
            && World!.UI?.Find<UiButton>(JoystickScreen, JumpButtonName) is { } button)
        {
            pressed |= button.Clicked;
            held |= button.Pressed;
        }

        if (pressed)
            _jumpBuffer = JumpBufferTime;

        _jumpHeld = held;
    }

    private void Move(Transform transform, float deltaTime, float axis)
    {
        float control = IsGrounded ? 1f : Math.Clamp(AirControl, 0f, 1f);

        if (MathF.Abs(axis) > 0.001f)
        {
            _velocity.X = MoveTowards(_velocity.X, axis * MoveSpeed, Acceleration * control * deltaTime);

            if (FlipSpriteByDirection && Get<SpriteRenderer>() is { } sprite)
                sprite.FlipX = axis < 0f;
        }
        else
        {
            _velocity.X = MoveTowards(_velocity.X, 0f, Friction * control * deltaTime);
        }
    }

    private void ApplyGravity(float deltaTime)
    {
        _velocity.Y = MathF.Min(_velocity.Y + Gravity * deltaTime, MaxFallSpeed);
    }

    private void TryJump(float deltaTime)
    {
        _coyote = IsGrounded ? CoyoteTime : MathF.Max(0f, _coyote - deltaTime);
        _jumpBuffer = MathF.Max(0f, _jumpBuffer - deltaTime);

        if (_jumpBuffer > 0f && _coyote > 0f)
        {
            _velocity.Y = -JumpSpeed;
            _jumpBuffer = 0f;
            _coyote = 0f;
            _jumpCutApplied = false;
            _jumpStartedHeld = _jumpHeld;
            return;
        }

        // Soltou o botão no meio da subida: corta o resto uma vez só. É o que dá pulo curto e
        // pulo longo com a mesma tecla.
        if (_jumpStartedHeld && !_jumpCutApplied && !_jumpHeld && _velocity.Y < 0f && JumpCut < 1f)
        {
            _velocity.Y *= JumpCut;
            _jumpCutApplied = true;
        }
    }

    private void UpdateAnimator()
    {
        if (Get<Animator>() is not { } animator)
            return;

        if (AnimatorSpeedParameter.Length > 0)
            animator.SetFloat(AnimatorSpeedParameter, MathF.Abs(_velocity.X));

        if (AnimatorAirborneParameter.Length > 0)
            animator.SetBool(AnimatorAirborneParameter, !IsGrounded);
    }

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        float diff = target - current;
        return MathF.Abs(diff) <= maxDelta ? target : current + MathF.Sign(diff) * maxDelta;
    }
}
