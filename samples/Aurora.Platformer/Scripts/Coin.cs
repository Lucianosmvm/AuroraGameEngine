using Aurora.Runtime.Ecs;
using Aurora.Runtime.Scenes;

namespace Aurora.Platformer;

/// <summary>
/// Moeda coletável. A entidade tem um Collider com <c>IsSolid: false</c> (trigger, não empurra
/// o jogador) e <c>IsKinematic: true</c> — sem o kinematic o tilemap empurraria a moeda para
/// fora dos tiles sólidos, porque a resolução contra tilemap move todo collider não-cinemático.
/// </summary>
[SceneScript]
public sealed class Coin : Behavior
{
    /// <summary>Quanto soma na variável global "Coins".</summary>
    public int Value = 1;

    public override void OnTriggerEnter(Entity other)
    {
        // Só o jogador coleta — qualquer outro trigger que passe por aqui é ignorado.
        if (other.Get<PlatformerController>() is null)
            return;

        World?.State?.AddVariable("Coins", Value);
        Entity.Destroy();
    }
}
