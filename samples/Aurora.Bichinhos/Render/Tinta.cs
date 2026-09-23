using System.Numerics;
using Aurora.Runtime.Assets;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Input;
using Silk.NET.Input;

namespace Bichinhos;

/// <summary>Retângulo de tela (topo-esquerda + tamanho) com teste de toque.</summary>
public readonly record struct Caixa(float X, float Y, float L, float A)
{
    public Vector2 Centro => new(X + L / 2f, Y + A / 2f);
    public bool Contem(Vector2 p) => p.X >= X && p.X <= X + L && p.Y >= Y && p.Y <= Y + A;
    public Caixa Encolher(float m) => new(X + m, Y + m, L - 2 * m, A - 2 * m);
}

/// <summary>
/// Toque/clique do frame. O jogo inteiro é interface desenhada em código (não há cena de UI em
/// JSON), então os botões perguntam aqui se foram tocados.
///
/// <para>Conta o APERTO, não o soltar: no Android o <c>SetPointer</c> da MainActivity marca a
/// posição junto com o aperto, e no soltar a posição volta pro mouse do SDL — que é lixo.</para>
/// </summary>
public sealed class Toque
{
    private readonly InputManager _input;

    public Toque(InputManager input) => _input = input;

    public Vector2 Posicao => _input.MousePosition;
    public bool Apertou => _input.WasMouseClicked(MouseButton.Left);
    public bool Segurando => _input.IsMouseDown(MouseButton.Left);

    /// <summary>Consumido quando um botão já tratou o toque, pra ele não atravessar pro que está
    /// atrás (menu por cima da casa, por exemplo).</summary>
    public bool Consumido { get; private set; }

    public void NovoFrame() => Consumido = false;

    public bool Tocou(Caixa caixa)
    {
        if (Consumido || !Apertou || !caixa.Contem(Posicao))
            return false;

        Consumido = true;
        return true;
    }

    public bool TocouQualquer()
    {
        if (Consumido || !Apertou)
            return false;

        Consumido = true;
        return true;
    }

    public bool SegurandoEm(Caixa caixa) => Segurando && caixa.Contem(Posicao);

    public bool Tecla(Key tecla) => _input.WasKeyPressed(tecla);
}

/// <summary>
/// Pincel de interface: painéis arredondados (9 fatias), barras, texto alinhado, sprites com
/// pivô. Tudo em coordenadas da tela de referência 720x1280.
/// </summary>
public sealed class Tinta
{
    private readonly AssetManager _assets;
    private readonly Texture2D _painel;
    private readonly Texture2D _circulo;
    private const float Canto = 24f;   // raio do canto na textura ui/painel.png (96x96)

    public SpriteBatch Lote { get; set; } = null!;
    public Font Fonte { get; }
    public Font FonteMedia { get; }
    public Font FonteGrande { get; }

    public static readonly Color Tinteiro = Color.FromHex("#2A1E2CFF");
    public static readonly Color Papel = Color.FromHex("#FFF8EEFF");

    public Tinta(AssetManager assets)
    {
        _assets = assets;
        _painel = assets.LoadTexture("sprites/ui/painel.png");
        _circulo = assets.LoadTexture("sprites/ui/circulo.png");
        Fonte = assets.LoadFont("fonts/DejaVuSans.ttf", 26f);
        FonteMedia = assets.LoadFont("fonts/DejaVuSans.ttf", 36f);
        FonteGrande = assets.LoadFont("fonts/DejaVuSans.ttf", 56f);
    }

    public Texture2D Textura(string caminho) => _assets.LoadTexture(caminho);

    // ------------------------------------------------------------------ formas

    public void Retangulo(Caixa c, Color cor) => Lote.DrawRect(new Vector2(c.X, c.Y), new Vector2(c.L, c.A), cor);

    /// <summary>Retângulo de canto arredondado esticado em 9 fatias: o canto não deforma.</summary>
    public void Painel(Caixa c, Color cor, float raio = 22f)
    {
        raio = MathF.Min(raio, MathF.Min(c.L, c.A) / 2f);
        float t = _painel.Width;
        float[] xs = [c.X, c.X + raio, c.X + c.L - raio, c.X + c.L];
        float[] ys = [c.Y, c.Y + raio, c.Y + c.A - raio, c.Y + c.A];
        float[] us = [0, Canto, t - Canto, t];

        for (int j = 0; j < 3; j++)
        for (int i = 0; i < 3; i++)
        {
            float w = xs[i + 1] - xs[i], h = ys[j + 1] - ys[j];
            if (w <= 0 || h <= 0)
                continue;

            Lote.Draw(_painel, new Vector2(xs[i], ys[j]), new Vector2(w, h), Vector2.Zero, 0f, cor,
                new RectF(us[i], us[j], us[i + 1] - us[i], us[j + 1] - us[j]));
        }
    }

    /// <summary>Painel com borda: um painel escuro e outro por dentro.</summary>
    public void Cartao(Caixa c, Color fundo, Color? borda = null, float grossura = 4f, float raio = 22f)
    {
        Painel(c, borda ?? Tinteiro, raio);
        Painel(c.Encolher(grossura), fundo, raio - grossura);
    }

    public void Elipse(Vector2 centro, Vector2 raio, Color cor)
        => Lote.Draw(_circulo, centro, raio * 2f, new Vector2(0.5f, 0.5f), 0f, cor);

    public void Circulo(Vector2 centro, float raio, Color cor) => Elipse(centro, new Vector2(raio), cor);

