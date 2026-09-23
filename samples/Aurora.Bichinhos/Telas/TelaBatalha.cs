using System.Numerics;
using Aurora.Runtime.Graphics;

namespace Bichinhos;

/// <summary>
/// Batalha por turnos no estilo Pokémon: o adversário no alto à direita, o seu bicho embaixo à
/// esquerda, quatro golpes, poção e fugir.
///
/// <para>A regra é toda da <see cref="Batalha"/>: aqui só se escolhe o golpe e se TOCA a lista de
/// eventos do turno, um por vez — texto na caixa, investida, piscar de dano, barra de vida
/// descendo. Tocar na tela adianta o texto.</para>
/// </summary>
public sealed class TelaBatalha : Tela
{
    private enum Fase { Entrada, Escolha, Tocando, Fim }

    private const int PrecoPocao = 10;

    private readonly Batalha _batalha;
    private readonly BichoVisual _visualJogador = new();
    private readonly BichoVisual _visualInimigo = new();
    private readonly Particulas _particulas = new();
    private readonly Queue<EventoBatalha> _fila = new();

    private Fase _fase = Fase.Entrada;
    private EventoBatalha? _atual;
    private float _t;
    private float _tempo;
    private string _texto = "";
    private float _vidaMostradaJogador;
    private float _vidaMostradaInimigo;
    private int _vidaAlvoJogador;
    private int _vidaAlvoInimigo;
    private bool _recompensaDada;

    private static readonly Vector2 PeInimigo = new(500f, 485f);
    private static readonly Vector2 PeJogador = new(230f, 870f);
    private const float LadoInimigo = 290f;
    private const float LadoJogador = 360f;

    private static readonly Caixa CaixaTexto = new(20f, 900f, 680f, 110f);
    private static readonly Caixa BotaoPocao = new(410f, 770f, 140f, 64f);
    private static readonly Caixa BotaoFugir = new(560f, 770f, 140f, 64f);
    private static readonly Caixa BotaoContinuar = new(160f, 1060f, 400f, 110f);

    private static Caixa BotaoGolpe(int i) => new(20f + (i % 2) * 345f, 1025f + (i / 2) * 102f, 335f, 90f);

    public TelaBatalha(BichinhosGame jogo, Batalha batalha) : base(jogo)
    {
        _batalha = batalha;
        _vidaMostradaJogador = _vidaAlvoJogador = batalha.Jogador.Vida;
        _vidaMostradaInimigo = _vidaAlvoInimigo = batalha.Inimigo.Vida;
        _visualJogador.Espelhar = true;   // olha pro adversário (à direita)
    }

    private Bicho B => Jogo.Bicho!;

    public override void Entrar()
    {
        _texto = $"Um {_batalha.Inimigo.Nome} selvagem apareceu!";
        _visualInimigo.Pular(70f);
    }

    // ================================================================= atualizar

    public override void Atualizar(float dt)
    {
        _t += dt;
        _tempo += dt;
        _visualJogador.Atualizar(dt);
        _visualInimigo.Atualizar(dt);
        _particulas.Atualizar(dt);

        // Barras de vida deslizam até o valor novo em vez de pular.
        _vidaMostradaJogador = Aproximar(_vidaMostradaJogador, _vidaAlvoJogador, _batalha.Jogador.VidaMax * 1.2f * dt);
        _vidaMostradaInimigo = Aproximar(_vidaMostradaInimigo, _vidaAlvoInimigo, _batalha.Inimigo.VidaMax * 1.2f * dt);

        switch (_fase)
        {
            case Fase.Entrada:
                if (_t > 1.4f || (_t > 0.3f && Toque.TocouQualquer()))
                    IrParaEscolha();
                break;

            case Fase.Escolha:
                AtualizarEscolha();
                break;

            case Fase.Tocando:
                AtualizarEvento(dt);
                break;

            case Fase.Fim:
                if (Toque.Tocou(BotaoContinuar))
                    VoltarPraCasa();
                break;
        }
    }

    private static float Aproximar(float atual, float alvo, float passo)
        => atual < alvo ? MathF.Min(alvo, atual + passo) : MathF.Max(alvo, atual - passo);

    private void IrParaEscolha()
    {
        _fase = Fase.Escolha;
        _texto = $"O que {_batalha.Jogador.Nome} vai fazer?";
    }

