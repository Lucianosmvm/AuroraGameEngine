using System.Numerics;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Input;
using BeastArena.Sim;

namespace BeastArena.Render;

/// <summary>
/// A faixa de baixo da tela: mão de cartas, próxima carta, barra de mana — o placar de pontos
/// em cima — e o toque que joga a carta.
///
/// <para>Dois jeitos de jogar: <b>arrastar</b> a carta até a arena e soltar, ou <b>tocar</b> na
/// carta (fica selecionada) e depois tocar na arena. O segundo é o que salva quem joga com uma
/// mão só no ônibus.</para>
///
/// <para>Segue UM toque por vez (o primeiro que encostar). Multi-toque aqui só causaria jogada
/// acidental com a palma da mão.</para>
/// </summary>
public sealed class Mao
{
    public const float TopoDoPainel = 980f;
    private const float DistanciaDeArrasto = 14f;

    private static readonly Color CorMana = Color.FromHex("#7CC6FFFF");
    private static readonly Color Painel = Color.FromHex("#161A13FF");
    private static readonly Color FundoDaCarta = Color.FromHex("#262C21FF");
    private static readonly Color Caixa = Color.FromHex("#0B0E0AB8");

    private int? _toque;
    private int? _pressionada;
    private Vector2 _inicio;
    private bool _arrastando;
    private string _aviso = "";
    private float _tempoDoAviso;

    public int? Selecionada { get; private set; }

    /// <summary>Onde o dedo (ou o mouse, no desktop) está agora, em pixels de tela.</summary>
    public Vector2 Ponteiro { get; private set; }

    public void Reiniciar()
    {
        _toque = null;
        _pressionada = null;
        _arrastando = false;
        _aviso = "";
        Selecionada = null;
    }

    private static RectF RetanguloDaCarta(int indice) => new(130f + indice * 146f, 1000f, 138f, 184f);
    private static readonly RectF RetanguloDaProxima = new(14f, 1050f, 100f, 130f);

    private static bool Contem(RectF r, Vector2 p)
        => p.X >= r.X && p.X <= r.X + r.Width && p.Y >= r.Y && p.Y <= r.Y + r.Height;

    // ------------------------------------------------------------------ entrada

    public void Atualizar(float deltaTime, InputManager input, Camera2D camera, Batalha batalha)
    {
        _tempoDoAviso = MathF.Max(0f, _tempoDoAviso - deltaTime);
        var toques = input.ActiveTouches;

        if (_toque is int id)
        {
            bool ainda = false;
            foreach (var (outro, posicao) in toques)
            {
                if (outro != id)
                    continue;

                ainda = true;
                Ponteiro = posicao;
            }

            if (ainda)
                Mover();
            else
            {
                _toque = null;
                Soltar(camera, batalha);
            }
        }
        else if (toques.Count > 0)
        {
            _toque = toques[0].Id;
            Ponteiro = toques[0].Position;
            Pressionar(camera, batalha);
        }
        else
        {
            // Desktop sem botão apertado: acompanha o mouse pra prévia da carta selecionada.
            Ponteiro = input.MousePosition;
        }
    }

    private void Pressionar(Camera2D camera, Batalha batalha)
    {
        for (int i = 0; i < Lado.TamanhoDaMao; i++)
        {
            if (!Contem(RetanguloDaCarta(i), Ponteiro))
                continue;

            _pressionada = i;
            _inicio = Ponteiro;
            _arrastando = false;
            return;
        }

        if (Ponteiro.Y < TopoDoPainel && Selecionada is int selecionada && TentarJogar(selecionada, camera, batalha))
            Selecionada = null;
    }

    private void Mover()
    {
        if (_pressionada is not null && Vector2.Distance(Ponteiro, _inicio) > DistanciaDeArrasto)
            _arrastando = true;
    }

    private void Soltar(Camera2D camera, Batalha batalha)
    {
        if (_pressionada is int indice)
        {
            if (_arrastando)
            {
                if (Ponteiro.Y < TopoDoPainel)
                    TentarJogar(indice, camera, batalha);
                Selecionada = null;
            }
            else
            {
                Selecionada = Selecionada == indice ? null : indice;
            }
        }

        _pressionada = null;
        _arrastando = false;
    }

    private bool TentarJogar(int indice, Camera2D camera, Batalha batalha)
    {
        var tile = TileSobPonteiro(camera);

        if (!batalha.PodeJogar(Equipe.Jogador, indice, tile, out string motivo))
        {
            _aviso = motivo;
            _tempoDoAviso = 1.4f;
            return false;
        }

        batalha.Jogar(Equipe.Jogador, indice, tile);
        return true;
    }

    private Vector2 TileSobPonteiro(Camera2D camera) => VisaoArena.ParaTile(camera.ScreenToWorld(Ponteiro));

