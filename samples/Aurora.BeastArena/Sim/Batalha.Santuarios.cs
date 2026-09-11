using System.Numerics;

namespace BeastArena.Sim;

// Captura dos santuários, pontos, vitória por meta e a aura dos ninhos.
public sealed partial class Batalha
{
    /// <summary>Dano por segundo da aura do ninho em criatura inimiga.</summary>
    public const float DanoDoNinhoPorSegundo = 90f;

    /// <summary>Fração da vida máxima curada por segundo em criatura aliada perto do ninho.</summary>
    public const float CuraDoNinhoPorSegundo = 0.05f;

    /// <summary>Cada tropa além da primeira acelera a captura nisto, até o dobro.</summary>
    private const float BonusPorTropaExtra = 0.25f;

    private static readonly Equipe[] Equipes = [Equipe.Jogador, Equipe.Inimigo];

    private void AtualizarSantuarios()
    {
        foreach (var santuario in _santuarios)
        {
            int jogador = 0, inimigo = 0;
            foreach (var unidade in _unidades)
            {
                if (!unidade.Captura || !santuario.Contem(unidade.Posicao))
                    continue;

                if (unidade.Equipe == Equipe.Jogador)
                    jogador++;
                else
                    inimigo++;
            }

            santuario.Disputado = jogador > 0 && inimigo > 0;

            if (!santuario.Disputado && jogador + inimigo > 0)
                Puxar(santuario, jogador > 0 ? Equipe.Jogador : Equipe.Inimigo, Math.Max(jogador, inimigo));

            santuario.SobAtaque = santuario.Dono switch
            {
                Equipe.Jogador => inimigo > 0,
                Equipe.Inimigo => jogador > 0,
                _ => false,
            };

            if (santuario.Dono is { } dono)
                LadoDe(dono).Pontos += PontosPorSegundo * Passo;
        }

        float pontosJogador = LadoDe(Equipe.Jogador).Pontos;
        float pontosInimigo = LadoDe(Equipe.Inimigo).Pontos;

        if (pontosJogador >= MetaDePontos || pontosInimigo >= MetaDePontos)
            Resultado = Comparar();
    }

    private void Puxar(Santuario santuario, Equipe equipe, int tropas)
    {
        float velocidade = Passo / TempoDeCaptura * MathF.Min(2f, 1f + BonusPorTropaExtra * (tropas - 1));
        float sentido = equipe == Equipe.Jogador ? 1f : -1f;
        santuario.Influencia = Math.Clamp(santuario.Influencia + sentido * velocidade, -1f, 1f);

        if (santuario.Dono is { } dono && santuario.InfluenciaDe(dono) <= 0f)
        {
            santuario.Dono = null;
            Anotar(new EventoDeBatalha(TipoDeEvento.Neutralizou, dono, santuario.Posicao, "Santuário perdido"));
        }

        if (santuario.Dono is not null || santuario.InfluenciaDe(equipe) < 1f)
            return;

        santuario.Dono = equipe;
        LadoDe(equipe).Capturas++;
        Anotar(new EventoDeBatalha(TipoDeEvento.Capturou, equipe, santuario.Posicao, "Santuário dominado!"));

        foreach (var unidade in _unidades)
        {
            if (unidade.Equipe == equipe && unidade.Captura && unidade.Carta.XpAoCapturar > 0
                && santuario.Contem(unidade.Posicao))
                GanharXp(unidade, unidade.Carta.XpAoCapturar);
        }
    }

    /// <summary>
    /// O ninho não é alvo e não cai. Em vez de atirar, tem uma aura: cura quem é da casa e queima
    /// quem invade. Sem ela, bastaria parar uma tropa em cima de onde o outro implanta.
    /// </summary>
    private void AtualizarNinhos()
    {
        foreach (var unidade in _unidades)
        {
            if (!unidade.Viva || unidade.Implantando > 0f)
                continue;

            foreach (var equipe in Equipes)
            {
                float alcance = Campo.AlcanceDoNinho + unidade.Raio;
                if (Vector2.DistanceSquared(unidade.Posicao, Campo.PosicaoNinho(equipe)) > alcance * alcance)
                    continue;

                if (unidade.Equipe == equipe)
                    unidade.Vida = MathF.Min(unidade.VidaMaxima, unidade.Vida + unidade.VidaMaxima * CuraDoNinhoPorSegundo * Passo);
                else
                    Causar(unidade, DanoDoNinhoPorSegundo * Passo, null, equipe, piscar: false);
            }
        }
    }
}
