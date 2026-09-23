namespace Bichinhos;

public enum Lado { Jogador, Inimigo }

/// <summary>Um dos dois lados da luta. O bicho do jogador vira um Lutador só durante a batalha:
/// vida e bônus de atributo não vazam pro bicho de casa.</summary>
public sealed class Lutador
{
    public required Especie Especie { get; init; }
    public required int Nivel { get; init; }
    public required int Estagio { get; init; }
    public required Atributos Atributos { get; init; }
    public required List<Golpe> Golpes { get; init; }

    /// <summary>Multiplicador de ataque vindo do cuidado (só o bicho do jogador tem).</summary>
    public float Humor { get; init; } = 1f;

    public int Vida { get; set; }
    public int VidaMax => Atributos.Vida;
    public int ModAtaque { get; set; }
    public int ModDefesa { get; set; }

    public string Nome => Especie.Estagios[Estagio].Nome;
    public Tipo Tipo => Especie.Tipo;
    public bool Desmaiado => Vida <= 0;

    public static Lutador De(Bicho bicho) => new()
    {
        Especie = bicho.Especie,
        Nivel = bicho.Nivel,
        Estagio = bicho.Estagio,
        Atributos = bicho.Atributos,
        Golpes = bicho.Golpes,
        Humor = bicho.BonusDeHumor,
        Vida = bicho.Atributos.Vida,
    };

    public static Lutador Selvagem(Especie especie, int nivel)
    {
        var cat = Catalogo.Atual;
        int estagio = cat.EstagioPorNivel(nivel);
        var atributos = cat.AtributosNoNivel(especie, nivel, estagio);
        return new Lutador
        {
            Especie = especie,
            Nivel = nivel,
            Estagio = estagio,
            Atributos = atributos,
            Golpes = cat.GolpesNoNivel(especie, nivel),
            Vida = atributos.Vida,
        };
    }

    /// <summary>Bônus de atributo à la Pokémon: cada nível vale meio atributo, de -2 a +2.</summary>
    public static float Mod(int n) => n >= 0 ? 1f + 0.5f * n : 1f / (1f - 0.5f * n);
}

/// <summary>O que aconteceu num turno, na ordem, pra tela tocar como animação + texto.</summary>
public abstract record EventoBatalha;
public sealed record Mensagem(string Texto) : EventoBatalha;
public sealed record Investida(Lado Quem, Tipo Tipo) : EventoBatalha;
public sealed record Dano(Lado Alvo, int Valor, int VidaDepois, float Efetividade) : EventoBatalha;
public sealed record Cura(Lado Alvo, int Valor, int VidaDepois) : EventoBatalha;
public sealed record MudouAtributo(Lado Alvo, bool Subiu) : EventoBatalha;
public sealed record Errou(Lado Quem) : EventoBatalha;
public sealed record Desmaio(Lado Quem) : EventoBatalha;

public enum Dificuldade { Facil, Normal, Dificil }

/// <summary>
/// A regra da luta por turnos, sem nada de tela: cada turno devolve a lista de eventos. O mais
/// rápido ataca primeiro; dano leva nível, poder, ataque/defesa, tipo e um sorteio de 85-100%.
/// </summary>
public sealed class Batalha
{
    private readonly Random _rng;

    public Lutador Jogador { get; }
    public Lutador Inimigo { get; }
    public Dificuldade Dificuldade { get; }
    public bool Acabou => Jogador.Desmaiado || Inimigo.Desmaiado || Fugiu;
    public bool Venceu => Inimigo.Desmaiado && !Jogador.Desmaiado;
    public bool Fugiu { get; private set; }

    public Batalha(Lutador jogador, Lutador inimigo, Dificuldade dificuldade, Random rng)
    {
        Jogador = jogador;
        Inimigo = inimigo;
        Dificuldade = dificuldade;
        _rng = rng;
    }

    /// <summary>Sorteia o adversário em volta do nível do jogador. Qualquer espécie aparece no
    /// mato, inclusive as iniciais — é assim que o jogador conhece as outras evoluções.</summary>
    public static Lutador SortearInimigo(int nivelJogador, Dificuldade dificuldade, Random rng)
    {
        var especies = Catalogo.Atual.Especies;
        var especie = especies[rng.Next(especies.Count)];

        int nivel = dificuldade switch
        {
            Dificuldade.Facil => nivelJogador - 1 - rng.Next(2),
            Dificuldade.Dificil => nivelJogador + 2 + rng.Next(2),
            _ => nivelJogador + rng.Next(-1, 2),
        };

        return Lutador.Selvagem(especie, Math.Clamp(nivel, 1, Catalogo.Atual.NivelMaximo));
    }

