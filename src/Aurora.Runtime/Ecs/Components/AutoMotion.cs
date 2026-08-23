using System.Numerics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Movimento decorativo constante: gira e/ou balança sozinho, sem input e sem física. É a
/// moeda que roda, o item que flutua, o portal que gira, a placa que balança.
///
/// <para>Os dois efeitos são independentes e somam. O balanço é medido a partir da posição em
/// que a entidade nasceu, então mover a entidade pela cena (ou um <see cref="FollowTarget"/>
/// junto) não é compatível — pra oscilar seguindo alguém, ponha o AutoMotion num filho.</para>
/// </summary>
public sealed class AutoMotion : Behavior
{
    /// <summary>Graus por segundo. Positivo gira num sentido, negativo no outro, 0 não gira.
    /// Em graus (e não radianos como <c>Transform.Rotation</c>) porque é o que se autora à mão
    /// numa cena: 360 é uma volta por segundo, e isso se lê de imediato.</summary>
    public float RotateSpeedDegrees;

    /// <summary>Deslocamento máximo do balanço, em pixels. 0 = sem balanço.</summary>
    public float BobAmplitude;

    /// <summary>Ciclos completos por segundo do balanço.</summary>
    public float BobSpeed = 1f;

    /// <summary>Direção do balanço em graus: 90 (padrão) sobe e desce, 0 vai e vem na
    /// horizontal.</summary>
    public float BobAngleDegrees = 90f;

    private Vector2 _origin;
    private float _phase;
    private bool _hasOrigin;

    public override void Start()
    {
        if (Get<Transform>() is { } transform)
        {
            _origin = transform.Position;
            _hasOrigin = true;
        }
    }

    public override void Update(float deltaTime)
    {
        var transform = Get<Transform>();
        if (transform is null)
            return;

        if (RotateSpeedDegrees != 0f)
            transform.Rotation += RotateSpeedDegrees * (MathF.PI / 180f) * deltaTime;

        if (BobAmplitude == 0f || !_hasOrigin)
            return;

        _phase += BobSpeed * deltaTime;

        float angle = BobAngleDegrees * (MathF.PI / 180f);
        var axis = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

        // Posição absoluta a partir da origem, não incremento: somar deslocamento a cada frame
        // acumula erro de ponto flutuante e a entidade vai derivando pra longe com o tempo.
        transform.Position = _origin + axis * (MathF.Sin(_phase * MathF.Tau) * BobAmplitude);
    }
}
