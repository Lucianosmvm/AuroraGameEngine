namespace FruitNinja;

/// <summary>Como uma leva de frutas é lançada num determinado nível.</summary>
public readonly record struct Onda(
    int QuantidadeMinima,
    int QuantidadeMaxima,
    float Intervalo,
    float ChanceDeBomba,
    float ChanceDePoder,
    float MultiplicadorDeForca);

/// <summary>
/// A dificuldade do jogo inteiro, em um arquivo só.
///
/// <para>O Fruit Ninja não tem "fases": ele acelera sozinho conforme você acerta. Aqui isso
/// virou NÍVEL — sobe a cada <see cref="PontosPorNivel"/> pontos e é o único número que
/// alimenta as contas de baixo. Quer o jogo mais duro? Mexa aqui, não nos scripts.</para>
/// </summary>
public static class CurvaNivel
{
    /// <summary>Pontos para subir um nível.</summary>
    public const int PontosPorNivel = 30;

    /// <summary>Teto do nível: passado dele o jogo para de acelerar, senão vira sorteio em vez
    /// de habilidade — no original a velocidade também satura.</summary>
    public const int NivelMaximo = 20;

    public static int NivelDosPontos(int pontos)
        => Math.Clamp(1 + pontos / PontosPorNivel, 1, NivelMaximo);

    /// <summary>Os números da leva num nível. Todas as curvas são lineares e saturadas: fácil
    /// de prever de cabeça e impossível de virar impossível por acidente.</summary>
    public static Onda Da(int nivel)
    {
        float t = (nivel - 1) / (float)(NivelMaximo - 1);   // 0 no começo, 1 no teto

        return new Onda(
            QuantidadeMinima: 1 + (int)(t * 3f),                 // 1 → 4
            QuantidadeMaxima: 3 + (int)(t * 4f),                 // 3 → 7
            Intervalo: Lerp(2.0f, 0.85f, t),                     // segundos entre levas
            ChanceDeBomba: Lerp(0.00f, 0.30f, Math.Max(0f, (nivel - 2) / 12f)),
            ChanceDePoder: 0.045f,
            MultiplicadorDeForca: Lerp(1f, 1.22f, t));
    }

    /// <summary>Bônus por cortar várias frutas no mesmo golpe, como o "Combo" do original: a
    /// partir de 3, cada fruta extra vale um ponto a mais.</summary>
    public const int ComboMinimo = 3;

    public static int BonusDeCombo(int frutasNoGolpe)
        => frutasNoGolpe < ComboMinimo ? 0 : frutasNoGolpe;

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
}
