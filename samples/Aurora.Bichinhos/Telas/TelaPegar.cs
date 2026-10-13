using System.Numerics;
using Aurora.Runtime.Graphics;

namespace Bichinhos;

/// <summary>
/// "Pegar frutas": a brincadeira de habilidade. Frutas, doces e estrelas caem; o bichinho corre
/// debaixo delas seguindo o dedo. Cocô tira pontos. A regra está em <see cref="PegaFrutas"/>; aqui
/// só entram o toque, o desenho e os sons.
/// </summary>
public sealed class TelaPegar : Tela
{
    private enum Fase { Explicando, Jogando, Fim }

    private static readonly Caixa BotaoComecar = new(160f, 880f, 400f, 110f);
    private static readonly Caixa BotaoVoltar = new(160f, 1040f, 400f, 100f);
    private const float PeY = 1190f;
    private const float LadoBicho = 300f;

    private readonly BichoVisual _visual = new();
    private readonly Particulas _particulas = new();
    private readonly PegaFrutas _jogo;
    private Fase _fase = Fase.Explicando;
    private float _alvoX = PegaFrutas.Largura / 2f;
    private float _tempoTictac;
    private string _resultado = "";
    private bool _recorde;

    public TelaPegar(BichinhosGame jogo) : base(jogo) => _jogo = new PegaFrutas(jogo.Rng);

    private Bicho B => Jogo.Bicho!;

    public override void Entrar()
    {
        Jogo.Musica("musica_batalha");
        _visual.Roupa = Jogo.Progresso.Roupa;
    }

    /// <summary>Só pro <c>--demo pegar</c>: já com frutas caindo, pra tirar foto.</summary>
    internal void ParaDemo(float segundos)
    {
        _fase = Fase.Jogando;
        for (float t = 0f; t < segundos; t += 1f / 30f)
            _jogo.Atualizar(1f / 30f, PegaFrutas.Largura / 2f + MathF.Sin(t * 2f) * 220f);
        _jogo.DrenarEventos();
    }

    public override void Atualizar(float dt)
    {
        _visual.Atualizar(dt);
        _particulas.Atualizar(dt);

        switch (_fase)
        {
            case Fase.Explicando:
                if (Toque.Tocou(BotaoComecar))
                    _fase = Fase.Jogando;
                break;

            case Fase.Jogando:
                Jogar(dt);
                break;

            case Fase.Fim:
                if (Toque.Tocou(BotaoVoltar))
                {
                    Jogo.Salvar();
                    Jogo.IrPara(new TelaCasa(Jogo, 0, _jogo.Pontos >= PegaFrutas.PontosParaGanhar ? "Que divertido!" : "Brinca de novo?"));
                }
                break;
        }
    }

    private void Jogar(float dt)
    {
        // O bicho segue o dedo (ou o mouse apertado). Soltou, fica onde está.
        if (Toque.Segurando)
            _alvoX = Toque.Posicao.X;

        _jogo.Atualizar(dt, _alvoX);
        _visual.Espelhar = _alvoX < _jogo.BichoX - 8f;

        foreach (var e in _jogo.DrenarEventos())
            Reagir(e);

        // Tique-taque nos últimos segundos, pra o jogador sentir o tempo acabando.
        if (_jogo.Restante is > 0f and <= 5f)
        {
            _tempoTictac -= dt;
            if (_tempoTictac <= 0f)
            {
                _tempoTictac = 0.5f;
                Som("tictac", 0.8f);
            }
        }

        if (_jogo.Terminou)
            Terminar();
    }

    private void Reagir(EventoPegar e)
    {
        if (!e.Pegou)
            return;

        if (e.Tipo == TipoItem.Coco)
        {
            Som("eca", 0.8f);
            _visual.Tremer(0.4f);
            _particulas.Texto("-2", e.Posicao - new Vector2(0f, 60f), Color.FromHex("#FF8A8AFF"), grande: true);
            return;
        }

        Som(e.Tipo == TipoItem.Maca ? "pegar" : "pegar_doce", 0.9f, 0.95f + (float)Jogo.Rng.NextDouble() * 0.15f);
        _visual.Pular(e.Tipo == TipoItem.Maca ? 18f : 40f);
        _visual.Mastigar(0.25f);
        _particulas.Texto($"+{e.Pontos}", e.Posicao - new Vector2(0f, 60f),
            e.Tipo == TipoItem.Maca ? Color.White : Color.FromHex("#FFE27AFF"), grande: e.Tipo != TipoItem.Maca);
        if (e.Tipo == TipoItem.Estrela)
            _particulas.Explosao("estrela", e.Posicao, 8, 40f);
    }

    private void Terminar()
    {
        _fase = Fase.Fim;

        int pontos = _jogo.Pontos;
        var (moedas, xp, ganhou) = PegaFrutas.Recompensa(pontos);
        B.Brincou(ganhou);
        var (niveis, _) = B.GanharXp(xp);
        Jogo.Progresso.Moedas += moedas;

        _recorde = pontos > Jogo.Progresso.RecordePegar;
        if (_recorde)
            Jogo.Progresso.RecordePegar = pontos;

        _resultado = $"{pontos} pontos\n+{moedas} moedas e +{xp} XP";
        if (niveis.Count > 0)
            _resultado += $"\nSubiu pro nível {B.Nivel}!";

        Som(ganhou ? "vitoria" : "carinho");
        if (ganhou)
        {
            _visual.Pular(90f);
            _particulas.Explosao("estrela", new Vector2(_jogo.BichoX, PeY - 220f), 14, 44f);
        }

        Jogo.Salvar();
    }

