using System.Numerics;

namespace BeastArena.Sim;

public enum Equipe
{
    /// <summary>Quem joga no aparelho: o ninho fica embaixo (Y alto).</summary>
    Jogador,

    /// <summary>O oponente (hoje a IA): o ninho fica em cima.</summary>
    Inimigo,
}

/// <summary>Obstáculo redondo de chão (pedra ou ninho). Voador passa por cima.</summary>
public readonly record struct Circulo(Vector2 Centro, float Raio);

/// <summary>
/// A geometria da arena, em TILES — não em pixels.
///
/// <para>Tudo em <c>Sim/</c> fala em tiles e segundos, e nada aqui conhece a Aurora. É isso que
/// deixa a mesma simulação rodar num servidor, num replay ou num teste sem janela nenhuma; quem
/// converte tile em pixel é o <c>Render/VisaoArena</c>.</para>
///
/// <para>Y cresce para baixo, igual à tela. O ninho do jogador fica embaixo e o do inimigo em
/// cima. Os três santuários ficam numa diagonal: o da esquerda mais perto do jogador, o da
/// direita mais perto do inimigo, o do meio no centro. O mapa inteiro é simétrico por rotação de
/// 180°, então as duas equipes jogam o mesmo campo — cada uma com um santuário "de casa", que é
/// de onde quem está perdendo se reergue.</para>
/// </summary>
public static class Campo
{
    public const float Largura = 18f;
    public const float Altura = 24f;
    public const float CentroY = Altura / 2f;

    /// <summary>Centro de cada santuário. O índice é o santuário: 0 = esquerda (casa do
    /// jogador), 1 = meio, 2 = direita (casa do inimigo).</summary>
    public static readonly Vector2[] Santuarios =
    [
        new(3.5f, CentroY + 2.5f),
        new(Largura / 2f, CentroY),
        new(Largura - 3.5f, CentroY - 2.5f),
    ];

    /// <summary>Tropa de chão com o centro dentro deste raio conta pra captura.</summary>
    public const float RaioDoSantuario = 1.6f;

    /// <summary>Corpo do ninho (obstáculo). O ninho não é alvo: não cai, não dá pra atacar.</summary>
    public const float RaioDoNinho = 1.3f;

    /// <summary>Até onde a aura do ninho cura aliado e fere invasor — é o que impede de acampar
    /// em cima de onde o outro implanta.</summary>
    public const float AlcanceDoNinho = 3.6f;

    public const float RaioDeImplantacaoDoNinho = 6.5f;
    public const float RaioDeImplantacaoDoSantuario = 3f;

    /// <summary>Pedras, descritas pro lado do jogador e espelhadas pro outro. As do meio separam
    /// os santuários: ir de um pro vizinho pela linha reta obriga a contornar.</summary>
    public static readonly Circulo[] Pedras = CriarPedras();

    /// <summary>Tudo que tropa de chão precisa contornar: pedras e os dois ninhos.</summary>
    public static readonly Circulo[] Obstaculos =
    [
        .. Pedras,
        new Circulo(PosicaoNinho(Equipe.Jogador), RaioDoNinho),
        new Circulo(PosicaoNinho(Equipe.Inimigo), RaioDoNinho),
    ];

    public static Vector2 PosicaoNinho(Equipe equipe)
        => Espelhar(equipe, new Vector2(Largura / 2f, 21.5f));

    /// <summary>
    /// Converte entre o referencial do jogador de baixo e o da <paramref name="equipe"/>. É a
    /// própria inversa (espelhar duas vezes volta ao ponto), então serve pros dois sentidos.
    /// </summary>
    public static Vector2 Espelhar(Equipe equipe, Vector2 ponto)
        => equipe == Equipe.Jogador ? ponto : new Vector2(ponto.X, Altura - ponto.Y);

    public static Equipe Oposta(this Equipe equipe)
        => equipe == Equipe.Jogador ? Equipe.Inimigo : Equipe.Jogador;

    public static bool DentroDoCampo(Vector2 ponto, float folga = 0.5f)
        => ponto.X >= folga && ponto.X <= Largura - folga && ponto.Y >= folga && ponto.Y <= Altura - folga;

    private static Circulo[] CriarPedras()
    {
        Circulo[] doJogador =
        [
            new(new Vector2(6.2f, 16f), 0.9f),
            new(new Vector2(11.8f, 16f), 0.9f),
        ];

        return
        [
            .. doJogador,
            .. doJogador.Select(p => p with { Centro = Espelhar(Equipe.Inimigo, p.Centro) }),
            new(new Vector2(6.25f, CentroY + 1.25f), 0.7f),
            new(new Vector2(11.75f, CentroY - 1.25f), 0.7f),
        ];
    }
}