    public List<EventoBatalha> Turno(Golpe golpeJogador)
    {
        var eventos = new List<EventoBatalha>();
        if (Acabou)
            return eventos;

        var golpeInimigo = EscolherGolpeInimigo();

        // Empate de velocidade vira cara ou coroa, senão o mesmo lado ganharia sempre.
        bool jogadorPrimeiro = Jogador.Atributos.Velocidade != Inimigo.Atributos.Velocidade
            ? Jogador.Atributos.Velocidade > Inimigo.Atributos.Velocidade
            : _rng.Next(2) == 0;

        if (jogadorPrimeiro)
        {
            Agir(Lado.Jogador, golpeJogador, eventos);
            if (!Acabou) Agir(Lado.Inimigo, golpeInimigo, eventos);
        }
        else
        {
            Agir(Lado.Inimigo, golpeInimigo, eventos);
            if (!Acabou) Agir(Lado.Jogador, golpeJogador, eventos);
        }

        return eventos;
    }

    /// <summary>Poção: cura metade da vida e gasta o turno (o inimigo ataca).</summary>
    public List<EventoBatalha> UsarPocao()
    {
        var eventos = new List<EventoBatalha>();
        int cura = Math.Min(Jogador.VidaMax - Jogador.Vida, Math.Max(1, Jogador.VidaMax / 2));
        Jogador.Vida += cura;
        eventos.Add(new Mensagem($"Você deu uma poção pro {Jogador.Nome}."));
        eventos.Add(new Cura(Lado.Jogador, cura, Jogador.Vida));
        Agir(Lado.Inimigo, EscolherGolpeInimigo(), eventos);
        return eventos;
    }

    /// <summary>Fugir sempre dá certo contra mais lento; contra mais rápido é 50%.</summary>
    public List<EventoBatalha> TentarFugir()
    {
        var eventos = new List<EventoBatalha>();
        if (Jogador.Atributos.Velocidade >= Inimigo.Atributos.Velocidade || _rng.Next(2) == 0)
        {
            Fugiu = true;
            eventos.Add(new Mensagem("Vocês fugiram!"));
            return eventos;
        }

        eventos.Add(new Mensagem("Não deu pra fugir!"));
        Agir(Lado.Inimigo, EscolherGolpeInimigo(), eventos);
        return eventos;
    }

    private void Agir(Lado lado, Golpe golpe, List<EventoBatalha> eventos)
    {
        var atacante = lado == Lado.Jogador ? Jogador : Inimigo;
        var alvo = lado == Lado.Jogador ? Inimigo : Jogador;
        var ladoAlvo = lado == Lado.Jogador ? Lado.Inimigo : Lado.Jogador;
        string quem = lado == Lado.Jogador ? atacante.Nome : $"{atacante.Nome} selvagem";

        eventos.Add(new Mensagem($"{quem} usou {golpe.Nome}!"));

        if (_rng.Next(100) >= golpe.Precisao)
        {
            eventos.Add(new Investida(lado, golpe.Tipo));
            eventos.Add(new Errou(lado));
            eventos.Add(new Mensagem("Mas errou!"));
            return;
        }

        switch (golpe.Efeito)
        {
            case EfeitoGolpe.Curar:
            {
                int cura = Math.Min(atacante.VidaMax - atacante.Vida, Math.Max(1, atacante.VidaMax * 45 / 100));
                atacante.Vida += cura;
                eventos.Add(new Cura(lado, cura, atacante.Vida));
                eventos.Add(new Mensagem(cura > 0 ? $"{atacante.Nome} recuperou {cura} de vida." : "Mas já estava com a vida cheia."));
                return;
            }
            case EfeitoGolpe.SubirAtaque:
                MudarMod(atacante, lado, ataque: true, +1, eventos);
                return;
            case EfeitoGolpe.SubirDefesa:
                MudarMod(atacante, lado, ataque: false, +1, eventos);
                return;
            case EfeitoGolpe.BaixarAtaque:
                MudarMod(alvo, ladoAlvo, ataque: true, -1, eventos);
                return;
        }

        float efetividade = Tipos.Efetividade(golpe.Tipo, alvo.Tipo);
        int dano = CalcularDano(atacante, alvo, golpe, efetividade, (float)(0.85 + _rng.NextDouble() * 0.15));

        alvo.Vida = Math.Max(0, alvo.Vida - dano);
        eventos.Add(new Investida(lado, golpe.Tipo));
        eventos.Add(new Dano(ladoAlvo, dano, alvo.Vida, efetividade));

        if (efetividade > 1f)
            eventos.Add(new Mensagem("É super eficaz!"));
        else if (efetividade < 1f)
            eventos.Add(new Mensagem("Não foi muito eficaz..."));

        if (alvo.Desmaiado)
        {
            eventos.Add(new Desmaio(ladoAlvo));
            eventos.Add(new Mensagem(ladoAlvo == Lado.Jogador ? $"{alvo.Nome} desmaiou!" : $"{alvo.Nome} selvagem desmaiou!"));
        }
    }