    // ================================================================= desenho

    public override void Desenhar(float dt)
    {
        Fundo("casa");
        Tinta.Retangulo(new Caixa(0, 0, BichinhosGame.Largura, BichinhosGame.Altura), Color.FromHex("#2A1E4450"));

        DesenharPlacar();

        foreach (var item in _jogo.Itens)
            Tinta.Icone(NomeDoIcone(item.Tipo), new Vector2(item.X, item.Y), 84f, rotacao: MathF.Sin(item.Y * 0.02f + item.X) * 0.25f);

        _visual.Desenhar(Tinta, B.Especie, B.Estagio, new Vector2(_jogo.BichoX, PeY), LadoBicho);
        _particulas.Desenhar(Tinta);

        switch (_fase)
        {
            case Fase.Explicando:
                DesenharExplicacao();
                break;
            case Fase.Fim:
                DesenharResultado();
                break;
        }
    }

    private static string NomeDoIcone(TipoItem tipo) => tipo switch
    {
        TipoItem.Maca => "maca",
        TipoItem.Doce => "doce",
        TipoItem.Estrela => "estrela",
        _ => "coco",
    };

    private void DesenharPlacar()
    {
        var topo = new Caixa(40f, 40f, 640f, 150f);
        Tinta.Cartao(topo, Tinta.Papel, Tinta.Tinteiro, 4f, 28f);
        Tinta.Texto($"Pontos: {_jogo.Pontos}", new Vector2(70f, 58f), Tinta.Tinteiro, Tinta.FonteMedia);
        Tinta.Texto($"Recorde {Math.Max(Jogo.Progresso.RecordePegar, _fase == Fase.Fim ? _jogo.Pontos : 0)}",
            new Vector2(650f, 66f), Color.FromHex("#6B5A6EFF"), alinhar: Tinta.Alinhar.Direita);

        float restante = _jogo.Restante / PegaFrutas.Duracao;
        var cor = _jogo.Restante <= 5f ? Color.FromHex("#E0463AFF") : Color.FromHex("#4FC3A1FF");
        Tinta.Barra(new Caixa(70f, 124f, 580f, 36f), restante, cor);
        Tinta.Texto($"{MathF.Ceiling(_jogo.Restante)}s", new Vector2(360f, 128f), Color.White, escala: 0.85f, alinhar: Tinta.Alinhar.Centro);
    }

    private void DesenharExplicacao()
    {
        Tinta.Retangulo(new Caixa(0, 0, BichinhosGame.Largura, BichinhosGame.Altura), Color.FromHex("#1A102A90"));

        var c = new Caixa(50f, 250f, 620f, 560f);
        Tinta.Cartao(c, Tinta.Papel, Tinta.Tinteiro, 5f, 30f);
        Tinta.Texto("Pegar frutas", new Vector2(c.Centro.X, c.Y + 28f), Tinta.Tinteiro, Tinta.FonteMedia, alinhar: Tinta.Alinhar.Centro);
        Tinta.Paragrafo("Arraste o dedo para o bichinho correr de um lado para o outro. Pegue tudo que cair, mas fuja do cocô!",
            new Vector2(c.Centro.X, c.Y + 90f), 540f, Color.FromHex("#6B5A6EFF"), alinhar: Tinta.Alinhar.Centro);

        (string Icone, string Texto)[] linhas = [("maca", "maçã  +1"), ("doce", "doce  +3"), ("estrela", "estrela  +5"), ("coco", "cocô  -2")];
        for (int i = 0; i < linhas.Length; i++)
        {
            float x = c.X + 60f + (i % 2) * 280f;
            float y = c.Y + 270f + (i / 2) * 90f;
            Tinta.Icone(linhas[i].Icone, new Vector2(x + 30f, y + 28f), 64f);
            Tinta.Texto(linhas[i].Texto, new Vector2(x + 80f, y + 8f), Tinta.Tinteiro);
        }

        Tinta.Texto($"São {PegaFrutas.Duracao:0} segundos. {PegaFrutas.PontosParaGanhar} pontos já é vitória!",
            new Vector2(c.Centro.X, c.Y + 470f), Color.FromHex("#6B5A6EFF"), escala: 0.85f, alinhar: Tinta.Alinhar.Centro);

        Tinta.Botao(BotaoComecar, "Começar!", Color.FromHex("#4FA83FFF"), Toque.SegurandoEm(BotaoComecar), null, true, Tinta.FonteMedia);
    }

    private void DesenharResultado()
    {
        var painel = new Caixa(60f, 330f, 600f, 480f);
        Tinta.Cartao(painel, Tinta.Papel, Tinta.Tinteiro, 4f, 26f);
        Tinta.Texto(_recorde ? "NOVO RECORDE!" : "Fim da brincadeira",
            new Vector2(360f, painel.Y + 30f), _recorde ? Color.FromHex("#D98A1FFF") : Tinta.Tinteiro, Tinta.FonteMedia, alinhar: Tinta.Alinhar.Centro);
        Tinta.Paragrafo(_resultado, new Vector2(360f, painel.Y + 110f), 540f, Tinta.Tinteiro, Tinta.FonteMedia, 0.8f, Tinta.Alinhar.Centro);
        Tinta.Botao(BotaoVoltar, "Voltar pra casa", Color.FromHex("#7B61C9FF"), Toque.SegurandoEm(BotaoVoltar));
    }
}
