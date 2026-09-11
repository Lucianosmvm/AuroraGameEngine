using System.Numerics;

namespace BeastArena.Sim;

/// <summary>
/// Uma criatura em campo. É o único tipo que tem vida e pode ser alvo — ninho e santuário não
/// caem —, então mira, dano e morte passam todos por aqui.
/// </summary>
public sealed class Unidade
{
    public int Id { get; internal init; }
    public Equipe Equipe { get; internal init; }
    public required CartaDef Carta { get; init; }

    public Vector2 Posicao { get; internal set; }

    /// <summary>Posição no passo anterior. O desenho interpola entre as duas, porque a
    /// simulação anda a 30 passos por segundo e a tela a 60 ou mais.</summary>
    public Vector2 PosicaoAnterior { get; internal set; }

    public float Vida { get; internal set; }
    public float VidaMaxima { get; internal set; }

    /// <summary>Tempo de batalha do último dano recebido (pro piscar do golpe).</summary>
    public float UltimoDano { get; internal set; } = -100f;

    public bool Viva => Vida > 0f;

    /// <summary>0 = forma base; 1 = primeira evolução da ficha; e assim por diante.</summary>
    public int Estagio { get; internal set; }

    public int Xp { get; internal set; }

    /// <summary>Segundos até a unidade entrar em ação. Dá pro oponente ver o que vem.</summary>
    public float Implantando { get; internal set; }

    /// <summary>Em ataque neste passo (parada no alcance do alvo).</summary>
    public bool Atacando { get; internal set; }

    public float Lenta { get; internal set; }
    public float Envenenada { get; internal set; }

    internal float Recarga;
    internal float ProximaBusca;
    internal Unidade? Alvo;
    internal Santuario? Objetivo;
    internal float MultiplicadorLentidao = 1f;
    internal float VenenoDps;
    internal Unidade? FonteDoVeneno;

    public EvolucaoDef? EvolucaoAtual => Estagio > 0 ? Carta.Evolucoes[Estagio - 1] : null;
    public EvolucaoDef? ProximaEvolucao => Estagio < Carta.Evolucoes.Count ? Carta.Evolucoes[Estagio] : null;

    public string Nome => EvolucaoAtual?.Nome ?? Carta.Nome;
    public float Escala => EvolucaoAtual?.Escala ?? 1f;

    public float Raio => Carta.Raio * Escala;
    public bool Voa => Carta.Voa;

    public float Dano => Carta.Dano * (EvolucaoAtual?.Dano ?? 1f);
    public float Area => EvolucaoAtual?.Area ?? Carta.Area;

    public float Velocidade => Carta.Velocidade * (EvolucaoAtual?.Velocidade ?? 1f)
        * (Lenta > 0f ? MultiplicadorLentidao : 1f);

    /// <summary>Conta pra captura de santuário: de chão e já implantada.</summary>
    public bool Captura => Viva && !Voa && Implantando <= 0f;

    /// <summary>0..1 do caminho até o próximo estágio; 1 quando já está no último.</summary>
    public float ProgressoDaEvolucao
    {
        get
        {
            if (ProximaEvolucao is not { } proxima)
                return 1f;

            int base_ = EvolucaoAtual?.Xp ?? 0;
            return Math.Clamp((Xp - base_) / (float)Math.Max(1, proxima.Xp - base_), 0f, 1f);
        }
    }
}

/// <summary>Projétil em voo. Persegue o alvo; se o alvo morrer antes, cai onde ele estava.</summary>
public sealed class Projetil
{
    public Vector2 Posicao { get; internal set; }
    public Vector2 PosicaoAnterior { get; internal set; }
    public Equipe Equipe { get; internal init; }
    public float Area { get; internal init; }

    internal Unidade Fonte = null!;
    internal Unidade Alvo = null!;
    internal Vector2 Destino;
    internal float Velocidade;
    internal float Dano;
    internal bool AtingeAr;
    internal float VenenoDps;
    internal float VenenoDuracao;
    internal float Lentidao = 1f;
    internal float LentidaoDuracao;
    internal bool Acabou;
}

/// <summary>Feitiço solto que ainda não caiu. Público pra tela desenhar o aviso da área.</summary>
public sealed class FeiticoPendente
{
    public required CartaDef Carta { get; init; }
    public Equipe Equipe { get; init; }
    public Vector2 Centro { get; init; }
    public float Restante { get; internal set; }
}
