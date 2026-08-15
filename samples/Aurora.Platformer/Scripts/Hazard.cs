using Aurora.Runtime.Ecs;
using Aurora.Runtime.Scenes;

namespace Aurora.Platformer;

/// <summary>
/// Espinho (ou qualquer zona de morte): encostou, o jogador volta ao spawn.
/// Mesma configuração de collider da <see cref="Coin"/> — trigger cinemático.
/// </summary>
[SceneScript]
public sealed class Hazard : Behavior
{
    public override void OnTriggerEnter(Entity other)
        => other.Get<PlatformerController>()?.Respawn();
}
