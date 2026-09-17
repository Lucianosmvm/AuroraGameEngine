using Aurora.Runtime.Ecs;
using Aurora.Runtime.Scenes;

namespace CaminhosDaFe;

/// <summary>
/// Personagem que conversa. A fala depende do ponto da história, então o texto não mora aqui:
/// o script só avisa a <see cref="Missao"/> quem foi procurado, e ela decide o que dizer.
/// </summary>
[SceneScript]
public sealed class Npc : Behavior, IInteracao
{
    /// <summary>Chave que a Missao reconhece ("jesse").</summary>
    public string Id = "jesse";

    public void Interagir(Entity quem) => Missao.FalarCom(Id, World);
}
