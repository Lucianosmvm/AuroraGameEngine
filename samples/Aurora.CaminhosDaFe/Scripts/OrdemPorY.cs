using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace CaminhosDaFe;

/// <summary>
/// Profundidade de visão de cima: quem está mais embaixo na tela é desenhado por cima. Sem isso
/// o Davi passaria "por cima" da copa de uma árvore que está na frente dele.
///
/// <para>A engine ordena por <see cref="SpriteRenderer.Layer"/>, então é só copiar o Y dos pés
/// pra Layer a cada frame. O chão (Tilemap) mora em Layer -10000 e fica sempre atrás.</para>
/// </summary>
[SceneScript]
public sealed class OrdemPorY : Behavior
{
    /// <summary>Distância do centro do sprite até os pés, em pixels. Árvore e casa usam
    /// OriginY = 1 (a posição já é a base), então ficam com 0.</summary>
    public float OffsetY = 8f;

    public override void Start() => Update(0f);

    public override void Update(float deltaTime)
    {
        if (Get<Transform>() is { } transform && Get<SpriteRenderer>() is { } sprite)
            sprite.Layer = (int)MathF.Round(transform.Position.Y + OffsetY);
    }
}
