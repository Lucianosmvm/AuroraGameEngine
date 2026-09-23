using System.Numerics;
using Aurora.Runtime.Graphics;
using Silk.NET.Input;

namespace Bichinhos;

/// <summary>
/// A casa: o bicho andando pelo tapete, as necessidades no alto e os seis botões de cuidado
/// embaixo. É daqui que se vai brincar e lutar, e é aqui que a evolução dispara.
/// </summary>
public sealed class TelaCasa : Tela
{
    private enum Menu { Nenhum, Comida, Batalha, Ficha, Recomecar }

    private enum Botao { Comer, Brincar, Limpar, Remedio, Luz, Batalhar }

    private static readonly (Botao Id, string Icone, string Cor)[] Botoes =
    [
        (Botao.Comer, "maca", "#F08A3CFF"),
        (Botao.Brincar, "bola", "#3F95E0FF"),
        (Botao.Limpar, "bolhas", "#2FB3A3FF"),
        (Botao.Remedio, "pilula", "#E0607EFF"),
        (Botao.Luz, "lampada", "#7B61C9FF"),
        (Botao.Batalhar, "espadas", "#D6453DFF"),
    ];

    private static readonly Vector2[] PosicoesCoco =
        [new(100f, 905f), new(620f, 905f), new(110f, 805f), new(610f, 805f)];

    private readonly BichoVisual _visual = new();
    private readonly Particulas _particulas = new();
    private readonly double _horasFora;
    private readonly string? _falaInicial;

    private Menu _menu;
    private float _x = 360f;
    private float _alvoX = 360f;
    private float _pausa = 1.5f;
    private float _acao;          // segundos de animação de cuidado em andamento (trava os botões)
    private string? _comida;      // ícone na boca enquanto come
    private string _fala = "";
    private float _falaT;
    private float _tempoZ;
    private float _evoluir = -1f; // contagem pra ir pra tela de evolução
    private float _tempo;
    private float _cocosSumindo;  // animação de limpeza: cocôs antigos desbotando
    private int _cocosAntes;

    private const float PeY = 865f;
    private const float LadoBicho = 360f;

    /// <param name="horasFora">Tempo que o jogo ficou fechado (muda o "oi").</param>
    /// <param name="fala">O que o bicho diz ao voltar de brincar ou lutar.</param>
    public TelaCasa(BichinhosGame jogo, double horasFora, string? fala = null) : base(jogo)
    {
        _horasFora = horasFora;
        _falaInicial = fala;
    }

    private Bicho B => Jogo.Bicho!;

    public override void Entrar()
    {
        if (_falaInicial is not null)
            Falar(_falaInicial, 3f);
        else if (_horasFora >= 1.0)
            Falar(_horasFora >= 8 ? "Que saudade!!" : "Você voltou!");
        else
            Falar("Oi!");

        _visual.Pular(50f);
    }

    /// <summary>Só pro <c>--demo</c>: abre um dos menus pra tirar foto.</summary>
    internal void AbrirMenu(string nome) => _menu = nome switch
    {
        "comer" => Menu.Comida,
        "lutar" => Menu.Batalha,
        "ficha" => Menu.Ficha,
        _ => Menu.Nenhum,
    };

    public void Falar(string texto, float segundos = 2.4f)
    {
        _fala = texto;
        _falaT = segundos;
    }

    // ================================================================= layout

    private static Caixa CaixaBotao(int i)
    {
        int col = i % 3, lin = i / 3;
        return new Caixa(30f + col * 230f, 960f + lin * 138f, 200f, 120f);
    }

    private static readonly Caixa Topo = new(20f, 24f, 680f, 300f);
    private static readonly Caixa BotaoFicha = new(386f, 262f, 290f, 50f);
    private static readonly Caixa Janela = new(60f, 330f, 600f, 560f);
    private static readonly Caixa Ficha = new(40f, 150f, 640f, 800f);
    private static readonly Caixa FichaRecomecar = new(Ficha.X + 40f, Ficha.Y + Ficha.A - 110f, 250f, 80f);
    private static readonly Caixa FichaFechar = new(Ficha.X + Ficha.L - 290f, Ficha.Y + Ficha.A - 110f, 250f, 80f);

    private static Caixa CaixaJanelinha(int linhas) => new(Janela.X, Janela.Y, Janela.L, 150f + linhas * 110f);

