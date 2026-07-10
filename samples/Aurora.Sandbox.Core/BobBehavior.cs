using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;

namespace Aurora.Sandbox;

/// <summary>Flutuação vertical senoidal (moedas, itens).</summary>
public sealed class BobBehavior : Behavior
{
    public float Amplitude { get; }
    public float Frequency { get; }
    public float Phase { get; }

    private float _baseY;
    private float _elapsed;

    public BobBehavior(float amplitude = 4f, float frequency = 2f, float phase = 0f)
    {
        Amplitude = amplitude;
        Frequency = frequency;
        Phase = phase;
    }

    public override void Start()
    {
        _baseY = Get<Transform>()!.Position.Y;
    }

    public override void Update(float deltaTime)
    {
        _elapsed += deltaTime;
        var transform = Get<Transform>()!;
        transform.Position.Y = _baseY + MathF.Sin(_elapsed * Frequency + Phase) * Amplitude;
    }
}
