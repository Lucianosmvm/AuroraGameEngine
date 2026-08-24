using System.Numerics;
using Aurora.Runtime.UI;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Movimento livre por input do jogador — o controlador de jogo visto de cima (RPG, roguelike,
/// survivor). Move o <see cref="Transform"/> direto, sem física: a colisão com o cenário é
/// resolvida pelo <see cref="Collider"/> depois, no mesmo frame.
///
/// <para>Lê teclado e gamepad pelos eixos combinados do <c>InputManager</c> e, quando
/// <see cref="JoystickName"/> aponta pra um <c>UiJoystick</c> de uma tela carregada, o toque
/// tem prioridade — o mesmo componente serve desktop e celular sem trocar nada.</para>
///
/// <para>Não decide o que o jogador FAZ, só como ele anda: ataque é
/// <see cref="AttackSpawner"/>, dano no contato é <see cref="ContactDamage"/>.</para>
/// </summary>
public sealed class TopDownController : Behavior
{
    /// <summary>Pixels de mundo por segundo. A diagonal é normalizada, então andar na diagonal
    /// não é mais rápido que andar reto.</summary>
    public float Speed = 100f;

    /// <summary>
    /// Como a direção é tratada:
    /// <list type="bullet">
    /// <item><c>Free</c> (padrão) — direção contínua. Analógico e joystick de toque entregam
    /// meio-termo, o personagem anda pra qualquer ângulo. É o de survivor/roguelike.</item>
    /// <item><c>EightWay</c> — trava nas 8 direções. Casa com spritesheet de 8 poses e dá o
    /// andar "encaixado" de action-RPG clássico.</item>
    /// <item><c>FourWay</c> — trava nas 4 retas, sem diagonal. É o andar de RPG de grade
    /// (Zelda, Pokémon, RPG Maker): o eixo de maior empurrão vence e o outro zera.</item>
    /// </list>
    ///
    /// <para>Os três são o MESMO movimento com tratamento de direção diferente, por isso são um
    /// campo e não três componentes. Pulo, gravidade e volante são outra física — esses moram no
    /// <see cref="PlatformerController"/> e no <see cref="VehicleController"/>.</para>
    /// </summary>
    public string Movement = "Free";

    /// <summary>Lê WASD/setas e o analógico do gamepad (InputManager.AxisX/AxisY).</summary>
    public bool UseKeyboard = true;

    /// <summary>Tela de UI onde está o joystick de toque. Vazio = sem joystick.</summary>
    public string JoystickScreen = "";

    /// <summary>Nome do <c>UiJoystick</c> dentro da tela. Vazio = sem joystick. Com o dedo no
    /// joystick ele manda; soltou, volta pro teclado.</summary>
    public string JoystickName = "";

    /// <summary>Espelha o sprite ao andar pra esquerda. Desligue se o personagem tem sprites
    /// próprios por direção — aí quem vira é o Animator, por parâmetro.</summary>
    public bool FlipSpriteByDirection = true;

    /// <summary>Parâmetro float do <see cref="Animator"/> alimentado com a velocidade atual em
    /// pixels/s — é o que faz a transição parado↔andando. Vazio = não mexe no Animator.</summary>
    public string AnimatorSpeedParameter = "Speed";

    /// <summary>Última direção não-nula, normalizada: pra onde o personagem está virado. O
    /// <see cref="AttackSpawner"/> em modo Facing mira por aqui. Começa pra baixo, que é a pose
    /// de frente na maioria dos spritesheets top-down.</summary>
    public Vector2 Facing { get; private set; } = new(0f, 1f);

    /// <summary>Velocidade do frame em pixels/s (zero parado). Leitura pra scripts e HUD.</summary>
    public Vector2 Velocity { get; private set; }

    public override void Update(float deltaTime)
    {
        var transform = Get<Transform>();
        if (transform is null || World is null)
            return;

        var move = SnapToMode(ReadInput());

        // Normaliza só o que passa de 1: o joystick entrega magnitude parcial (empurrão leve =
        // passo lento) e isso tem que sobreviver. O teclado entrega 1 por eixo, então a diagonal
        // chega em raiz de 2 — é aqui que ela deixa de ser mais rápida que a reta.
        if (move.LengthSquared() > 1f)
            move = Vector2.Normalize(move);

        // Status de lentidão/pressa entram aqui, num ponto só: multiplicar na velocidade final
        // vale pra teclado e joystick de uma vez, e some sozinho quando o status expira.
        Velocity = move * Speed * (Get<Status>()?.SpeedMultiplier ?? 1f);

        if (move.LengthSquared() > 0.0001f)
        {
            transform.Position += Velocity * deltaTime;
            Facing = Vector2.Normalize(move);

            if (FlipSpriteByDirection && move.X != 0f && Get<SpriteRenderer>() is { } sprite)
                sprite.FlipX = move.X < 0f;
        }

        if (AnimatorSpeedParameter.Length > 0)
            Get<Animator>()?.SetFloat(AnimatorSpeedParameter, Velocity.Length());
    }

    /// <summary>
    /// Aplica o travamento de direção do <see cref="Movement"/>, preservando a intensidade — o
    /// analógico continua andando devagar quando empurrado de leve, mesmo travado nas 4 direções.
    /// </summary>
    private Vector2 SnapToMode(Vector2 move)
    {
        if (move.LengthSquared() <= 0.0001f)
            return move;

        if (Movement.Equals("FourWay", StringComparison.OrdinalIgnoreCase))
        {
            // O eixo de maior empurrão vence e o outro zera: é o que tira a diagonal sem deixar
            // o personagem travado quando os dois eixos estão pressionados.
            return MathF.Abs(move.X) >= MathF.Abs(move.Y)
                ? new Vector2(MathF.Sign(move.X) * MathF.Abs(move.X), 0f)
                : new Vector2(0f, MathF.Sign(move.Y) * MathF.Abs(move.Y));
        }

        if (Movement.Equals("EightWay", StringComparison.OrdinalIgnoreCase))
        {
            float magnitude = MathF.Min(1f, move.Length());
            float step = MathF.Tau / 8f;
            float angle = MathF.Round(MathF.Atan2(move.Y, move.X) / step) * step;
            return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * magnitude;
        }

        return move;   // Free
    }

    private Vector2 ReadInput()
    {
        if (JoystickName.Length > 0
            && World!.UI?.Find<UiJoystick>(JoystickScreen, JoystickName)?.Value is { } stick
            && stick.LengthSquared() > 0.0001f)
        {
            return stick;
        }

        if (UseKeyboard && World!.Input is { } input)
            return new Vector2(input.AxisX, input.AxisY);

        return Vector2.Zero;
    }
}