    private Vector2 Pe => new(_x, PeY);
    private Caixa CorpoBicho => new(_x - LadoBicho * 0.33f, PeY - LadoBicho * 0.8f, LadoBicho * 0.66f, LadoBicho * 0.8f);

    // ================================================================= atualizar

    public override void Atualizar(float dt)
    {
        _tempo += dt;
        _visual.Atualizar(dt);
        _particulas.Atualizar(dt);
        _falaT -= dt;
        _acao = MathF.Max(0f, _acao - dt);
        _cocosSumindo = MathF.Max(0f, _cocosSumindo - dt);
        if (_acao <= 0f)
            _comida = null;

        if (_evoluir >= 0f)
        {
            _evoluir -= dt;
            if (_evoluir < 0f)
                Jogo.IrPara(new TelaEvolucao(Jogo));
            return;
        }

        if (B.Dormindo)
        {
            _tempoZ -= dt;
            if (_tempoZ <= 0f)
            {
                _tempoZ = 1.1f;
                _particulas.Texto("Z", Pe + new Vector2(60f, -LadoBicho * 0.7f), Color.FromHex("#D8E4FFFF"), grande: true, duracao: 2f);
            }
        }
        else
        {
            Passear(dt);
        }

        if (_menu != Menu.Nenhum)
        {
            AtualizarMenu();
            return;
        }

        if (_acao <= 0f && B.EvolucaoPendente)
        {
            Falar("Hã? Tá acontecendo alguma coisa!", 2f);
            _visual.Tremer(1.5f);
            _evoluir = 1.6f;
            return;
        }

        for (int i = 0; i < Botoes.Length; i++)
        {
            if (Toque.Tocou(CaixaBotao(i)))
                Acionar(Botoes[i].Id);
        }

        if (Toque.Tocou(BotaoFicha))
            _menu = Menu.Ficha;

        if (Toque.Tocou(CorpoBicho))
            Carinho();

        // Esc fecha no PC. No Android quem fecha é o sistema (voltar/home), e o save já está em dia.
        if (Toque.Tecla(Key.Escape) && !OperatingSystem.IsAndroid())
            Jogo.Exit();
    }

    private void Passear(float dt)
    {
        if (_acao > 0f)
            return;

        if (MathF.Abs(_alvoX - _x) < 2f)
        {
            _pausa -= dt;
            if (_pausa <= 0f)
            {
                _alvoX = 230f + (float)Jogo.Rng.NextDouble() * 260f;
                _pausa = 2f + (float)Jogo.Rng.NextDouble() * 3f;
            }
            return;
        }

        // Anda aos pulinhos: cada pulinho avança um pouco, que nem bichinho de LCD.
        float velocidade = B.Doente ? 30f : 70f;
        float passo = MathF.Sign(_alvoX - _x) * MathF.Min(MathF.Abs(_alvoX - _x), velocidade * dt);
        _x += passo;
        _visual.Espelhar = passo < 0f;
        if (!_visual.NoAr && !B.Doente)
            _visual.Pular(18f);
    }

    private void Acionar(Botao botao)
    {
        if (_acao > 0f)
            return;

        if (B.Dormindo && botao != Botao.Luz)
        {
            Falar("Zzz...");
            return;
        }

        switch (botao)
        {
            case Botao.Comer:
                _menu = Menu.Comida;
                break;

            case Botao.Brincar:
                if (B.Energia < 10f)
                    Falar("Tô cansado...");
                else if (B.Doente)
                    Falar("Não tô bem pra brincar...");
                else
                    Jogo.IrPara(new TelaBrincar(Jogo));
                break;

            case Botao.Limpar:
                if (B.Cocos == 0 && B.Higiene > 90f)
                {
                    Falar("Já tô limpinho!");
                    break;
                }
                _cocosAntes = B.Cocos;
                _cocosSumindo = 0.8f;
                bool sujo = B.Cocos > 0 || B.Higiene < 60f;
                B.Limpar();
                _acao = 1f;
                _particulas.Explosao("bolhas", Pe - new Vector2(0f, 150f), 12, 50f);
                foreach (var p in PosicoesCoco.Take(_cocosAntes))
                    _particulas.Explosao("bolhas", p, 4, 36f);
                Falar("Cheiroso!");
                _visual.Pular();
                if (sujo) DarXp(3);
                break;

            case Botao.Remedio:
                if (B.TomarRemedio(out string falaRemedio))
                {
                    _comida = "pilula";
                    _acao = 1.2f;
                    _visual.Tremer(0.8f);
                    DarXp(2);
                }
                Falar(falaRemedio);
                break;

            case Botao.Luz:
                B.Dormindo = !B.Dormindo;
                Falar(B.Dormindo ? "Boa noite..." : "Bom dia!");
                if (!B.Dormindo)
                    _visual.Pular();
                Jogo.Salvar();
                break;

            case Botao.Batalhar:
                _menu = Menu.Batalha;
                break;
        }
    }

