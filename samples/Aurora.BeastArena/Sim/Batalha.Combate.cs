using System.Numerics;

namespace BeastArena.Sim;

// Mira, objetivo, ataque, dano, morte, evolução e recompensa.
public sealed partial class Batalha
{
    private const float IntervaloDeBusca = 0.2f;

    /// <summary>Fração da vida máxima NOVA curada ao evoluir. Sem cura, a evolução chega quase
    /// sempre numa unidade que acabou de brigar e morre em seguida — o jogador nem vê o estágio.</summary>
    private const float CuraAoEvoluir = 0.25f;

    /// <summary>Quanto pra dentro do raio do santuário a tropa anda antes de parar. Parar na
    /// borda deixaria qualquer empurrão da separação tirar ela da contagem.</summary>
    private const float FolgaNoSantuario = 0.5f;

    // ------------------------------------------------------------------ unidades

    private void AtualizarUnidades()
    {
        foreach (var unidade in _unidades)
        {
            if (!unidade.Viva)
                continue;

            unidade.PosicaoAnterior = unidade.Posicao;
            unidade.Atacando = false;

            if (unidade.Implantando > 0f)
            {
                unidade.Implantando = MathF.Max(0f, unidade.Implantando - Passo);
                continue;
            }

            AtualizarStatus(unidade);
            if (!unidade.Viva)
                continue;

            unidade.Recarga -= Passo * (unidade.Lenta > 0f ? unidade.MultiplicadorLentidao : 1f);

            // Trava no alvo enquanto estiver batendo nele: quem já está na briga não larga porque
            // outro passou do lado. Andando, reavalia de tempos em tempos — é o que faz a unidade
            // "ver" o inimigo que apareceu no caminho, e mudar de santuário quando o dela virou.
            bool travada = unidade.Alvo is { Viva: true } atual && NoAlcance(unidade, atual);
            if (!travada)
            {
                unidade.ProximaBusca -= Passo;
                if (unidade.ProximaBusca <= 0f || unidade.Alvo is { Viva: false })
                {
                    unidade.Alvo = EscolherAlvo(unidade);
                    unidade.Objetivo = EscolherObjetivo(unidade);
                    unidade.ProximaBusca = IntervaloDeBusca;
                }
            }

            if (unidade.Alvo is { } alvo)
            {
                if (!NoAlcance(unidade, alvo))
                {
                    Mover(unidade, alvo.Posicao);
                    continue;
                }

                unidade.Atacando = true;
                if (unidade.Recarga > 0f)
                    continue;

                Atacar(unidade, alvo);
                unidade.Recarga = unidade.Carta.Cadencia;
                continue;
            }

            // Ninguém à vista: vai pro santuário e para lá dentro. Com os três já da equipe não tem
            // o que tomar: marcha pro ninho inimigo, e a aura dele cobra o preço. Sem isso o
            // exército de quem lidera só crescia, parado, e quem está atrás não virava nunca.
            if (unidade.Objetivo is not { } santuario)
                Mover(unidade, Campo.PosicaoNinho(unidade.Equipe.Oposta()));
            else if (Vector2.Distance(unidade.Posicao, santuario.Posicao) > santuario.Raio - FolgaNoSantuario)
                Mover(unidade, santuario.Posicao);
        }
    }

    private void AtualizarStatus(Unidade unidade)
    {
        if (unidade.Lenta > 0f)
            unidade.Lenta = MathF.Max(0f, unidade.Lenta - Passo);

        if (unidade.Envenenada <= 0f)
            return;

        unidade.Envenenada = MathF.Max(0f, unidade.Envenenada - Passo);
        var fonte = unidade.FonteDoVeneno;
        Causar(unidade, unidade.VenenoDps * Passo, fonte, fonte?.Equipe ?? unidade.Equipe.Oposta(), piscar: false);
    }

    /// <summary>
    /// Inimigo mais perto dentro da visão que ela consegue acertar. Quem
    /// <see cref="CartaDef.IgnoraUnidades"/> só enxerga quem disputa o santuário onde ela está.
    /// </summary>
    private Unidade? EscolherAlvo(Unidade unidade)
    {
        var carta = unidade.Carta;
        Santuario? segurando = null;

        if (carta.IgnoraUnidades)
        {
            segurando = _santuarios.FirstOrDefault(s => s.Contem(unidade.Posicao));
            if (segurando is null)
                return null;
        }

        var inimiga = unidade.Equipe.Oposta();
        Unidade? melhor = null;
        float melhorDistancia = carta.Visao;

        foreach (var outra in _unidades)
        {
            if (outra.Equipe != inimiga || !outra.Viva || (outra.Voa && !carta.AtacaAr)
                || (segurando is not null && !segurando.Contem(outra.Posicao)))
                continue;

            float distancia = DistanciaDeBorda(unidade, outra);
            if (distancia <= melhorDistancia)
            {
                melhor = outra;
                melhorDistancia = distancia;
            }
        }

        return melhor;
    }

