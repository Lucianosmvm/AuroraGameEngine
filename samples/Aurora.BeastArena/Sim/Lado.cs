namespace BeastArena.Sim;

/// <summary>
/// O que pertence a UM jogador na batalha: mana, mão, fila do baralho e placar.
///
/// <para>Ciclo do baralho: 4 cartas na mão, as outras numa fila. Jogar a carta do espaço N põe a
/// primeira da fila no espaço N e manda a jogada pro FIM da fila — por isso a mesma carta nunca
/// volta antes de todas as outras passarem, e dá pra "contar ciclo" do oponente.</para>
/// </summary>
public sealed class Lado
{
    public const int TamanhoDaMao = 4;
    public const float ManaMaxima = 12f;
    public const float ManaInicial = 5f;

    private readonly Queue<CartaDef> _fila = new();

    public Equipe Equipe { get; }
    public float Mana { get; internal set; } = ManaInicial;
    public CartaDef[] Mao { get; } = new CartaDef[TamanhoDaMao];
    public CartaDef Proxima => _fila.Peek();

    /// <summary>Pontos de santuário. Fracionário porque pinga a cada passo; a tela mostra inteiro.</summary>
    public float Pontos { get; internal set; }

    /// <summary>Quantas vezes este lado dominou um santuário. Estatística da tela de fim.</summary>
    public int Capturas { get; internal set; }

    /// <summary>Quantas vezes unidades deste lado evoluíram.</summary>
    public int Evolucoes { get; internal set; }

    /// <summary>Mana ganha matando unidades evoluídas do oponente.</summary>
    public int ManaDeRecompensa { get; internal set; }

    /// <summary>Mana ganha por abate comum (fração do custo da vítima).</summary>
    public float ManaDeAbates { get; internal set; }

    internal Lado(Equipe equipe, IReadOnlyList<CartaDef> baralho)
    {
        if (baralho.Count <= TamanhoDaMao)
            throw new ArgumentException($"O baralho precisa de mais de {TamanhoDaMao} cartas.", nameof(baralho));

        Equipe = equipe;

        for (int i = 0; i < baralho.Count; i++)
        {
            if (i < TamanhoDaMao)
                Mao[i] = baralho[i];
            else
                _fila.Enqueue(baralho[i]);
        }
    }

    internal CartaDef Jogar(int indice)
    {
        var carta = Mao[indice];
        Mao[indice] = _fila.Dequeue();
        _fila.Enqueue(carta);
        return carta;
    }

    internal void GanharMana(float quantidade)
        => Mana = Math.Clamp(Mana + quantidade, 0f, ManaMaxima);
}