    private void Carinho()
    {
        if (_acao > 0f)
            return;

        if (B.Dormindo)
        {
            Falar("Zzz... hmm...");
            return;
        }

        B.Carinho();
        _visual.Pular(40f);
        _particulas.Icone("coracao", Pe - new Vector2(0f, LadoBicho * 0.8f), 54f);
        if (Jogo.Rng.Next(3) == 0)
            Falar(B.Alegria > 80f ? "Hihi!" : "Mais!", 1.2f);
    }

    private void Comer(bool doce)
    {
        bool comeu;
        string fala;
        if (doce)
        {
            if (Jogo.Progresso.Moedas < PrecoDoce)
            {
                Falar("Sem moedas pro doce...");
                return;
            }
            comeu = B.ComerDoce(out fala);
            if (comeu) Jogo.Progresso.Moedas -= PrecoDoce;
        }
        else
        {
            comeu = B.Comer(out fala);
        }

        Falar(fala);
        _menu = Menu.Nenhum;
        if (!comeu)
        {
            _visual.Tremer();
            return;
        }

        _comida = doce ? "doce" : "maca";
        _acao = 1.4f;
        _alvoX = _x;
        _visual.Mastigar(1.3f);
        DarXp(doce ? 2 : 3);
        Jogo.Salvar();
    }

    private const int PrecoDoce = 3;

    /// <summary>XP de cuidado. Pouco por ação — o grosso vem das batalhas — mas um bicho bem
    /// cuidado sobe de nível mesmo sem lutar.</summary>
    private void DarXp(int xp)
    {
        var (niveis, golpes) = B.GanharXp(xp);
        if (niveis.Count == 0)
            return;

        _particulas.Explosao("estrela", Pe - new Vector2(0f, 200f), 10, 44f);
        _particulas.Texto($"Nível {B.Nivel}!", Pe - new Vector2(0f, 420f), Color.FromHex("#FFE27AFF"), grande: true, duracao: 2f);
        Falar(golpes.Count > 0 ? $"Aprendi {golpes[^1].Nome}!" : $"Subi pro nível {B.Nivel}!");
        Jogo.Salvar();
    }

    // ================================================================= menus

    private Caixa BotaoMenu(int i) => new(Janela.X + 50f, Janela.Y + 150f + i * 110f, Janela.L - 100f, 90f);

    private void AtualizarMenu()
    {
        switch (_menu)
        {
            case Menu.Comida:
                if (Toque.Tocou(BotaoMenu(0))) Comer(doce: false);
                else if (Toque.Tocou(BotaoMenu(1))) Comer(doce: true);
                else if (Toque.Tocou(BotaoMenu(3))) _menu = Menu.Nenhum;
                break;

            case Menu.Batalha:
            {
                bool pode = B.PodeBatalhar(out _);
                for (int i = 0; i < 3; i++)
                {
                    if (pode && Toque.Tocou(BotaoMenu(i)))
                    {
                        var dificuldade = (Dificuldade)i;
                        var inimigo = Batalha.SortearInimigo(B.Nivel, dificuldade, Jogo.Rng);
                        Jogo.IrPara(new TelaBatalha(Jogo, new Batalha(Lutador.De(B), inimigo, dificuldade, Jogo.Rng)));
                        return;
                    }
                }
                if (Toque.Tocou(BotaoMenu(3))) _menu = Menu.Nenhum;
                break;
            }

            case Menu.Ficha:
                if (Toque.Tocou(FichaRecomecar))
                    _menu = Menu.Recomecar;
                else if (Toque.Tocou(FichaFechar))
                    _menu = Menu.Nenhum;
                break;

            case Menu.Recomecar:
                if (Toque.Tocou(BotaoMenu(1)))
                    Jogo.Recomecar();
                else if (Toque.Tocou(BotaoMenu(2)))
                    _menu = Menu.Nenhum;
                break;
        }

        // Toque fora da janela fecha (menos na confirmação, que precisa de resposta).
        bool fechaPorFora = _menu is Menu.Comida or Menu.Batalha or Menu.Ficha;
        var janela = _menu == Menu.Ficha ? Ficha : CaixaJanelinha(4);
        if (fechaPorFora && Toque.Apertou && !janela.Contem(Toque.Posicao) && Toque.TocouQualquer())
            _menu = Menu.Nenhum;
    }

