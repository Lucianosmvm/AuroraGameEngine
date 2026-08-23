using System.Numerics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Cola a posição desta entidade na de outra, por nome, com um deslocamento. Serve pro efeito
/// de ataque que acompanha quem atacou, pro companheiro que anda junto, pra barra de vida
/// flutuando sobre o inimigo, pra sombra sob o personagem.
///
/// <para>Diferente do <see cref="NavAgent"/>: aqui não há pathfinding nem desvio de parede, é
/// posição copiada. Pra perseguir contornando o cenário, use NavAgent com <c>Follow</c>.</para>
/// </summary>
public sealed class FollowTarget : Behavior
{
    /// <summary>Nome da entidade seguida. Resolvido a cada frame, então funciona mesmo que o
    /// alvo nasça depois desta entidade.</summary>
    public string TargetName = "Player";

    public float OffsetX;
    public float OffsetY;

    /// <summary>Pixels por segundo de aproximação. 0 = gruda instantâneo (efeito preso ao dono).
    /// Valores altos dão um atraso tipo câmera; baixos, um companheiro que fica pra trás.</summary>
    public float FollowSpeed;

    /// <summary>Se destrói junto quando o alvo some. Ligue em efeito preso ao dono: sem isso ele
    /// fica parado no ar quando quem o lançou morre.</summary>
    public bool DestroyWhenTargetGone;

    /// <summary>Deslocamento em relação ao alvo. Vem dos campos OffsetX/OffsetY da cena, ou é
    /// setado no spawn por código — o <see cref="AttackSpawner"/> usa isso pra jogar o efeito na
    /// direção do golpe, que muda a cada ataque e por isso não caberia no arquivo.</summary>
    public Vector2 Offset
    {
        get => new(OffsetX, OffsetY);
        set => (OffsetX, OffsetY) = (value.X, value.Y);
    }

    public override void Update(float deltaTime)
    {
        var mine = Get<Transform>();
        if (mine is null || World is null)
            return;

        if (!World.TryFind(TargetName, out var target) || target.Get<Transform>() is not { } targetTransform)
        {
            if (DestroyWhenTargetGone)
                Entity.Destroy();
            return;
        }

        var goal = targetTransform.Position + Offset;

        if (FollowSpeed <= 0f)
        {
            mine.Position = goal;
            return;
        }

        // Passo de tamanho fixo em direção ao alvo, limitado pelo que falta: sem esse Min, um
        // FollowSpeed alto passa do ponto e a entidade fica tremendo em volta do alvo.
        var delta = goal - mine.Position;
        float distance = delta.Length();
        if (distance <= 0.0001f)
            return;

        mine.Position += delta / distance * MathF.Min(FollowSpeed * deltaTime, distance);
    }
}