    private void AtualizarEscolha()
    {
        var golpes = _batalha.Jogador.Golpes;
        for (int i = 0; i < golpes.Count; i++)
        {
            if (Toque.Tocou(BotaoGolpe(i)))
            {
                Tocar(_batalha.Turno(golpes[i]));
                return;
            }
        }

        if (Toque.Tocou(BotaoPocao))
        {
            if (Jogo.Progresso.Moedas < PrecoPocao)
                _texto = $"Poção custa {PrecoPocao} moedas. Você não tem o bastante.";
            else if (_batalha.Jogador.Vida >= _batalha.Jogador.VidaMax)
                _texto = "A vida já está cheia!";
            else
            {
                Jogo.Progresso.Moedas -= PrecoPocao;
                Tocar(_batalha.UsarPocao());
            }
        }
        else if (Toque.Tocou(BotaoFugir))
        {
            Tocar(_batalha.TentarFugir());
        }
    }

    private void Tocar(List<EventoBatalha> eventos)
    {
        foreach (var e in eventos)
            _fila.Enqueue(e);
        _fase = Fase.Tocando;
        ProximoEvento();
    }

    private void ProximoEvento()
    {
        // A investida pode acabar no meio de um frame: garante que o bicho volta pro lugar.
        if (_atual is Investida anterior)
            Visual(anterior.Quem).Deslocamento = Vector2.Zero;

        _t = 0f;
        if (!_fila.TryDequeue(out _atual))
        {
            _atual = null;
            if (_batalha.Acabou)
                Terminar();
            else
                IrParaEscolha();
            return;
        }

        switch (_atual)
        {
            case Mensagem m:
                _texto = m.Texto;
                break;
            case Dano d:
                Visual(d.Alvo).Flash();
                Visual(d.Alvo).Tremer(0.3f);
                DefinirVida(d.Alvo, d.VidaDepois);
                var cor = d.Efetividade > 1f ? Color.FromHex("#FFD54FFF") : Color.White;
                _particulas.Texto($"-{d.Valor}", Pe(d.Alvo) - new Vector2(0f, Tamanho(d.Alvo) * 0.85f), cor, grande: d.Efetividade > 1f);
                break;
            case Cura c:
                DefinirVida(c.Alvo, c.VidaDepois);
                _particulas.Explosao("coracao", Pe(c.Alvo) - new Vector2(0f, Tamanho(c.Alvo) * 0.4f), 6, 36f);
                if (c.Valor > 0)
                    _particulas.Texto($"+{c.Valor}", Pe(c.Alvo) - new Vector2(0f, Tamanho(c.Alvo) * 0.85f), Color.FromHex("#7CFFB0FF"));
                break;
            case MudouAtributo a:
                Visual(a.Alvo).Pular(a.Subiu ? 50f : 15f);
                _particulas.Explosao(a.Subiu ? "estrela" : "gota", Pe(a.Alvo) - new Vector2(0f, Tamanho(a.Alvo) * 0.4f), 6, 34f);
                break;
            case Errou e:
                _particulas.Texto("errou", Pe(Outro(e.Quem)) - new Vector2(0f, Tamanho(Outro(e.Quem)) * 0.85f), Color.FromHex("#CCCCCCFF"));
                break;
        }
    }

    private float DuracaoEvento => _atual switch
    {
        Mensagem => 1.1f,
        Investida => 0.4f,
        Dano => 0.55f,
        Cura => 0.6f,
        MudouAtributo => 0.5f,
        Desmaio => 0.9f,
        _ => 0.3f,
    };

    private void AtualizarEvento(float dt)
    {
        switch (_atual)
        {
            case Investida inv:
            {
                // Vai e volta na direção do outro, com a cor do tipo riscando no meio do caminho.
                float p = _t / 0.4f;
                float ida = MathF.Max(0f, p < 0.5f ? p * 2f : (1f - p) * 2f);
                var direcao = Vector2.Normalize(Pe(Outro(inv.Quem)) - Pe(inv.Quem));
                Visual(inv.Quem).Deslocamento = direcao * 90f * ida;
                break;
            }
            case Desmaio d:
                Visual(d.Quem).Opacidade = MathF.Max(0f, 1f - _t / 0.7f);
                Visual(d.Quem).Deslocamento = new Vector2(0f, _t * 80f);
                break;
        }

        // Mensagem pode ser adiantada com um toque; animação não (a vida precisa terminar de cair).
        bool adiantar = _atual is Mensagem && _t > 0.25f && Toque.TocouQualquer();
        if (_t >= DuracaoEvento || adiantar)
            ProximoEvento();
    }