    /// <summary>A prévia só existe com o ponteiro em cima da arena — em cima do painel, soltar
    /// não joga, então mostrar onde "cairia" mentiria.</summary>
    public PreviaDeJogada? Previa(Camera2D camera, Batalha batalha)
    {
        int? indice = _arrastando ? _pressionada : Selecionada;
        if (indice is not int i || Ponteiro.Y >= TopoDoPainel || batalha.Acabou)
            return null;

        var tile = TileSobPonteiro(camera);
        return new PreviaDeJogada(batalha.LadoDe(Equipe.Jogador).Mao[i], tile,
            batalha.PodeJogar(Equipe.Jogador, i, tile, out _));
    }

    // ------------------------------------------------------------------ desenho

    public void Desenhar(SpriteBatch batch, Font fonte, Formas formas, Sprites sprites, Batalha batalha)
    {
        var lado = batalha.LadoDe(Equipe.Jogador);

        batch.DrawRect(new Vector2(0f, TopoDoPainel), new Vector2(720f, 300f), Painel);
        batch.DrawRect(new Vector2(0f, TopoDoPainel), new Vector2(720f, 3f), Color.FromHex("#3A4530FF"));

        Escrever(batch, fonte, "Próxima", new Vector2(64f, 1026f), Color.FromHex("#8E9780FF"), 0.6f);
        DesenharCarta(batch, fonte, formas, sprites, lado.Proxima, RetanguloDaProxima, lado.Mana, selecionada: false, mini: true);

        for (int i = 0; i < Lado.TamanhoDaMao; i++)
        {
            var retangulo = RetanguloDaCarta(i);
            bool selecionada = Selecionada == i || (_arrastando && _pressionada == i);

            if (selecionada)
                retangulo = retangulo with { Y = retangulo.Y - 14f };

            DesenharCarta(batch, fonte, formas, sprites, lado.Mao[i], retangulo, lado.Mana, selecionada, mini: false);
        }

        DesenharMana(batch, fonte, formas, lado.Mana, batalha.ManaPorSegundo(Equipe.Jogador));
        DesenharPlacar(batch, fonte, formas, batalha);

        if (_tempoDoAviso > 0f)
            Escrever(batch, fonte, _aviso, new Vector2(360f, 950f), Color.FromHex("#FF8080FF").WithAlpha(MathF.Min(1f, _tempoDoAviso * 2f)), 0.85f);

        // A carta arrastada segue o dedo por cima de tudo: é o "estou segurando isto".
        if (_arrastando && _pressionada is int arrastada && Ponteiro.Y >= TopoDoPainel)
        {
            var r = RetanguloDaCarta(arrastada);
            DesenharCarta(batch, fonte, formas, sprites, lado.Mao[arrastada],
                r with { X = Ponteiro.X - r.Width / 2f, Y = Ponteiro.Y - r.Height / 2f }, lado.Mana, true, false);
        }
    }

    private static void DesenharCarta(SpriteBatch batch, Font fonte, Formas formas, Sprites sprites, CartaDef carta, RectF r,
        float mana, bool selecionada, bool mini)
    {
        var canto = new Vector2(r.X, r.Y);
        var tamanho = new Vector2(r.Width, r.Height);

        if (selecionada)
            batch.DrawRect(canto - new Vector2(4f), tamanho + new Vector2(8f), Color.FromHex("#FFD54FFF"));

        batch.DrawRect(canto, tamanho, FundoDaCarta);
        batch.DrawRect(canto, new Vector2(r.Width, r.Height * 0.72f), Color.FromHex(carta.Cor).WithAlpha(0.16f));
        batch.DrawRect(canto + new Vector2(0f, r.Height - 6f), new Vector2(r.Width, 6f),
            carta.Evolucoes.Count > 0 ? Color.FromHex("#FFD54F99") : Color.FromHex("#FFFFFF22"));

        var icone = canto + new Vector2(r.Width / 2f, r.Height * 0.42f);
        if (sprites.Icone(carta) is { } arte)
        {
            batch.Draw(arte, icone, new Vector2(r.Width * (mini ? 0.95f : 0.9f)), new Vector2(0.5f), 0f, Color.White);
        }
        else
        {
            formas.DesenharDisco(batch, icone, r.Width * 0.3f, Color.FromHex(carta.Cor));

            float escalaLetra = mini ? 0.9f : 1.3f;
            var medida = fonte.MeasureText(carta.Letra, escalaLetra);
            fonte.Draw(batch, carta.Letra, icone - medida / 2f, Color.FromHex("#1B1B22FF"), escalaLetra);
        }

        if (!mini)
        {
            float escalaNome = fonte.MeasureText(carta.Nome, 0.62f).X > r.Width - 10f ? 0.5f : 0.62f;
            Escrever(batch, fonte, carta.Nome, canto + new Vector2(r.Width / 2f, r.Height - 32f), Color.White, escalaNome);

            if (carta.Quantidade > 1)
                Escrever(batch, fonte, $"x{carta.Quantidade}", canto + new Vector2(r.Width - 20f, 18f), Color.FromHex("#C9CFBDFF"), 0.55f);
        }

        var bolha = canto + new Vector2(mini ? 14f : 20f, mini ? 14f : 20f);
        formas.DesenharDisco(batch, bolha, mini ? 13f : 18f, CorMana);
        Escrever(batch, fonte, carta.Custo.ToString(), bolha, Color.FromHex("#0F1B26FF"), mini ? 0.6f : 0.8f);

        // Sem mana: escurece, e a faixa clara que sobe mostra quanto falta — o jogador não
        // precisa fazer conta pra saber se a carta sai no próximo segundo.
        if (mana < carta.Custo)
        {
            float fracao = Math.Clamp(mana / carta.Custo, 0f, 1f);
            batch.DrawRect(canto, new Vector2(r.Width, r.Height * (1f - fracao)), Color.FromHex("#000000A0"));
            batch.DrawRect(canto + new Vector2(0f, r.Height * (1f - fracao)), new Vector2(r.Width, r.Height * fracao), Color.FromHex("#00000055"));
        }
    }

