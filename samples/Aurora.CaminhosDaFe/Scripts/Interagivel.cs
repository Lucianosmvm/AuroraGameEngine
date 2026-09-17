using Aurora.Runtime.Ecs;
using Aurora.Runtime.Scenes;

namespace CaminhosDaFe;

/// <summary>Quem reage ao [E] do Davi. Implementado por scripts da mesma entidade que tem
/// <see cref="Interagivel"/> (NPC, ovelha, e o que vier: baú, placa, poço).</summary>
public interface IInteracao
{
    void Interagir(Entity quem);
}

/// <summary>
/// Marca a entidade como "dá pra apertar E aqui". Só guarda o alcance e o verbo que a dica
/// mostra; o que acontece fica num script da própria entidade que implementa
/// <see cref="IInteracao"/>. O Davi escolhe o interagível ativo mais próximo dos pés.
/// </summary>
[SceneScript]
public sealed class Interagivel : Behavior
{
    /// <summary>Distância máxima (pixels de mundo) entre os pés do Davi e esta entidade.</summary>
    public float Raio = 22f;

    /// <summary>Palavra da dica flutuante: "[E] Falar", "[E] Chamar".</summary>
    public string Verbo = "Falar";

    /// <summary>Desligado some da busca — a ovelha que já está seguindo não oferece "Chamar".</summary>
    public bool Ativo = true;

    public void Acionar(Entity quem)
    {
        if (World is null)
            return;

        foreach (var componente in World.GetComponents(Entity.Id))
        {
            if (componente is IInteracao interacao)
                interacao.Interagir(quem);
        }
    }
}