    private void Terminar()
    {
        _fase = Fase.Fim;
        if (_recompensaDada)
            return;
        _recompensaDada = true;

        if (_batalha.Fugiu)
        {
            _texto = "Vocês voltaram pra casa.";
            B.Energia -= 5f;
            Jogo.Salvar();
            return;
        }

        B.Lutou(_batalha.Venceu);

        if (_batalha.Venceu)
        {
            var (xp, moedas) = _batalha.Recompensa();
            Jogo.Progresso.Moedas += moedas;
            var (niveis, golpes) = B.GanharXp(xp);

            var linhas = new List<string> { $"Vitória! +{xp} XP e +{moedas} moedas." };
            if (niveis.Count > 0)
                linhas.Add($"{_batalha.Jogador.Nome} subiu pro nível {B.Nivel}!");
            foreach (var g in golpes)
                linhas.Add($"Aprendeu {g.Nome}!");
            _texto = string.Join("\n", linhas);

            _visualJogador.Pular(80f);
            _particulas.Explosao("estrela", PeJogador - new Vector2(0f, 200f), 14, 46f);
        }
        else
        {
            _texto = $"{_batalha.Jogador.Nome} perdeu... Leve pra casa pra descansar.";
        }

        Jogo.Salvar();
    }

    private void VoltarPraCasa()
    {
        string fala = _batalha.Fugiu ? "Ufa!" : _batalha.Venceu ? "Ganhei!" : "Snif...";
        Jogo.IrPara(new TelaCasa(Jogo, 0, fala));
    }

    private BichoVisual Visual(Lado lado) => lado == Lado.Jogador ? _visualJogador : _visualInimigo;
    private static Vector2 Pe(Lado lado) => lado == Lado.Jogador ? PeJogador : PeInimigo;
    private static float Tamanho(Lado lado) => lado == Lado.Jogador ? LadoJogador : LadoInimigo;
    private static Lado Outro(Lado lado) => lado == Lado.Jogador ? Lado.Inimigo : Lado.Jogador;

    private void DefinirVida(Lado lado, int vida)
    {
        if (lado == Lado.Jogador) _vidaAlvoJogador = vida;
        else _vidaAlvoInimigo = vida;
    }

    // ================================================================= desenho

    public override void Desenhar(float dt)
    {
        Fundo("batalha");

        var inimigo = _batalha.Inimigo;
        var jogador = _batalha.Jogador;

        // Entrada: os dois deslizam de fora da tela pra plataforma.
        float entrada = _fase == Fase.Entrada ? MathF.Min(1f, _t / 0.6f) : 1f;
        float suave = 1f - (1f - entrada) * (1f - entrada);
        var desvioInimigo = new Vector2((1f - suave) * 400f, 0f);
        var desvioJogador = new Vector2(-(1f - suave) * 400f, 0f);

        _visualInimigo.Desenhar(Tinta, inimigo.Especie, inimigo.Estagio, PeInimigo + desvioInimigo, LadoInimigo);
        _visualJogador.Desenhar(Tinta, jogador.Especie, jogador.Estagio, PeJogador + desvioJogador, LadoJogador);

        if (_atual is Investida inv && inv.Tipo != Tipo.Normal)
            Rastro(inv);

        _particulas.Desenhar(Tinta);

        Placa(new Caixa(20f, 60f, 400f, 130f), inimigo, _vidaMostradaInimigo, mostrarNumeros: false);
        Placa(new Caixa(400f, 640f, 300f, 120f), jogador, _vidaMostradaJogador, mostrarNumeros: true);

        // Caixa de texto.
        Tinta.Cartao(CaixaTexto, Tinta.Papel, Tinta.Tinteiro, 4f, 22f);
        Tinta.Paragrafo(_texto, new Vector2(CaixaTexto.X + 26f, CaixaTexto.Y + 18f), CaixaTexto.L - 52f, Tinta.Tinteiro,
            escala: _texto.Contains('\n') ? 0.8f : 0.95f);

        if (_fase == Fase.Fim)
        {
            Tinta.Botao(BotaoContinuar, "Voltar pra casa", Color.FromHex("#7B61C9FF"), Toque.SegurandoEm(BotaoContinuar));
            return;
        }

        bool ativo = _fase == Fase.Escolha;
        var golpes = jogador.Golpes;
        for (int i = 0; i < 4; i++)
        {
            var c = BotaoGolpe(i);
            if (i >= golpes.Count)
            {
                Tinta.Painel(c, Color.FromHex("#00000030"), 20f);
                Tinta.Texto("---", new Vector2(c.Centro.X, c.Centro.Y - 14f), Color.White.WithAlpha(0.5f), alinhar: Tinta.Alinhar.Centro);
                continue;
            }

            var g = golpes[i];
            Tinta.Botao(c, "", Color.FromHex(Tipos.Cor(g.Tipo)), ativo && Toque.SegurandoEm(c), null, ativo);
            float afunda = ativo && Toque.SegurandoEm(c) ? 5f : 0f;
            Tinta.TextoContornado(g.Nome, new Vector2(c.Centro.X, c.Y + 12f + afunda), Color.White, escala: g.Nome.Length > 14 ? 0.85f : 1f);

            // Dica de efetividade contra esse adversário, como nos jogos novos.
            float ef = Tipos.Efetividade(g.Tipo, inimigo.Tipo);
            string dica = g.Poder == 0 ? "efeito" : ef > 1f ? "super eficaz" : ef < 1f ? "pouco eficaz" : $"poder {g.Poder}";
            Tinta.Texto(dica, new Vector2(c.Centro.X, c.Y + 50f + afunda), Color.White.WithAlpha(0.9f), escala: 0.75f, alinhar: Tinta.Alinhar.Centro);
        }

        Tinta.Botao(BotaoPocao, $"Poção {PrecoPocao}", Color.FromHex("#E0607EFF"), ativo && Toque.SegurandoEm(BotaoPocao), null, ativo, null);
        Tinta.Botao(BotaoFugir, "Fugir", Color.FromHex("#8C8098FF"), ativo && Toque.SegurandoEm(BotaoFugir), null, ativo);
    }

