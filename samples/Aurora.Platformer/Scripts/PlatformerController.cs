using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;
using Silk.NET.Input;

namespace Aurora.Platformer;

/// <summary>
/// Movimentação lateral + pulo com gravidade, resolvendo colisão contra o tilemap e contra
/// qualquer <see cref="Collider"/> sólido da cena.
///
/// <para>Como o passo de colisão funciona aqui: o <see cref="World"/> roda TODOS os
/// <see cref="Behavior.Update"/> primeiro e só depois resolve colisões, empurrando quem está
/// sobreposto para fora e chamando <see cref="OnCollision"/>. Ou seja: este script integra
/// posição livremente e usa o callback só para zerar a velocidade e descobrir se há chão
/// embaixo. Por isso <see cref="_grounded"/> é zerado no FIM do Update — o passo de colisão
/// que vem logo em seguida marca de novo se ainda estivermos apoiados.</para>
///
/// <para>Convenção de eixos da engine: Y cresce para BAIXO. Gravidade é +Y, pulo é -Y, e um
/// <c>info.Normal.Y == -1</c> ("fui empurrado para cima") significa chão sob os pés.</para>
/// </summary>
[SceneScript]
public sealed class PlatformerController : Behavior
{
    /// <summary>Velocidade horizontal máxima, em pixels por segundo.</summary>
    public float MoveSpeed = 150f;

    /// <summary>Quão rápido chega na MoveSpeed (px/s²). Alto = controle "seco", baixo = patinando.</summary>
    public float Acceleration = 1200f;

    /// <summary>Desaceleração quando não há input horizontal (px/s²).</summary>
    public float Friction = 1400f;

    /// <summary>Fração de Acceleration/Friction aplicada no ar (0 = sem controle aéreo).</summary>
    public float AirControl = 0.6f;

    public float Gravity = 1400f;

    /// <summary>Impulso vertical do pulo. Altura ≈ JumpSpeed² / (2 * Gravity) — 420/1400 dá ~63 px (4 tiles).</summary>
    public float JumpSpeed = 420f;

    /// <summary>Soltar o botão no meio da subida corta a velocidade para esta fração (pulo variável).</summary>
    public float JumpCut = 0.45f;

    /// <summary>Teto da queda. Mantenha abaixo de (tamanho do tile / maior deltaTime) para não
    /// atravessar plataforma fina: 600 px/s com o teto de dt de 1/45 s dá ~13 px por frame,
    /// menos que um tile de 16.</summary>
    public float MaxFallSpeed = 600f;

    /// <summary>Janela em que ainda dá para pular depois de sair da beirada (coyote time).</summary>
    public float CoyoteTime = 0.10f;

    /// <summary>Janela em que um pulo apertado antes de encostar no chão continua valendo.</summary>
    public float JumpBufferTime = 0.12f;

    /// <summary>Y a partir do qual o jogador caiu no vazio e volta ao spawn.</summary>
    public float FallLimitY = 600f;

    /// <summary>
    /// Eixo horizontal externo (-1..1), somado ao teclado/analógico: joystick na tela (Android),
    /// cutscene, IA, teste automatizado. É um valor mantido, não um pulso — quem escreve aqui
    /// zera quando o dedo sai do botão.
    /// </summary>
    public float ExternalAxis;

    private Vector2 _velocity;
    private Vector2 _spawn;
    private bool _grounded;
    private float _coyote;
    private float _jumpBuffer;
    private bool _jumpCutArmed;

    /// <summary>Velocidade atual (HUD, animação, efeitos).</summary>
    public Vector2 Velocity => _velocity;

    /// <summary>True enquanto o pulo ainda é permitido (no chão ou dentro do coyote time).</summary>
    public bool CanJump => _coyote > 0f;

    /// <summary>Ponto de nascimento — a posição em que a entidade estava ao carregar a cena.</summary>
    public Vector2 SpawnPoint => _spawn;

    public override void Start()
        => _spawn = Get<Transform>()?.Position ?? Vector2.Zero;

    /// <summary>Agenda um pulo como se o botão tivesse sido apertado agora (botão de toque na
    /// tela, cutscene, teste automatizado). Respeita coyote time e buffer como o input normal.</summary>
    public void RequestJump() => _jumpBuffer = JumpBufferTime;

