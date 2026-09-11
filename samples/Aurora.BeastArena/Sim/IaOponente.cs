using System.Numerics;

namespace BeastArena.Sim;

public enum Dificuldade
{
    Facil,
    Normal,
}

/// <summary>
/// Oponente de máquina. Joga com as MESMAS regras do jogador: mesma mão de 4, mesma mana, mesma
/// área de implantação, e só age por <see cref="Batalha.Jogar"/>. Não enxerga nada que a tela
/// não mostre. A mesma IA serve pros dois lados (é assim que o teste bota IA contra IA).
///
/// <para>Prioridades, nessa ordem: defender santuário que ainda não é todo do oponente e está
/// sendo invadido (os meus primeiro); reforçar um tanque que já está andando; e, com mana acumulada, atacar em ONDA o
/// santuário que vale mais tomar. Roda por passo da simulação e com semente própria, então a
/// partida continua reproduzível.</para>
/// </summary>
public sealed class IaOponente
{
    /// <summary>Até onde, além do raio, inimigo perto de santuário conta como ameaça.</summary>
    private const float MargemDeAmeaca = 2.5f;

    private readonly Batalha _batalha;
    private readonly Equipe _equipe;
    private readonly Dificuldade _dificuldade;
    private readonly Random _rng;
    private float _pensar = 2f;

    /// <summary>Santuário da onda em andamento; null = juntando mana.</summary>
    private Santuario? _alvoDaOnda;

    public IaOponente(Batalha batalha, Equipe equipe, Dificuldade dificuldade, int semente)
    {
        _batalha = batalha;
        _equipe = equipe;
        _dificuldade = dificuldade;
        _rng = new Random(semente);
    }

    private Lado Lado => _batalha.LadoDe(_equipe);

    private Vector2 MeuNinho => Campo.PosicaoNinho(_equipe);

    /// <summary>Chame uma vez por <see cref="Batalha.Avancar"/>, antes dele.</summary>
    public void Passo()
    {
        if (_batalha.Acabou)
            return;

        _pensar -= Batalha.Passo;
        if (_pensar > 0f)
            return;

        _pensar = _dificuldade == Dificuldade.Facil ? Sortear(1.0f, 1.8f) : Sortear(0.45f, 0.85f);

        if (Defender() || Reforcar())
            return;

        Capturar();
    }

    // ------------------------------------------------------------------ defesa

    private bool Defender()
    {
        // Neutro sendo tomado também conta: quem não tem santuário nenhum e só defende "os seus"
        // assiste o outro dominar os três sem jogar carta.
        foreach (var santuario in _batalha.Santuarios.Where(s => s.InfluenciaDe(_equipe) > -1f)
                     .OrderByDescending(s => s.InfluenciaDe(_equipe)))
        {
            var ameacas = UnidadesPerto(_equipe.Oposta(), santuario);
            if (ameacas.Count == 0)
                continue;

            // Já tenho força suficiente lá: não gasta, deixa a mana pra tomar outro.
            float minhaVida = UnidadesPerto(_equipe, santuario).Sum(u => u.Vida);
            if (minhaVida >= ameacas.Sum(u => u.Vida) * 1.3f)
                continue;

            // No fácil, parte das invasões passa sem resposta — é o que dá espaço pro jogador
            // novato ver um santuário virar e entender o jogo.
            if (_dificuldade == Dificuldade.Facil && _rng.NextDouble() < 0.35)
                return true;

            if (TentarFeitico(ameacas))
                return true;

            // Evoluída conta como mais perto: é a ameaça que cresce se ninguém cuidar.
            var principal = ameacas.MinBy(u => Vector2.Distance(u.Posicao, santuario.Posicao) - u.Estagio * 2.5f)!;

            int indice = MelhorDefensor(principal);
            if (indice < 0)
                return true;   // nada que responda cabe na mana: segura em vez de gastar atacando

            // À distância fica recuado do santuário, do lado do ninho; corpo a corpo vai em cima
            // da ameaça.
            var carta = Lado.Mao[indice];
            var destino = carta.VelocidadeProjetil > 0f
                ? santuario.Posicao + Vector2.Normalize(MeuNinho - santuario.Posicao) * 2.5f
                : principal.Posicao;

            return JogarPerto(indice, destino);
        }

        return false;
    }

