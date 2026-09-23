namespace Bichinhos;

/// <summary>
/// O bichinho do jogador: necessidades (estilo Tamagotchi), nível e estágio. É só dado + regra —
/// nada de desenho aqui, pra dar pra testar o relógio e a evolução sem abrir janela.
///
/// <para>Todas as necessidades vão de 0 (péssimo) a 100 (ótimo): Fome 100 = de barriga cheia.</para>
/// </summary>
public sealed class Bicho
{
    public string EspecieId { get; set; } = "";
    public int Nivel { get; set; } = 1;
    public int Xp { get; set; }
    public int Estagio { get; set; }

    public float Fome { get; set; } = 80f;
    public float Alegria { get; set; } = 80f;
    public float Energia { get; set; } = 100f;
    public float Higiene { get; set; } = 100f;
    public float Saude { get; set; } = 100f;

    public bool Dormindo { get; set; }
    public bool Doente { get; set; }
    public int Cocos { get; set; }

    /// <summary>Minutos (de relógio acordado) até o próximo cocô.</summary>
    public float MinutosProximoCoco { get; set; } = 90f;

    /// <summary>Horas de vida somadas — vira a "idade em dias" da ficha.</summary>
    public double HorasDeVida { get; set; }

    public int Vitorias { get; set; }
    public int Derrotas { get; set; }

    public const int MaxCocos = 4;

    // ------------------------------------------------------------------ taxas por hora

    // Calibradas pra um dia normal de celular: deixar o bicho sozinho uma manhã inteira deixa ele
    // com fome e sujo, mas não doente. Esquecer um dia inteiro adoece.
    private const float FomePorHora = 12f;
    private const float AlegriaPorHora = 9f;
    private const float EnergiaPorHora = 8f;
    private const float HigienePorHora = 3f;
    private const float HigienePorCocoPorHora = 7f;
    private const float EnergiaDormindoPorHora = 45f;
    private const float SaudeRuimPorHora = 6f;
    private const float SaudeDoentePorHora = 4f;
    private const float SaudeBoaPorHora = 5f;

    // ------------------------------------------------------------------ derivados

    public Especie Especie => Catalogo.Atual.Especie(EspecieId);
    public string Nome => Especie.Estagios[Estagio].Nome;

    public int XpParaProximo => XpNecessario(Nivel);

    public static int XpNecessario(int nivel) => 10 + nivel * nivel * 2;

    public bool NivelMaximo => Nivel >= Catalogo.Atual.NivelMaximo;

    /// <summary>Nível já passou do limiar mas o bicho ainda não mudou de forma. A evolução é uma
    /// cena, não um número: quem chama decide quando mostrar.</summary>
    public bool EvolucaoPendente => Catalogo.Atual.EstagioPorNivel(Nivel) > Estagio;

    public Atributos Atributos => Catalogo.Atual.AtributosNoNivel(Especie, Nivel, Estagio);

    public List<Golpe> Golpes => Catalogo.Atual.GolpesNoNivel(Especie, Nivel);

    /// <summary>A necessidade mais urgente agora, ou null se está tudo bem. Vira o balãozinho
    /// sobre a cabeça do bicho.</summary>
    public Necessidade? Urgente
    {
        get
        {
            if (Doente) return Necessidade.Remedio;
            if (Cocos >= 2 || Higiene < 30f) return Necessidade.Banho;
            if (Fome < 30f) return Necessidade.Comida;
            if (!Dormindo && Energia < 20f) return Necessidade.Sono;
            if (Alegria < 30f) return Necessidade.Carinho;
            return null;
        }
    }

    public bool PodeBatalhar(out string motivo)
    {
        motivo = Dormindo ? $"{Nome} está dormindo."
            : Doente ? $"{Nome} está doente. Dê remédio primeiro."
            : Energia < 15f ? $"{Nome} está cansado demais. Deixe dormir."
            : Fome < 10f ? $"{Nome} está com muita fome pra lutar."
            : "";
        return motivo.Length == 0;
    }

    /// <summary>Bicho feliz e alimentado luta um pouco melhor — é o elo entre cuidar e batalhar.</summary>
    public float BonusDeHumor => 0.9f + 0.2f * Math.Clamp((Alegria + Fome) / 200f, 0f, 1f);

    // ------------------------------------------------------------------ relógio

    /// <summary>Passa o tempo. Chamado todo frame com o tempo real (em horas) e, ao abrir o jogo,
    /// com o tempo que ele ficou fechado — em passos de um minuto, pra cocô e doença acontecerem
    /// no meio do caminho e não todos de uma vez no fim.</summary>
    public void Avancar(double horas, Random rng)
    {
        const double Passo = 1.0 / 60.0;

        while (horas > 0)
        {
            double h = Math.Min(Passo, horas);
            AvancarPasso((float)h, rng);
            horas -= h;
        }
    }