    public override void Update(float deltaTime)
    {
        var transform = Get<Transform>();
        if (transform is null)
            return;

        var input = World?.Input;

        // --- 1. input -------------------------------------------------------
        // AxisX já soma A/D, setas e analógico esquerdo do controle.
        float axis = Math.Clamp((input?.AxisX ?? 0f) + ExternalAxis, -1f, 1f);
        bool jumpPressed = input is not null
            && (input.WasKeyPressed(Key.Space) || input.WasKeyPressed(Key.W) || input.WasKeyPressed(Key.Up)
                || input.WasGamepadButtonPressed(ButtonName.A));
        bool jumpHeld = input is not null
            && (input.IsKeyDown(Key.Space) || input.IsKeyDown(Key.W) || input.IsKeyDown(Key.Up)
                || input.IsGamepadButtonDown(ButtonName.A));

        // --- 2. temporizadores ----------------------------------------------
        // _grounded vem do passo de colisão do frame anterior (ver comentário da classe).
        _coyote = _grounded ? CoyoteTime : MathF.Max(0f, _coyote - deltaTime);
        _jumpBuffer = jumpPressed ? JumpBufferTime : MathF.Max(0f, _jumpBuffer - deltaTime);

        // --- 3. movimento horizontal ----------------------------------------
        float target = axis * MoveSpeed;
        float rate = MathF.Abs(target) > 0.01f ? Acceleration : Friction;
        if (!_grounded)
            rate *= AirControl;
        _velocity.X = MoveTowards(_velocity.X, target, rate * deltaTime);

        // --- 4. pulo ---------------------------------------------------------
        if (_jumpBuffer > 0f && _coyote > 0f)
        {
            _velocity.Y = -JumpSpeed;
            _jumpBuffer = 0f;
            _coyote = 0f;
            _grounded = false;

            // O corte só vale para pulo dado pelo botão: assim um pulo vindo de
            // RequestJump (botão de toque, cutscene, teste) sai inteiro em vez de nascer
            // já cortado por "o teclado não está pressionado".
            _jumpCutArmed = jumpHeld;
        }

        // Pulo variável: soltar o botão subindo limita a velocidade restante (idempotente —
        // aplicar todo frame não vai zerando o pulo, só mantém o teto).
        if (_jumpCutArmed && !jumpHeld && _velocity.Y < -JumpSpeed * JumpCut)
        {
            _velocity.Y = -JumpSpeed * JumpCut;
            _jumpCutArmed = false;
        }

        // --- 5. gravidade e integração ---------------------------------------
        _velocity.Y = MathF.Min(_velocity.Y + Gravity * deltaTime, MaxFallSpeed);
        transform.Position += _velocity * deltaTime;

        // --- 6. sprite --------------------------------------------------------
        if (MathF.Abs(_velocity.X) > 1f && Get<SpriteRenderer>() is { } sprite)
            sprite.FlipX = _velocity.X < 0f;

        // --- 7. caiu no vazio --------------------------------------------------
        if (transform.Position.Y > FallLimitY)
            Respawn();

        // --- 8. o passo de colisão (logo após este Update) remarca se houver chão -----
        _grounded = false;
    }

    /// <summary>
    /// Chamado pelo World depois de empurrar a entidade para fora de um sólido — tilemap ou
    /// outro Collider. <paramref name="info"/>.Normal aponta para fora do outro corpo.
    /// </summary>
    public override void OnCollision(Entity other, CollisionInfo info)
    {
        if (info.Normal.Y < -0.5f)
        {
            // Empurrado para cima = tem chão embaixo.
            _grounded = true;
            if (_velocity.Y > 0f)
                _velocity.Y = 0f;
        }
        else if (info.Normal.Y > 0.5f)
        {
            // Bateu a cabeça: perde a subida, senão gruda no teto até a gravidade vencer.
            if (_velocity.Y < 0f)
                _velocity.Y = 0f;
        }
        else if (MathF.Abs(info.Normal.X) > 0.5f)
        {
            _velocity.X = 0f;
        }
    }

    /// <summary>Volta ao ponto de nascimento zerando a velocidade e conta uma morte.</summary>
    public void Respawn()
    {
        if (Get<Transform>() is { } transform)
            transform.Position = _spawn;

        _velocity = Vector2.Zero;
        _grounded = false;
        _coyote = 0f;
        _jumpBuffer = 0f;

        World?.State?.AddVariable("Deaths", 1);
    }

    private static float MoveTowards(float current, float target, float maxDelta)
        => MathF.Abs(target - current) <= maxDelta ? target : current + MathF.Sign(target - current) * maxDelta;
}