    /// <summary>Um risco colorido do tipo do golpe cortando do atacante até o alvo.</summary>
    private void Rastro(Investida inv)
    {
        float p = MathF.Min(1f, _t / 0.4f);
        var de = Pe(inv.Quem) - new Vector2(0f, Tamanho(inv.Quem) * 0.45f);
        var ate = Pe(Outro(inv.Quem)) - new Vector2(0f, Tamanho(Outro(inv.Quem)) * 0.45f);
        var cor = Color.FromHex(Tipos.Cor(inv.Tipo));
        for (int i = 0; i < 6; i++)
        {
            float s = Math.Clamp(p * 1.4f - i * 0.07f, 0f, 1f);
            var pos = Vector2.Lerp(de, ate, s);
            float raio = 26f - i * 3f;
            Tinta.Circulo(pos, raio + 4f, Color.White.WithAlpha(0.8f * (1f - p * 0.5f)));
            Tinta.Circulo(pos, raio, cor.WithAlpha(1f - p * 0.5f));
        }
    }

    private void Placa(Caixa c, Lutador l, float vidaMostrada, bool mostrarNumeros)
    {
        Tinta.Cartao(c, Tinta.Papel, Tinta.Tinteiro, 4f, 22f);
        Tinta.Texto(l.Nome, new Vector2(c.X + 22f, c.Y + 14f), Tinta.Tinteiro, escala: l.Nome.Length > 12 ? 0.85f : 1f);
        Tinta.Texto($"Nv {l.Nivel}", new Vector2(c.X + c.L - 22f, c.Y + 14f), Tinta.Tinteiro, alinhar: Tinta.Alinhar.Direita);

        var tipo = Color.FromHex(Tipos.Cor(l.Tipo));
        Tinta.Painel(new Caixa(c.X + 22f, c.Y + 52f, 90f, 28f), tipo, 14f);
        Tinta.Texto(Tipos.Nome(l.Tipo), new Vector2(c.X + 67f, c.Y + 54f), Color.White, escala: 0.75f, alinhar: Tinta.Alinhar.Centro);

        float fracao = vidaMostrada / MathF.Max(1f, l.VidaMax);
        var cor = fracao > 0.5f ? Color.FromHex("#4FC36AFF") : fracao > 0.2f ? Color.FromHex("#F2C230FF") : Color.FromHex("#E0463AFF");
        Tinta.Barra(new Caixa(c.X + 122f, c.Y + 54f, c.L - 144f, 24f), fracao, cor);

        if (mostrarNumeros)
            Tinta.Texto($"{(int)MathF.Ceiling(vidaMostrada)}/{l.VidaMax}", new Vector2(c.X + c.L - 22f, c.Y + 84f), Tinta.Tinteiro, escala: 0.8f, alinhar: Tinta.Alinhar.Direita);
        else if (l.ModAtaque != 0 || l.ModDefesa != 0)
            Tinta.Texto(Mods(l), new Vector2(c.X + c.L - 22f, c.Y + 88f), Color.FromHex("#6B5A6EFF"), escala: 0.75f, alinhar: Tinta.Alinhar.Direita);

        if (mostrarNumeros && (l.ModAtaque != 0 || l.ModDefesa != 0))
            Tinta.Texto(Mods(l), new Vector2(c.X + 22f, c.Y + 84f), Color.FromHex("#6B5A6EFF"), escala: 0.75f);
    }

    private static string Mods(Lutador l)
    {
        var partes = new List<string>();
        if (l.ModAtaque != 0) partes.Add($"Atq {(l.ModAtaque > 0 ? "+" : "")}{l.ModAtaque}");
        if (l.ModDefesa != 0) partes.Add($"Def {(l.ModDefesa > 0 ? "+" : "")}{l.ModDefesa}");
        return string.Join("  ", partes);
    }
}
