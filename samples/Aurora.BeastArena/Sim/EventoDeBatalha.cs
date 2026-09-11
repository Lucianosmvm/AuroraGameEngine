using System.Numerics;

namespace BeastArena.Sim;

public enum TipoDeEvento
{
    Implantou,
    Evoluiu,
    Recompensa,
    Morreu,

    /// <summary>Santuário virou da <see cref="EventoDeBatalha.Equipe"/>.</summary>
    Capturou,

    /// <summary>O dono do santuário (<see cref="EventoDeBatalha.Equipe"/>) perdeu o domínio.</summary>
    Neutralizou,

    FeiticoCaiu,
}

/// <summary>
/// Algo que aconteceu num passo e que a tela quer mostrar (texto subindo, onda de feitiço).
///
/// <para>A simulação não desenha nada nem chama ninguém: ela anota, e quem quiser colhe com
/// <see cref="Batalha.ColherEventos"/>. Um servidor simplesmente nunca colhe.</para>
/// </summary>
public readonly record struct EventoDeBatalha(
    TipoDeEvento Tipo,
    Equipe Equipe,
    Vector2 Posicao,
    string Texto = "",
    int Valor = 0,
    float Raio = 0f);
