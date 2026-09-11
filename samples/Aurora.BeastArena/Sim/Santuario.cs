using System.Numerics;

namespace BeastArena.Sim;

/// <summary>
/// Ponto de controle no meio da arena. É o objetivo da partida: santuário dominado dá pontos e
/// mana por segundo pro dono, e libera implantar criatura em volta dele.
///
/// <para><b>Influência</b> vai de -1 (inimigo) a +1 (jogador). Tropa de chão de UMA equipe só
/// dentro do raio puxa pro lado dela; das duas equipes, trava (disputa). Ninguém dentro, fica
/// onde está. Chegar em ±1 dá o domínio; o dono só perde quando a influência cruza o zero —
/// virar um santuário alheio é neutralizar e depois capturar, o dobro do tempo.</para>
/// </summary>
public sealed class Santuario
{
    public int Indice { get; internal init; }
    public Vector2 Posicao { get; internal init; }

    public float Influencia { get; internal set; }
    public Equipe? Dono { get; internal set; }

    /// <summary>Tem tropa de chão das duas equipes dentro neste passo.</summary>
    public bool Disputado { get; internal set; }

    /// <summary>Tem dono e tropa de chão do OUTRO lado dentro. Enquanto isso, o dono não
    /// implanta em volta — senão defender seria nascer em cima do atacante que andou o mapa todo.</summary>
    public bool SobAtaque { get; internal set; }

    public float Raio => Campo.RaioDoSantuario;

    /// <summary>Influência do ponto de vista da <paramref name="equipe"/>: 1 = toda dela.</summary>
    public float InfluenciaDe(Equipe equipe) => equipe == Equipe.Jogador ? Influencia : -Influencia;

    public bool Contem(Vector2 ponto) => Vector2.DistanceSquared(ponto, Posicao) <= Raio * Raio;
}