    // ================================================================= desenho

    public override void Desenhar(float dt)
    {
        Fundo("casa");

        bool noite = B.Dormindo;
        if (noite)
            Tinta.Retangulo(new Caixa(0, 0, BichinhosGame.Largura, BichinhosGame.Altura), Color.FromHex("#0B1030A0"));

        DesenharCocos(noite);

        var tom = noite ? Color.FromHex("#8C93C8FF") : Color.White;
        _visual.Desenhar(Tinta, B.Especie, B.Estagio, Pe, LadoBicho, B.Dormindo, B.Doente, B.Alegria < 25f && !B.Doente, tom);

        if (_comida is not null)
        {
            float p = 1f - _acao / 1.4f;
            Tinta.Icone(_comida, Pe - new Vector2(-10f, LadoBicho * 0.45f), 90f * MathF.Max(0.15f, 1f - p));
        }

        if (B.Doente && !noite)
            Tinta.Icone("gota", Pe + new Vector2(LadoBicho * 0.3f, -LadoBicho * 0.75f + MathF.Sin(_tempo * 3f) * 6f), 44f);

        _particulas.Desenhar(Tinta);
        DesenharBalao();
        DesenharTopo();

        for (int i = 0; i < Botoes.Length; i++)
            DesenharBotao(i);

        if (_menu != Menu.Nenhum)
            DesenharMenu();
    }

    private void DesenharCocos(bool noite)
    {
        var textura = Tinta.Textura("sprites/icones/coco.png");
        var tom = noite ? Color.FromHex("#8C93C8FF") : Color.White;

        for (int i = 0; i < B.Cocos; i++)
        {
            float balanca = MathF.Sin(_tempo * 3f + i) * 0.05f;
            Tinta.Sprite(textura, PosicoesCoco[i], new Vector2(84f), new Vector2(0.5f, 0.85f), tom, balanca);
            // Cheirinho subindo.
            float s = (_tempo * 0.8f + i * 0.3f) % 1f;
            Tinta.Elipse(PosicoesCoco[i] + new Vector2(MathF.Sin(s * 9f) * 8f, -70f - s * 40f), new Vector2(5f), Color.FromHex("#9BB06AFF").WithAlpha(1f - s));
        }

        if (_cocosSumindo > 0f)
        {
            for (int i = 0; i < _cocosAntes; i++)
                Tinta.Sprite(textura, PosicoesCoco[i], new Vector2(84f * _cocosSumindo / 0.8f), new Vector2(0.5f, 0.85f), tom.WithAlpha(_cocosSumindo / 0.8f));
        }
    }

    private void DesenharBalao()
    {
        var cabeca = Pe - new Vector2(0f, LadoBicho * 0.92f);

        if (_falaT > 0f && _fala.Length > 0)
        {
            var tamanho = Tinta.Medir(_fala);
            float largura = MathF.Max(120f, tamanho.X + 44f);
            float x = Math.Clamp(cabeca.X - largura / 2f, 20f, BichinhosGame.Largura - 20f - largura);
            var caixa = new Caixa(x, cabeca.Y - 90f, largura, 66f);
            float entra = MathF.Min(1f, (2.4f - _falaT) * 8f + 0.3f);
            Tinta.Cartao(caixa, Color.White.WithAlpha(entra), Tinta.Tinteiro.WithAlpha(entra), 3.5f, 26f);
            Tinta.Texto(_fala, new Vector2(caixa.Centro.X, caixa.Y + 17f), Tinta.Tinteiro.WithAlpha(entra), alinhar: Tinta.Alinhar.Centro);
            return;
        }

        if (B.Dormindo || _acao > 0f || B.Urgente is not { } urgente)
            return;

        string icone = urgente switch
        {
            Necessidade.Comida => "maca",
            Necessidade.Banho => "bolhas",
            Necessidade.Remedio => "pilula",
            Necessidade.Sono => "lampada",
            _ => "coracao",
        };

        var centro = cabeca + new Vector2(110f, -50f + MathF.Sin(_tempo * 4f) * 6f);
        Tinta.Circulo(centro, 48f, Tinta.Tinteiro);
        Tinta.Circulo(centro, 44f, Color.White);
        Tinta.Circulo(centro + new Vector2(-40f, 40f), 12f, Tinta.Tinteiro);
        Tinta.Circulo(centro + new Vector2(-40f, 40f), 9f, Color.White);
        Tinta.Icone(icone, centro, 58f);
    }