    private void AvancarPasso(float h, Random rng)
    {
        HorasDeVida += h;

        if (Dormindo)
        {
            Fome -= FomePorHora * 0.35f * h;
            Alegria -= AlegriaPorHora * 0.2f * h;
            Energia += EnergiaDormindoPorHora * h;

            if (Energia >= 100f)
            {
                Energia = 100f;
                Dormindo = false;   // acorda sozinho quando descansou
            }
        }
        else
        {
            Fome -= FomePorHora * h;
            Alegria -= AlegriaPorHora * (Doente ? 1.5f : 1f) * h;
            Energia -= EnergiaPorHora * h;

            MinutosProximoCoco -= h * 60f;
            if (MinutosProximoCoco <= 0f)
            {
                Cocos = Math.Min(MaxCocos, Cocos + 1);
                MinutosProximoCoco = 80f + (float)rng.NextDouble() * 40f;
            }

            if (Energia <= 0f)
            {
                Energia = 0f;
                Dormindo = true;    // desmaia de sono se ninguém mandar dormir
            }
        }

        Higiene -= (HigienePorHora + HigienePorCocoPorHora * Cocos) * h;

        bool maltratado = Fome < 15f || Higiene < 20f || Cocos >= 3;
        if (maltratado)
            Saude -= SaudeRuimPorHora * h;
        if (Doente)
            Saude -= SaudeDoentePorHora * h;
        if (!maltratado && !Doente)
            Saude += SaudeBoaPorHora * h;

        // Saúde baixa vira doença aos poucos: uma chance por minuto, maior quanto pior.
        if (!Doente && Saude < 50f && rng.NextDouble() < (50f - Saude) / 50f * 0.02)
            Doente = true;

        Limitar();
    }

    private void Limitar()
    {
        Fome = Math.Clamp(Fome, 0f, 100f);
        Alegria = Math.Clamp(Alegria, 0f, 100f);
        Energia = Math.Clamp(Energia, 0f, 100f);
        Higiene = Math.Clamp(Higiene, 0f, 100f);
        Saude = Math.Clamp(Saude, 0f, 100f);
    }

    // ------------------------------------------------------------------ ações de cuidado

    /// <summary>Refeição. Recusa se já está cheio — é o "não quero" do Tamagotchi.</summary>
    public bool Comer(out string fala)
    {
        if (Fome >= 95f)
        {
            fala = "Tô cheio!";
            return false;
        }

        Fome += 35f;
        Saude += 2f;
        MinutosProximoCoco -= 15f;   // comeu, vai ao banheiro mais cedo
        fala = "Nham nham!";
        Limitar();
        return true;
    }

    public bool ComerDoce(out string fala)
    {
        if (Fome >= 98f && Alegria >= 98f)
        {
            fala = "Não cabe mais nada!";
            return false;
        }

        Alegria += 25f;
        Fome += 8f;
        Saude -= 3f;
        fala = "Que delícia!";
        Limitar();
        return true;
    }

    public void Limpar()
    {
        Cocos = 0;
        Higiene = 100f;
        Limitar();
    }

    public bool TomarRemedio(out string fala)
    {
        if (!Doente && Saude >= 90f)
        {
            fala = "Não tô doente!";
            return false;
        }

        Doente = false;
        Saude += 35f;
        Alegria -= 5f;   // remédio é ruim, mas precisa
        fala = "Eca... mas melhorei!";
        Limitar();
        return true;
    }

    public void Carinho()
    {
        Alegria += 4f;
        Limitar();
    }

    public void Brincou(bool ganhou)
    {
        Alegria += ganhou ? 25f : 12f;
        Energia -= 8f;
        Fome -= 5f;
        Limitar();
    }

    public void Lutou(bool venceu)
    {
        Energia -= 15f;
        Fome -= 8f;
        Alegria += venceu ? 10f : -15f;
        if (!venceu)
            Saude -= 10f;
        if (venceu) Vitorias++;
        else Derrotas++;
        Limitar();
    }

    // ------------------------------------------------------------------ experiência

    /// <summary>Soma XP e sobe quantos níveis couberem. Devolve os níveis ganhos (vazio se nenhum)
    /// e os golpes aprendidos no caminho, pra tela anunciar um por um.</summary>
    public (List<int> Niveis, List<Golpe> Golpes) GanharXp(int xp)
    {
        var niveis = new List<int>();
        var golpes = new List<Golpe>();

        if (NivelMaximo)
            return (niveis, golpes);

        Xp += Math.Max(0, xp);

        while (!NivelMaximo && Xp >= XpParaProximo)
        {
            Xp -= XpParaProximo;
            Nivel++;
            niveis.Add(Nivel);

            foreach (var aprendido in Especie.Golpes.Where(g => g.Nivel == Nivel))
                golpes.Add(Catalogo.Atual.Golpe(aprendido.Golpe));
        }

        if (NivelMaximo)
            Xp = 0;

        return (niveis, golpes);
    }

    /// <summary>Aplica a evolução pendente (um estágio por vez).</summary>
    public void Evoluir()
    {
        if (EvolucaoPendente)
            Estagio++;
    }
}

public enum Necessidade { Comida, Banho, Remedio, Sono, Carinho }