    public void Barra(Caixa c, float fracao, Color cor, Color? fundo = null)
    {
        Painel(c, fundo ?? Color.FromHex("#00000040"), c.A / 2f);
        fracao = Math.Clamp(fracao, 0f, 1f);
        if (fracao <= 0.001f)
            return;
        // Barra quase vazia continua redonda: largura mínima = altura.
        float largura = MathF.Max(c.A, (c.L - 6f) * fracao);
        Painel(new Caixa(c.X + 3f, c.Y + 3f, MathF.Min(largura, c.L - 6f), c.A - 6f), cor, (c.A - 6f) / 2f);
    }

    // ------------------------------------------------------------------ sprites

    /// <summary>Sprite pelo pivô normalizado (0.5, 0.9 = pé no chão dos bichos).</summary>
    public void Sprite(Texture2D tex, Vector2 pos, Vector2 tamanho, Vector2 pivo, Color cor, float rotacao = 0f, bool espelhar = false)
        => Lote.Draw(tex, pos, tamanho, pivo, rotacao, cor, espelhar);

    public void Icone(string nome, Vector2 centro, float lado, Color? cor = null, float rotacao = 0f)
        => Lote.Draw(Textura($"sprites/icones/{nome}.png"), centro, new Vector2(lado), new Vector2(0.5f, 0.5f), rotacao, cor ?? Color.White);

    // ------------------------------------------------------------------ texto

    public enum Alinhar { Esquerda, Centro, Direita }

    public Vector2 Medir(string texto, Font? fonte = null, float escala = 1f) => (fonte ?? Fonte).MeasureText(texto, escala);

    public void Texto(string texto, Vector2 pos, Color cor, Font? fonte = null, float escala = 1f, Alinhar alinhar = Alinhar.Esquerda)
    {
        var f = fonte ?? Fonte;
        var tamanho = f.MeasureText(texto, escala);
        float x = alinhar switch
        {
            Alinhar.Centro => pos.X - tamanho.X / 2f,
            Alinhar.Direita => pos.X - tamanho.X,
            _ => pos.X,
        };
        f.Draw(Lote, texto, new Vector2(x, pos.Y), cor, escala);
    }

    /// <summary>Texto com contorno escuro — legível por cima do cenário claro ou escuro.</summary>
    public void TextoContornado(string texto, Vector2 pos, Color cor, Font? fonte = null, float escala = 1f, Alinhar alinhar = Alinhar.Centro, Color? contorno = null)
    {
        var borda = contorno ?? Tinteiro.WithAlpha(cor.A);
        float d = 2.5f * escala * ((fonte ?? Fonte).Size / 26f);
        foreach (var (dx, dy) in new[] { (-d, 0f), (d, 0f), (0f, -d), (0f, d), (-d, -d), (d, d), (-d, d), (d, -d) })
            Texto(texto, pos + new Vector2(dx, dy), borda, fonte, escala, alinhar);
        Texto(texto, pos, cor, fonte, escala, alinhar);
    }

    /// <summary>Texto quebrado na largura, linha a linha a partir de <paramref name="pos"/>.</summary>
    public void Paragrafo(string texto, Vector2 pos, float largura, Color cor, Font? fonte = null, float escala = 1f, Alinhar alinhar = Alinhar.Esquerda)
    {
        var f = fonte ?? Fonte;
        float y = pos.Y;
        foreach (string linha in f.WrapText(texto, largura, escala).Split('\n'))
        {
            Texto(linha, new Vector2(pos.X, y), cor, f, escala, alinhar);
            y += f.LineHeight * escala;
        }
    }

    // ------------------------------------------------------------------ botões

    /// <summary>Botão gordinho de celular: sombra embaixo, afunda quando apertado.</summary>
    public void Botao(Caixa c, string rotulo, Color cor, bool apertado, string? icone = null, bool ativo = true, Font? fonte = null)
    {
        var baseCor = ativo ? cor : Color.FromHex("#B8AFA8FF");
        float afunda = apertado && ativo ? 5f : 0f;

        Painel(new Caixa(c.X, c.Y + 7f, c.L, c.A), Escurecer(baseCor, 0.35f));
        var face = new Caixa(c.X, c.Y + afunda, c.L, c.A);
        Painel(face, Tinteiro);
        Painel(face.Encolher(3.5f), baseCor, 19f);
        Painel(new Caixa(face.X + 10f, face.Y + 8f, face.L - 20f, face.A * 0.32f), Color.White.WithAlpha(0.18f), 12f);

        var f = fonte ?? Fonte;
        var corTexto = ativo ? Color.White : Color.FromHex("#F4F0ECFF");

        if (icone is not null && c.A >= 100f)
        {
            float lado = c.A * 0.5f;
            Icone(icone, new Vector2(face.Centro.X, face.Y + c.A * 0.38f), lado, ativo ? Color.White : Color.White.WithAlpha(0.5f));
            TextoContornado(rotulo, new Vector2(face.Centro.X, face.Y + c.A - f.LineHeight - 8f), corTexto, f);
        }
        else if (icone is not null)
        {
            float lado = c.A * 0.62f;
            var tamanho = f.MeasureText(rotulo);
            float x = face.Centro.X - (tamanho.X + lado + 8f) / 2f;
            Icone(icone, new Vector2(x + lado / 2f, face.Centro.Y), lado);
            TextoContornado(rotulo, new Vector2(x + lado + 8f, face.Centro.Y - f.LineHeight / 2f + 2f), corTexto, f, alinhar: Alinhar.Esquerda);
        }
        else
        {
            TextoContornado(rotulo, new Vector2(face.Centro.X, face.Centro.Y - f.LineHeight / 2f + 2f), corTexto, f);
        }
    }

    public static Color Escurecer(Color c, float t) => new(c.R * (1 - t), c.G * (1 - t), c.B * (1 - t), c.A);
    public static Color Misturar(Color a, Color b, float t)
        => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
}
