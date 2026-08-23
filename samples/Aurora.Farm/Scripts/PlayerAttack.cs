using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;
using Silk.NET.Input;

namespace AuroraFarm;

/// <summary>
/// Ataque no clique do mouse, com o efeito nascendo na direção em que o jogador está virado.
///
/// <para>São três passos: (1) descobrir a direção — mira do mouse convertida pra mundo com
/// <c>World.Camera.ScreenToWorld</c>, com o movimento do teclado como reserva; (2) instanciar
/// uma entidade nova com <c>SpriteRenderer</c> + <c>Animator</c> à frente do jogador, girada
/// pro ângulo da direção; (3) o <see cref="AttackEffect"/> apaga essa entidade quando a
/// animação acaba.</para>
///
/// <para>Passo a passo comentado: <c>docs/TUTORIAL-SCRIPTS-PLAYER.md</c>.</para>
/// </summary>
[SceneScript]
public sealed class PlayerAttack : Behavior
{
    /// <summary>Sprite sheet do efeito: uma linha de quadros quadrados, apontando pra DIREITA
    /// (a rotação 0 do Transform). Carregado por <c>World.Assets</c>, que cacheia por caminho.</summary>
    public string EffectTexture = "sprites/slash.png";

    /// <summary>Segundos entre um ataque e o próximo.</summary>
    public float Cooldown = 0.35f;

    /// <summary>Distância do centro do jogador até o centro do efeito, em pixels.</summary>
    public float Reach = 26f;

    /// <summary>Lado do efeito desenhado, em pixels de mundo.</summary>
    public float EffectSize = 52f;

    /// <summary>Duração de cada quadro da animação.</summary>
    public float FrameDuration = 0.05f;

    /// <summary>Camada de desenho do efeito — acima do jogador (que na fazenda está na 10).</summary>
    public int EffectLayer = 11;

    /// <summary>Quadros do sheet. 0 = descobre sozinho (largura ÷ altura, quadros quadrados).</summary>
    public int EffectFrames;

    /// <summary>True: a direção é arredondada pras 8 direções, como RPG clássico.
    /// False: ângulo livre, o corte aponta exatamente pro cursor.</summary>
    public bool SnapToEightDirections;

    /// <summary>Dano em quem tem <c>Health</c> dentro do alcance. 0 = ataque só visual.</summary>
    public float Damage;

    /// <summary>Raio da área de dano ao redor do ponto do efeito.</summary>
    public float DamageRadius = 34f;

    private float _cooldownTimer;

    // Direção em que o jogador está virado. Começa pra baixo, convenção de top-down.
    private Vector2 _facing = new(0f, 1f);

    public override void Update(float deltaTime)
    {
        _cooldownTimer -= deltaTime;

        var input = World?.Input;
        var transform = Get<Transform>();
        if (World is null || input is null || transform is null)
            return;

        // 1) Enquanto anda, a direção do movimento é a direção pra onde o jogador olha.
        var move = new Vector2(input.AxisX, input.AxisY);
        if (move.LengthSquared() > 0.0001f)
            _facing = Vector2.Normalize(move);

        if (!input.WasMouseClicked(MouseButton.Left) || _cooldownTimer > 0f)
            return;

        // 2) No clique, a mira do mouse manda: o corte sai na direção do cursor.
        if (AimFromMouse(transform.Position) is { } aim)
            _facing = aim;

        _cooldownTimer = Cooldown;
        SpawnEffect(transform.Position);

        if (Damage > 0f)
            HitAround(transform.Position + _facing * Reach);
    }

    /// <summary>
    /// Direção do jogador até o cursor, em coordenadas de MUNDO. O mouse vem em pixel de tela;
    /// sem o <c>ScreenToWorld</c> da câmera o ataque sairia errado assim que a câmera saísse
    /// da origem ou o zoom mudasse. Null quando o cursor está em cima do jogador (direção
    /// indefinida — aí mantém a última).
    /// </summary>
    private Vector2? AimFromMouse(Vector2 origin)
    {
        if (World?.Camera is not { } camera || World.Input is not { } input)
            return null;

        var target = camera.ScreenToWorld(input.MousePosition);
        var direction = target - origin;
        if (direction.LengthSquared() < 0.01f)
            return null;

        direction = Vector2.Normalize(direction);
        return SnapToEightDirections ? Snap(direction) : direction;
    }

    /// <summary>Arredonda o vetor pro múltiplo de 45° mais próximo.</summary>
    private static Vector2 Snap(Vector2 direction)
    {
        float step = MathF.PI / 4f;
        float angle = MathF.Round(MathF.Atan2(direction.Y, direction.X) / step) * step;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    /// <summary>Cria a entidade do efeito à frente do jogador, girada pra direção do ataque.</summary>
    private void SpawnEffect(Vector2 origin)
    {
        var texture = World!.Assets?.LoadTexture(EffectTexture);
        if (texture is null)
            return;

        // Sheet de uma linha só com quadros quadrados: a altura é o lado do quadro.
        int frameSize = texture.Height;
        int frames = EffectFrames > 0 ? EffectFrames : Math.Max(1, texture.Width / Math.Max(1, frameSize));

        var effect = World.CreateEntity("AttackEffect");

        // Rotation em radianos, 0 = apontando pra direita — a mesma convenção do sheet.
        effect.Add(new Transform(origin + _facing * Reach)
        {
            Rotation = MathF.Atan2(_facing.Y, _facing.X),
        });

        effect.Add(new SpriteRenderer(texture, EffectLayer)
        {
            Size = new Vector2(EffectSize, EffectSize),
        });

        effect.Add(new Animator
        {
            FrameWidth = frameSize,
            FrameHeight = frameSize,
            SheetColumns = frames,
            Clips =
            [
                new AnimationClip
                {
                    Name = "attack",
                    Frames = Enumerable.Range(0, frames).ToArray(),
                    FrameDuration = FrameDuration,
                    Loop = false,          // sem isso o corte ficaria piscando pra sempre
                },
            ],
        });

        // Gruda no jogador: sem isso, andar durante o golpe deixa o corte pra trás.
        effect.Add(new AttackEffect
        {
            Owner = Entity,
            Offset = _facing * Reach,
        });
    }

    /// <summary>Dano em todo mundo com Health dentro do raio (menos o próprio atacante).</summary>
    private void HitAround(Vector2 center)
    {
        foreach (var (target, _) in World!.Query<Health>())
        {
            if (target.Id == Entity.Id)
                continue;

            if (target.Get<Transform>() is { } targetTransform
                && Vector2.Distance(targetTransform.Position, center) <= DamageRadius)
            {
                World.Damage(target, Damage, Entity);
            }
        }
    }
}
