using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;

namespace AuroraFarm;

/// <summary>
/// Vive numa entidade "Crop" plantada por <see cref="PlayerFarmer"/>: conta o tempo até
/// crescer e troca o sprite de muda pra plantação madura. Não tem estágio intermediário —
/// só dois sprites (recorte de docs/rtp.jpeg) — dá pra continuar adicionando mais estágios
/// trocando a lista de texturas por índice de crescimento em vez de bool.
/// </summary>
public sealed class CropPlot : Behavior
{
    public float GrowSeconds = 8f;
    public Texture2D? GrownTexture;

    /// <summary>True quando pronta pra colher (lido por PlayerFarmer).</summary>
    public bool Ready { get; private set; }

    private float _timer;

    public override void Update(float deltaTime)
    {
        if (Ready)
            return;

        _timer += deltaTime;
        if (_timer < GrowSeconds)
            return;

        Ready = true;

        var sprite = Get<SpriteRenderer>();
        if (sprite is not null && GrownTexture is not null)
        {
            sprite.Texture = GrownTexture;
            sprite.Size = new System.Numerics.Vector2(52f, 42f);
        }
    }
}