    private void DesenharTopo()
    {
        Tinta.Cartao(Topo, Tinta.Papel.WithAlpha(0.96f), Tinta.Tinteiro, 4f, 28f);

        var tipo = Color.FromHex(Tipos.Cor(B.Especie.Tipo));
        Tinta.Texto(B.Nome, new Vector2(44f, 40f), Tinta.Tinteiro, Tinta.FonteMedia);

        // Moedas no canto.
        string moedas = Jogo.Progresso.Moedas.ToString();
        float larguraMoedas = Tinta.Medir(moedas, Tinta.FonteMedia).X;
        Tinta.Icone("moeda", new Vector2(660f - larguraMoedas - 30f, 62f), 44f);
        Tinta.Texto(moedas, new Vector2(670f, 40f), Tinta.Tinteiro, Tinta.FonteMedia, alinhar: Tinta.Alinhar.Direita);

        // Linha do nível: tipo, nível e barra de XP.
        Tinta.Painel(new Caixa(44f, 98f, 120f, 38f), tipo, 19f);
        Tinta.Texto(Tipos.Nome(B.Especie.Tipo), new Vector2(104f, 102f), Color.White, alinhar: Tinta.Alinhar.Centro);
        Tinta.Texto($"Nv {B.Nivel}", new Vector2(180f, 102f), Tinta.Tinteiro);
        float fracaoXp = B.NivelMaximo ? 1f : B.Xp / (float)B.XpParaProximo;
        Tinta.Barra(new Caixa(270f, 104f, 406f, 28f), fracaoXp, Color.FromHex("#7B61C9FF"));
        Tinta.Texto(B.NivelMaximo ? "MÁX" : $"XP {B.Xp}/{B.XpParaProximo}", new Vector2(473f, 104f), Color.White, escala: 0.8f, alinhar: Tinta.Alinhar.Centro);

        (string Icone, string Nome, float Valor, string Cor)[] barras =
        [
            ("maca", "Fome", B.Fome, "#F08A3CFF"),
            ("coracao", "Alegria", B.Alegria, "#FF5C7AFF"),
            ("raio", "Energia", B.Energia, "#F2C230FF"),
            ("bolhas", "Higiene", B.Higiene, "#2FB3A3FF"),
            ("cruz", "Saúde", B.Saude, "#4FC3A1FF"),
        ];

        for (int i = 0; i < barras.Length; i++)
        {
            var (icone, _, valor, cor) = barras[i];
            float x = 44f + (i % 2) * 336f;
            float y = 158f + (i / 2) * 56f;
            Tinta.Icone(icone, new Vector2(x + 22f, y + 20f), 44f);
            // Barra fica vermelha quando está baixa, pra chamar o olho.
            var c = valor < 25f ? Color.FromHex("#E0463AFF") : Color.FromHex(cor);
            Tinta.Barra(new Caixa(x + 52f, y + 6f, 244f, 30f), valor / 100f, c);
        }

        bool apertado = Toque.SegurandoEm(BotaoFicha);
        Tinta.Botao(BotaoFicha, "Ficha", Color.FromHex("#9A7BD6FF"), apertado, "estrela", _menu == Menu.Nenhum);
    }

    private void DesenharBotao(int i)
    {
        var (id, icone, cor) = Botoes[i];
        var c = CaixaBotao(i);
        string rotulo = id switch
        {
            Botao.Comer => "Comer",
            Botao.Brincar => "Brincar",
            Botao.Limpar => "Limpar",
            Botao.Remedio => "Remédio",
            Botao.Luz => B.Dormindo ? "Acordar" : "Dormir",
            _ => "Batalhar",
        };

        bool ativo = _menu == Menu.Nenhum && _acao <= 0f && _evoluir < 0f && (!B.Dormindo || id == Botao.Luz);
        Tinta.Botao(c, rotulo, Color.FromHex(cor), ativo && Toque.SegurandoEm(c), icone, ativo);
    }