    private static void MudarMod(Lutador alvo, Lado lado, bool ataque, int delta, List<EventoBatalha> eventos)
    {
        int atual = ataque ? alvo.ModAtaque : alvo.ModDefesa;
        int novo = Math.Clamp(atual + delta, -2, 2);
        string atributo = ataque ? "O ataque" : "A defesa";

        if (novo == atual)
        {
            eventos.Add(new Mensagem($"{atributo} de {alvo.Nome} não muda mais."));
            return;
        }

        if (ataque) alvo.ModAtaque = novo;
        else alvo.ModDefesa = novo;

        eventos.Add(new MudouAtributo(lado, delta > 0));
        eventos.Add(new Mensagem($"{atributo} de {alvo.Nome} {(delta > 0 ? "subiu" : "caiu")}!"));
    }

    public static int CalcularDano(Lutador atacante, Lutador alvo, Golpe golpe, float efetividade, float sorteio)
    {
        float ataque = atacante.Atributos.Ataque * Lutador.Mod(atacante.ModAtaque) * atacante.Humor;
        float defesa = MathF.Max(1f, alvo.Atributos.Defesa * Lutador.Mod(alvo.ModDefesa));
        float mesmoTipo = golpe.Tipo == atacante.Tipo && golpe.Tipo != Tipo.Normal ? 1.5f : 1f;

        float bruto = ((2f * atacante.Nivel / 5f + 2f) * golpe.Poder * ataque / defesa) / 50f + 2f;
        return Math.Max(1, (int)(bruto * mesmoTipo * efetividade * sorteio));
    }

    /// <summary>IA simples: cura quando está mal, senão prefere o golpe que mais machuca — com um
    /// pouco de sorteio pra não ser previsível. No difícil ela quase não erra a escolha.</summary>
    private Golpe EscolherGolpeInimigo()
    {
        var golpes = Inimigo.Golpes;
        var cura = golpes.FirstOrDefault(g => g.Efeito == EfeitoGolpe.Curar);
        if (cura is not null && Inimigo.Vida < Inimigo.VidaMax * 0.35f && _rng.Next(100) < 70)
            return cura;

        double acaso = Dificuldade switch { Dificuldade.Facil => 0.6, Dificuldade.Dificil => 0.1, _ => 0.3 };
        if (_rng.NextDouble() < acaso)
            return golpes[_rng.Next(golpes.Count)];

        return golpes
            .OrderByDescending(g => g.Poder == 0 ? 15f : g.Poder * Tipos.Efetividade(g.Tipo, Jogador.Tipo) * (g.Tipo == Inimigo.Tipo ? 1.5f : 1f) * g.Precisao / 100f)
            .First();
    }

    /// <summary>XP e moedas da vitória. Adversário acima do seu nível paga mais.</summary>
    public (int Xp, int Moedas) Recompensa()
    {
        float bonus = Dificuldade switch { Dificuldade.Facil => 0.7f, Dificuldade.Dificil => 1.6f, _ => 1f };
        int xp = (int)((12 + Inimigo.Nivel * 6) * bonus);
        int moedas = (int)((4 + Inimigo.Nivel * 2) * bonus);
        return (xp, moedas);
    }
}