    /// <summary>
    /// Pra onde a unidade anda sem inimigo à vista. Dentro de um santuário que ainda não é todo da
    /// equipe, fica nele até virar. Senão, o mais perto que ainda não é. Null = os três já são da
    /// equipe.
    /// </summary>
    private Santuario? EscolherObjetivo(Unidade unidade)
    {
        var equipe = unidade.Equipe;

        foreach (var santuario in _santuarios)
        {
            if (santuario.Contem(unidade.Posicao) && santuario.InfluenciaDe(equipe) < 1f)
                return santuario;
        }

        Santuario? melhor = null;
        float melhorDistancia = float.MaxValue;

        foreach (var santuario in _santuarios)
        {
            float distancia = Vector2.DistanceSquared(unidade.Posicao, santuario.Posicao);
            if (santuario.InfluenciaDe(equipe) < 1f && distancia < melhorDistancia)
            {
                melhor = santuario;
                melhorDistancia = distancia;
            }
        }

        return melhor;
    }

    private void Atacar(Unidade unidade, Unidade alvo)
    {
        var carta = unidade.Carta;
        float multiplicador = unidade.EvolucaoAtual?.Dano ?? 1f;

        if (carta.VelocidadeProjetil > 0f)
        {
            var projetil = Disparar(unidade, alvo, unidade.Dano, carta.VelocidadeProjetil, unidade.Area, carta.AtacaAr);
            projetil.VenenoDps = carta.VenenoDps * multiplicador;
            projetil.VenenoDuracao = carta.VenenoDuracao;
            projetil.Lentidao = carta.Lentidao;
            projetil.LentidaoDuracao = carta.LentidaoDuracao;
        }
        else
        {
            Golpear(unidade, alvo.Posicao, alvo, unidade.Dano, unidade.Area, carta.AtacaAr,
                carta.VenenoDps * multiplicador, carta.VenenoDuracao, carta.Lentidao, carta.LentidaoDuracao);
        }
    }

    // ------------------------------------------------------------------ projéteis e feitiços

    private Projetil Disparar(Unidade fonte, Unidade alvo, float dano, float velocidade, float area, bool atingeAr)
    {
        var projetil = new Projetil
        {
            Fonte = fonte, Alvo = alvo, Equipe = fonte.Equipe, Area = area,
            Posicao = fonte.Posicao, PosicaoAnterior = fonte.Posicao,
            Destino = alvo.Posicao, Velocidade = velocidade, Dano = dano, AtingeAr = atingeAr,
        };

        _projeteis.Add(projetil);
        return projetil;
    }

    private void AtualizarProjeteis()
    {
        foreach (var projetil in _projeteis)
        {
            projetil.PosicaoAnterior = projetil.Posicao;

            if (projetil.Alvo.Viva)
                projetil.Destino = projetil.Alvo.Posicao;

            var delta = projetil.Destino - projetil.Posicao;
            float distancia = delta.Length();
            float passo = projetil.Velocidade * Passo;

            if (distancia > passo)
            {
                projetil.Posicao += delta / distancia * passo;
                continue;
            }

            projetil.Posicao = projetil.Destino;
            projetil.Acabou = true;

            // Alvo morto no meio do voo: projétil de área ainda explode onde ele estava; o de
            // alvo único se perde (não existe "acertar o chão").
            Golpear(projetil.Fonte, projetil.Destino, projetil.Alvo.Viva ? projetil.Alvo : null,
                projetil.Dano, projetil.Area, projetil.AtingeAr,
                projetil.VenenoDps, projetil.VenenoDuracao, projetil.Lentidao, projetil.LentidaoDuracao);
        }

        _projeteis.RemoveAll(p => p.Acabou);
    }

    private void AtualizarFeiticos()
    {
        foreach (var feitico in _feiticos)
        {
            feitico.Restante -= Passo;
            if (feitico.Restante > 0f)
                continue;

            var carta = feitico.Carta;
            var inimiga = feitico.Equipe.Oposta();

            // Materializa antes: o dano mata, e matar mexe em estatística que outro alvo da
            // mesma área também lê (recompensa, XP).
            foreach (var unidade in _unidades.Where(u => u.Viva && u.Equipe == inimiga
                         && Vector2.Distance(u.Posicao, feitico.Centro) - u.Raio <= carta.Raio).ToList())
            {
                Ferir(unidade, carta.Dano, null, feitico.Equipe, 0f, 0f, carta.Lentidao, carta.LentidaoDuracao);
            }

            Anotar(new EventoDeBatalha(TipoDeEvento.FeiticoCaiu, feitico.Equipe, feitico.Centro, carta.Id, Raio: carta.Raio));
        }

        _feiticos.RemoveAll(f => f.Restante <= 0f);
    }

    // ------------------------------------------------------------------ dano