    private void DesenharMenu()
    {
        Tinta.Retangulo(new Caixa(0, 0, BichinhosGame.Largura, BichinhosGame.Altura), Color.FromHex("#1A102AA0"));

        switch (_menu)
        {
            case Menu.Comida:
                Janelinha("Hora de comer", 4);
                Tinta.Botao(BotaoMenu(0), "Refeição", Color.FromHex("#F08A3CFF"), Toque.SegurandoEm(BotaoMenu(0)), "maca");
                Tinta.Botao(BotaoMenu(1), $"Doce ({PrecoDoce} moedas)", Color.FromHex("#E0607EFF"), Toque.SegurandoEm(BotaoMenu(1)), "doce",
                    Jogo.Progresso.Moedas >= PrecoDoce);
                Tinta.Paragrafo("Refeição mata a fome. Doce deixa feliz, mas não pode exagerar.",
                    new Vector2(Janela.Centro.X, BotaoMenu(2).Y + 8f), Janela.L - 100f, Color.FromHex("#6B5A6EFF"), escala: 0.85f, alinhar: Tinta.Alinhar.Centro);
                Tinta.Botao(BotaoMenu(3), "Voltar", Color.FromHex("#8C8098FF"), Toque.SegurandoEm(BotaoMenu(3)));
                break;

            case Menu.Batalha:
            {
                Janelinha("Batalhar", 4);
                bool pode = B.PodeBatalhar(out string motivo);
                string[] nomes = ["Mato (fácil)", "Campo (normal)", "Arena (difícil)"];
                string[] cores = ["#4FA83FFF", "#E0A030FF", "#D6453DFF"];
                for (int i = 0; i < 3; i++)
                    Tinta.Botao(BotaoMenu(i), nomes[i], Color.FromHex(cores[i]), pode && Toque.SegurandoEm(BotaoMenu(i)), "espadas", pode);
                if (!pode)
                    Tinta.Paragrafo(motivo, new Vector2(Janela.Centro.X, Janela.Y + 88f), Janela.L - 80f, Color.FromHex("#C0392BFF"), escala: 0.85f, alinhar: Tinta.Alinhar.Centro);
                else
                    Tinta.Texto("Quanto mais difícil, mais XP e moedas.", new Vector2(Janela.Centro.X, Janela.Y + 92f), Color.FromHex("#6B5A6EFF"), escala: 0.85f, alinhar: Tinta.Alinhar.Centro);
                Tinta.Botao(BotaoMenu(3), "Voltar", Color.FromHex("#8C8098FF"), Toque.SegurandoEm(BotaoMenu(3)));
                break;
            }

            case Menu.Ficha:
                DesenharFicha();
                break;

            case Menu.Recomecar:
                Janelinha("Recomeçar?", 3);
                Tinta.Paragrafo($"{B.Nome} vai embora e você escolhe um ovo novo. Não dá pra desfazer.",
                    new Vector2(Janela.Centro.X, Janela.Y + 110f), Janela.L - 100f, Tinta.Tinteiro, alinhar: Tinta.Alinhar.Centro);
                Tinta.Botao(BotaoMenu(1), "Sim, recomeçar", Color.FromHex("#D6453DFF"), Toque.SegurandoEm(BotaoMenu(1)));
                Tinta.Botao(BotaoMenu(2), "Não", Color.FromHex("#8C8098FF"), Toque.SegurandoEm(BotaoMenu(2)));
                break;
        }
    }

    private void Janelinha(string titulo, int linhas)
    {
        var c = CaixaJanelinha(linhas);
        Tinta.Cartao(c, Tinta.Papel, Tinta.Tinteiro, 5f, 30f);
        Tinta.Texto(titulo, new Vector2(c.Centro.X, c.Y + 28f), Tinta.Tinteiro, Tinta.FonteMedia, alinhar: Tinta.Alinhar.Centro);
    }