    /// <param name="porSegundo">Ritmo atual. Mostrar o número é o que ensina que santuário
    /// dominado acelera a mana.</param>
    private static void DesenharMana(SpriteBatch batch, Font fonte, Formas formas, float mana, float porSegundo)
    {
        var canto = new Vector2(130f, 1206f);
        var tamanho = new Vector2(576f, 40f);

        batch.DrawRect(canto, tamanho, Color.FromHex("#18293AFF"));
        batch.DrawRect(canto, new Vector2(tamanho.X * mana / Lado.ManaMaxima, tamanho.Y), CorMana);

        for (int i = 1; i < (int)Lado.ManaMaxima; i++)
            batch.DrawRect(canto + new Vector2(tamanho.X * i / Lado.ManaMaxima - 1f, 0f), new Vector2(2f, tamanho.Y), Color.FromHex("#161A13AA"));

        formas.DesenharDisco(batch, new Vector2(66f, 1226f), 32f, CorMana);
        Escrever(batch, fonte, ((int)mana).ToString(), new Vector2(66f, 1226f), Color.FromHex("#0F1B26FF"), 1.1f);

        Escrever(batch, fonte, $"+{porSegundo:0.00}/s", new Vector2(648f, 1226f), Color.White.WithAlpha(0.85f), 0.6f);
    }

    private static void DesenharPlacar(SpriteBatch batch, Font fonte, Formas formas, Batalha batalha)
    {
        int restante = (int)MathF.Ceiling(batalha.TempoRestante);

        batch.DrawRect(new Vector2(600f, 24f), new Vector2(108f, 52f), Caixa);
        Escrever(batch, fonte, $"{restante / 60}:{restante % 60:00}", new Vector2(654f, 50f), Color.White, 1.0f);

        batch.DrawRect(new Vector2(12f, 24f), new Vector2(236f, 74f), Caixa);
        DesenharPontos(batch, fonte, formas, batalha, Equipe.Jogador, 44f);
        DesenharPontos(batch, fonte, formas, batalha, Equipe.Inimigo, 78f);
    }

    /// <summary>Uma linha do placar: pontos, barra até a meta e uma bolinha por santuário dominado.</summary>
    private static void DesenharPontos(SpriteBatch batch, Font fonte, Formas formas, Batalha batalha, Equipe equipe, float y)
    {
        var cor = VisaoArena.CorDaEquipe(equipe);
        var lado = batalha.LadoDe(equipe);

        Escrever(batch, fonte, ((int)lado.Pontos).ToString(), new Vector2(46f, y), cor, 0.85f);

        var barra = new Vector2(80f, y - 5f);
        const float largura = 120f;
        batch.DrawRect(barra, new Vector2(largura, 10f), Color.FromHex("#FFFFFF1A"));
        batch.DrawRect(barra, new Vector2(largura * Math.Clamp(lado.Pontos / Batalha.MetaDePontos, 0f, 1f), 10f), cor);

        int dominados = batalha.SantuariosDe(equipe);
        for (int i = 0; i < batalha.Santuarios.Count; i++)
            formas.DesenharDisco(batch, new Vector2(214f + i * 12f, y), 4.5f, i < dominados ? cor : Color.FromHex("#FFFFFF26"));
    }

    /// <summary>Texto CENTRADO em <paramref name="centro"/>.</summary>
    private static void Escrever(SpriteBatch batch, Font fonte, string texto, Vector2 centro, Color cor, float escala)
    {
        var medida = fonte.MeasureText(texto, escala);
        fonte.Draw(batch, texto, centro - medida / 2f, cor, escala);
    }
}