    private bool TentarFeitico(List<Unidade> ameacas)
    {
        for (int i = 0; i < Lado.TamanhoDaMao; i++)
        {
            var carta = Lado.Mao[i];
            if (carta.Tipo != TipoDeCarta.Feitico || Lado.Mana < carta.Custo)
                continue;

            Vector2 melhorCentro = default;
            float melhorValor = 0f;

            foreach (var candidata in ameacas)
            {
                float valor = 0f;
                foreach (var outra in ameacas)
                {
                    if (Vector2.Distance(outra.Posicao, candidata.Posicao) <= carta.Raio)
                        valor += outra.Carta.Custo / (float)Math.Max(1, outra.Carta.Quantidade) * (1 + outra.Estagio);
                }

                if (valor > melhorValor)
                {
                    melhorValor = valor;
                    melhorCentro = candidata.Posicao;
                }
            }

            // Só usa se a troca compensa: o valor atingido paga o custo do feitiço.
            if (melhorValor >= carta.Custo && _batalha.PodeJogar(_equipe, i, melhorCentro, out _))
            {
                _batalha.Jogar(_equipe, i, melhorCentro);
                return true;
            }
        }

        return false;
    }

    private int MelhorDefensor(Unidade ameaca)
    {
        int melhor = -1;
        float melhorNota = float.MinValue;

        for (int i = 0; i < Lado.TamanhoDaMao; i++)
        {
            var carta = Lado.Mao[i];
            if (carta.Tipo != TipoDeCarta.Criatura || carta.IgnoraUnidades || Lado.Mana < carta.Custo)
                continue;

            if (ameaca.Voa && !carta.AtacaAr)
                continue;

            // Veneno conta como dano contínuo: cada golpe renova, então na prática soma ao dps.
            float dps = (carta.Dano / MathF.Max(0.1f, carta.Cadencia) + carta.VenenoDps) * carta.Quantidade;
            float nota = dps / 40f + carta.Vida * carta.Quantidade / 600f
                         + (carta.VelocidadeProjetil > 0f ? 1.5f : 0f)
                         + (carta.Area > 0f && ameaca.Carta.Quantidade > 1 ? 2f : 0f)
                         - carta.Custo * 0.6f;

            if (nota > melhorNota)
            {
                melhorNota = nota;
                melhor = i;
            }
        }

        return melhor;
    }

    // ------------------------------------------------------------------ ataque

    /// <summary>Tanque meu já andando: manda alguém que bate de longe logo atrás dele.</summary>
    private bool Reforcar()
    {
        if (Lado.Mana < 5f)
            return false;

        var tanque = _batalha.Unidades
            .Where(u => u.Viva && u.Equipe == _equipe && u.Implantando <= 0f && EhTanque(u.Carta))
            .MinBy(u => Vector2.Distance(u.Posicao, Campo.PosicaoNinho(_equipe.Oposta())));

        // Tanque ainda colado no ninho: o apoio nasceria em cima dele de qualquer jeito.
        if (tanque is null || Vector2.Distance(tanque.Posicao, MeuNinho) < 4.5f)
            return false;

        for (int i = 0; i < Lado.TamanhoDaMao; i++)
        {
            var carta = Lado.Mao[i];
            if (carta.Tipo == TipoDeCarta.Criatura && carta.VelocidadeProjetil > 0f && Lado.Mana >= carta.Custo)
                return JogarPerto(i, tanque.Posicao + Vector2.Normalize(MeuNinho - tanque.Posicao) * 2.5f);
        }

        return false;
    }