    private void DesenharFicha()
    {
        var c = Ficha;
        Tinta.Cartao(c, Tinta.Papel, Tinta.Tinteiro, 5f, 30f);

        var tipo = Color.FromHex(Tipos.Cor(B.Especie.Tipo));
        Tinta.Texto(B.Nome, new Vector2(c.X + 40f, c.Y + 30f), Tinta.Tinteiro, Tinta.FonteMedia);
        Tinta.Texto($"Nível {B.Nivel}  ·  {Tipos.Nome(B.Especie.Tipo)}", new Vector2(c.X + 40f, c.Y + 84f), tipo);
        int dias = (int)(B.HorasDeVida / 24.0);
        Tinta.Texto($"{dias} {(dias == 1 ? "dia" : "dias")} de vida  ·  {B.Vitorias} vitórias, {B.Derrotas} derrotas",
            new Vector2(c.X + 40f, c.Y + 122f), Color.FromHex("#6B5A6EFF"), escala: 0.85f);

        Tinta.Sprite(Tinta.Textura(B.Especie.Sprite(B.Estagio)), new Vector2(c.X + c.L - 110f, c.Y + 160f), new Vector2(180f),
            new Vector2(0.5f, 0.9f), Color.White);

        var a = B.Atributos;
        (string, int, int)[] stats = [("Vida", a.Vida, 160), ("Ataque", a.Ataque, 90), ("Defesa", a.Defesa, 90), ("Velocidade", a.Velocidade, 90)];
        float y = c.Y + 190f;
        foreach (var (nome, valor, max) in stats)
        {
            Tinta.Texto(nome, new Vector2(c.X + 40f, y), Tinta.Tinteiro);
            Tinta.Barra(new Caixa(c.X + 220f, y + 2f, 300f, 28f), valor / (float)max, tipo);
            Tinta.Texto(valor.ToString(), new Vector2(c.X + c.L - 40f, y), Tinta.Tinteiro, alinhar: Tinta.Alinhar.Direita);
            y += 48f;
        }

        y += 14f;
        Tinta.Texto("Golpes", new Vector2(c.X + 40f, y), Tinta.Tinteiro, Tinta.FonteMedia, 0.8f);
        y += 46f;
        foreach (var g in B.Golpes)
        {
            Tinta.Painel(new Caixa(c.X + 40f, y, c.L - 80f, 50f), Color.FromHex(Tipos.Cor(g.Tipo)).WithAlpha(0.2f), 16f);
            Tinta.Texto(g.Nome, new Vector2(c.X + 60f, y + 10f), Tinta.Tinteiro);
            string detalhe = g.Poder > 0 ? $"{Tipos.Nome(g.Tipo)} · poder {g.Poder}" : Efeito(g.Efeito);
            Tinta.Texto(detalhe, new Vector2(c.X + c.L - 60f, y + 12f), Color.FromHex("#6B5A6EFF"), escala: 0.8f, alinhar: Tinta.Alinhar.Direita);
            y += 58f;
        }

        var proximo = B.Especie.Golpes.FirstOrDefault(g => g.Nivel > B.Nivel);
        int? evolucao = Catalogo.Atual.NivelEvolucao.Where(n => n > B.Nivel).Cast<int?>().FirstOrDefault();
        string dica = evolucao is { } nv && B.Estagio < 2 ? $"Evolui no nível {nv}." : "Forma final!";
        if (proximo is not null)
            dica += $"  Aprende {Catalogo.Atual.Golpe(proximo.Golpe).Nome} no nível {proximo.Nivel}.";
        Tinta.Paragrafo(dica, new Vector2(c.X + 40f, y + 6f), c.L - 80f, Color.FromHex("#6B5A6EFF"), escala: 0.8f);

        Tinta.Botao(FichaRecomecar, "Recomeçar", Color.FromHex("#B0707EFF"), Toque.SegurandoEm(FichaRecomecar));
        Tinta.Botao(FichaFechar, "Fechar", Color.FromHex("#7B61C9FF"), Toque.SegurandoEm(FichaFechar));
    }

    private static string Efeito(EfeitoGolpe e) => e switch
    {
        EfeitoGolpe.Curar => "recupera vida",
        EfeitoGolpe.SubirAtaque => "sobe o ataque",
        EfeitoGolpe.SubirDefesa => "sobe a defesa",
        EfeitoGolpe.BaixarAtaque => "baixa o ataque do outro",
        _ => "",
    };
}