    /// <summary>Um golpe (corpo a corpo ou chegada de projétil): no alvo, ou em todo inimigo da
    /// área em volta de <paramref name="centro"/>.</summary>
    private void Golpear(Unidade fonte, Vector2 centro, Unidade? alvo, float dano, float area, bool atingeAr,
        float venenoDps, float venenoDuracao, float lentidao, float lentidaoDuracao)
    {
        if (area <= 0f)
        {
            if (alvo is not null)
                Ferir(alvo, dano, fonte, fonte.Equipe, venenoDps, venenoDuracao, lentidao, lentidaoDuracao);
            return;
        }

        var inimiga = fonte.Equipe.Oposta();
        var atingidos = _unidades.Where(u => u.Viva && u.Equipe == inimiga && (atingeAr || !u.Voa)
                                             && Vector2.Distance(u.Posicao, centro) - u.Raio <= area).ToList();

        foreach (var atingido in atingidos)
            Ferir(atingido, dano, fonte, fonte.Equipe, venenoDps, venenoDuracao, lentidao, lentidaoDuracao);
    }

    private void Ferir(Unidade alvo, float dano, Unidade? fonte, Equipe equipeDaFonte,
        float venenoDps, float venenoDuracao, float lentidao, float lentidaoDuracao)
    {
        if (!alvo.Viva)
            return;

        if (lentidao < 1f && lentidaoDuracao > 0f)
        {
            alvo.MultiplicadorLentidao = alvo.Lenta > 0f
                ? MathF.Min(alvo.MultiplicadorLentidao, lentidao)
                : lentidao;
            alvo.Lenta = MathF.Max(alvo.Lenta, lentidaoDuracao);
        }

        if (venenoDps > 0f && venenoDuracao > 0f)
        {
            // Renova em vez de somar: veneno empilhando de várias serpentes derreteria
            // qualquer tanque, e a leitura na tela ("está verde") fica sempre verdadeira.
            alvo.VenenoDps = alvo.Envenenada > 0f ? MathF.Max(alvo.VenenoDps, venenoDps) : venenoDps;
            alvo.Envenenada = venenoDuracao;
            alvo.FonteDoVeneno = fonte;
        }

        Causar(alvo, dano, fonte, equipeDaFonte);
    }

    /// <param name="piscar">Dano contínuo (veneno, aura do ninho) não pisca: piscaria em todo
    /// passo e a unidade ficaria branca o tempo inteiro.</param>
    private void Causar(Unidade alvo, float dano, Unidade? fonte, Equipe equipeDaFonte, bool piscar = true)
    {
        if (!alvo.Viva || dano <= 0f)
            return;

        alvo.Vida -= dano;
        if (piscar)
            alvo.UltimoDano = Tempo;

        if (alvo.Viva)
            return;

        alvo.Vida = 0f;
        UnidadeMorreu(alvo, fonte, equipeDaFonte);
    }

    private void UnidadeMorreu(Unidade morta, Unidade? fonte, Equipe equipeDaFonte)
    {
        Anotar(new EventoDeBatalha(TipoDeEvento.Morreu, morta.Equipe, morta.Posicao, morta.Nome, morta.Estagio));

        var matador = LadoDe(morta.Equipe.Oposta());

        float manaDoAbate = ManaPorAbate * morta.Carta.Custo / Math.Max(1, morta.Carta.Quantidade);
        matador.GanharMana(manaDoAbate);
        matador.ManaDeAbates += manaDoAbate;

        int recompensa = morta.EvolucaoAtual?.Recompensa ?? 0;
        if (recompensa > 0)
        {
            matador.GanharMana(recompensa);
            matador.ManaDeRecompensa += recompensa;
            Anotar(new EventoDeBatalha(TipoDeEvento.Recompensa, matador.Equipe, morta.Posicao, $"+{recompensa} mana", recompensa));
        }

        // Evoluída vale mais XP: caçar a ameaça que o outro deixou crescer é o que faz a SUA
        // unidade crescer também.
        if (fonte is { Viva: true } matadora && matadora.Equipe == equipeDaFonte)
            GanharXp(matadora, morta.Carta.XpQueVale * (1 + morta.Estagio));
    }

    internal void GanharXp(Unidade unidade, int quantidade)
    {
        unidade.Xp += quantidade;

        while (unidade.ProximaEvolucao is { } proxima && unidade.Xp >= proxima.Xp)
            Evoluir(unidade);
    }

    private void Evoluir(Unidade unidade)
    {
        float fracao = unidade.Vida / MathF.Max(1f, unidade.VidaMaxima);

        unidade.Estagio++;
        var evolucao = unidade.EvolucaoAtual!;

        unidade.VidaMaxima = unidade.Carta.Vida * evolucao.Vida;
        unidade.Vida = MathF.Min(unidade.VidaMaxima, unidade.VidaMaxima * (fracao + CuraAoEvoluir));

        LadoDe(unidade.Equipe).Evolucoes++;
        Anotar(new EventoDeBatalha(TipoDeEvento.Evoluiu, unidade.Equipe, unidade.Posicao, evolucao.Nome, unidade.Estagio));
    }

    // ------------------------------------------------------------------ geometria

    private static float DistanciaDeBorda(Unidade a, Unidade b)
        => Vector2.Distance(a.Posicao, b.Posicao) - a.Raio - b.Raio;

    private static bool NoAlcance(Unidade unidade, Unidade alvo)
        => DistanciaDeBorda(unidade, alvo) <= unidade.Carta.Alcance;
}