    /// <summary>
    /// Junta mana até a reserva e então solta carta atrás de carta no MESMO santuário até a mana
    /// acabar. Uma carta por vez contra um santuário guardado só alimenta o defensor com abates;
    /// e sortear o alvo a cada carta espalharia a onda pelo mapa.
    /// </summary>
    private void Capturar()
    {
        if (_alvoDaOnda is { } atual && atual.InfluenciaDe(_equipe) >= 1f)
            _alvoDaOnda = null;

        if (_alvoDaOnda is null)
        {
            // Atrás em santuários, ataca com menos: esperar a onda cheia enquanto o outro pontua
            // é perder devagar.
            int atras = Math.Max(0, _batalha.SantuariosDe(_equipe.Oposta()) - _batalha.SantuariosDe(_equipe));
            float reserva = MathF.Max(4f, (_dificuldade == Dificuldade.Facil ? 11f : 9f) - 2f * atras);
            if (Lado.Mana < reserva)
                return;

            _alvoDaOnda = EscolherAlvoDaOnda();
            if (_alvoDaOnda is null)
                return;
        }

        var alvo = _alvoDaOnda;

        int tanque = -1;
        var outras = new List<int>();
        for (int i = 0; i < Lado.TamanhoDaMao; i++)
        {
            var carta = Lado.Mao[i];
            if (carta.Tipo != TipoDeCarta.Criatura || Lado.Mana < carta.Custo)
                continue;

            if (EhTanque(carta))
                tanque = i;
            else
                outras.Add(i);
        }

        // Sorteio, não "a mais barata": a regra fixa nunca jogava a carta de custo médio, que
        // ficava presa na mão ocupando um dos quatro espaços a partida inteira.
        if (tanque >= 0)
            JogarPerto(tanque, alvo.Posicao);
        else if (outras.Count > 0)
            JogarPerto(outras[_rng.Next(outras.Count)], alvo.Posicao);
        else
            _alvoDaOnda = null;   // nada mais cabe na mana: a onda acabou
    }

    /// <summary>Nota menor = melhor alvo: perto do meu ninho, pouca defesa, e tirar do oponente
    /// vale mais que tomar um neutro (ele para de pontuar).</summary>
    private Santuario? EscolherAlvoDaOnda()
    {
        var oponente = _equipe.Oposta();

        return _batalha.Santuarios
            .Where(s => s.InfluenciaDe(_equipe) < 1f)
            .MinBy(s => Vector2.Distance(s.Posicao, MeuNinho) * 0.2f
                        + UnidadesPerto(oponente, s).Sum(u => u.Vida) / 400f
                        - (s.Dono == oponente ? 1.5f : 0f)
                        + (float)_rng.NextDouble() * 0.8f);
    }

    // ------------------------------------------------------------------ utilidades

    private static bool EhTanque(CartaDef carta) => carta.IgnoraUnidades || carta.Vida >= 1500f;

    private List<Unidade> UnidadesPerto(Equipe equipe, Santuario santuario)
    {
        float alcance = santuario.Raio + MargemDeAmeaca;
        return _batalha.Unidades
            .Where(u => u.Viva && u.Equipe == equipe
                        && Vector2.DistanceSquared(u.Posicao, santuario.Posicao) <= alcance * alcance)
            .ToList();
    }

    /// <summary>
    /// Implanta no ponto válido mais perto do destino. A área muda com os santuários dominados,
    /// então a IA procura numa grade de meio tile em vez de assumir um lugar fixo.
    /// </summary>
    private bool JogarPerto(int indice, Vector2 destino)
    {
        Vector2? melhor = null;
        float melhorDistancia = float.MaxValue;

        for (float y = 0.75f; y < Campo.Altura; y += 0.5f)
        {
            for (float x = 0.75f; x < Campo.Largura; x += 0.5f)
            {
                var ponto = new Vector2(x, y);
                float distancia = Vector2.DistanceSquared(ponto, destino);

                if (distancia < melhorDistancia && _batalha.AreaDeImplantacao(_equipe, ponto))
                {
                    melhor = ponto;
                    melhorDistancia = distancia;
                }
            }
        }

        if (melhor is not { } local || !_batalha.PodeJogar(_equipe, indice, local, out _))
            return false;

        _batalha.Jogar(_equipe, indice, local);
        return true;
    }

    private float Sortear(float minimo, float maximo) => minimo + (float)_rng.NextDouble() * (maximo - minimo);
}
