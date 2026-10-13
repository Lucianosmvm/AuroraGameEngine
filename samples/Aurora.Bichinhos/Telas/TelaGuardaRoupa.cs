using System.Numerics;
using Aurora.Runtime.Graphics;

namespace Bichinhos;

/// <summary>
/// O guarda-roupa: o bichinho de frente, vestido do jeito que está, e os itens em cartões. Tocar num
/// item que você não tem compra (e já veste); num que tem, veste ou tira. A regra está em
/// <see cref="Loja"/>; aqui só entram o toque e o desenho.
/// </summary>
public sealed class TelaGuardaRoupa : Tela
{
    private static readonly Caixa Cabecalho = new(40f, 40f, 640f, 120f);
    private static readonly Caixa BotaoVoltar = new(160f, 1150f, 400f, 90f);
    private static readonly Vector2 Pe = new(360f, 510f);
    private const float LadoBicho = 330f;

    private readonly BichoVisual _visual = new();
    private readonly Particulas _particulas = new();
    private string _mensagem = "Toque num item para comprar, vestir ou tirar.";
    private bool _mensagemBoa = true;
    private float _tempo;

    public TelaGuardaRoupa(BichinhosGame jogo) : base(jogo) { }

    private Bicho B => Jogo.Bicho!;
    private Progresso P => Jogo.Progresso;

    private static Caixa CaixaDoItem(int i) => new(40f + (i % 2) * 330f, 560f + (i / 2) * 175f, 310f, 160f);

    public override void Entrar()
    {
        Jogo.Musica("musica_casa");
        _visual.Roupa = P.Roupa;
        _visual.Pular(40f);
    }

    public override void Atualizar(float dt)
    {
        _tempo += dt;
        _visual.Atualizar(dt);
        _particulas.Atualizar(dt);

        for (int i = 0; i < Itens.Todos.Count; i++)
        {
            if (Toque.Tocou(CaixaDoItem(i)))
                Tocar(Itens.Todos[i]);
        }

        if (Toque.Tocou(BotaoVoltar))
            Jogo.IrPara(new TelaCasa(Jogo, 0, P.Roupa.Vestidos().Any() ? "Que estilo!" : null));
    }

    private void Tocar(Item item)
    {
        var centro = Pe - new Vector2(0f, LadoBicho * 0.5f);

        if (!Loja.Tem(P, item))
        {
            switch (Loja.Comprar(P, item))
            {
                case ResultadoCompra.Ok:
                    Loja.Equipar(P, item);
                    Dizer($"{item.Nome} comprado!", true);
                    Som("moeda");
                    _visual.Pular(70f);
                    _particulas.Explosao("estrela", centro, 10, 42f);
                    Jogo.Salvar();
                    break;
                case ResultadoCompra.SemMoedas:
                    Dizer($"Faltam {item.Preco - P.Moedas} moedas.", false);
                    Som("erro", 0.7f);
                    break;
                default:
                    Dizer(item.Exclusivo ? "Esse só se ganha no 7º dia do presente." : "Não dá para comprar.", false);
                    Som("erro", 0.7f);
                    break;
            }
            return;
        }

        if (Loja.Equipado(P, item))
        {
            Loja.Tirar(P, item.Slot);
            Dizer($"{item.Nome} guardado.", true);
        }
        else
        {
            Loja.Equipar(P, item);
            Dizer($"{B.Nome} está de {item.Nome.ToLowerInvariant()}!", true);
            Som("carinho");
            _visual.Pular(45f);
            _particulas.Icone("coracao", centro, 54f);
        }
        Jogo.Salvar();
    }

    private void Dizer(string texto, bool boa)
    {
        _mensagem = texto;
        _mensagemBoa = boa;
    }

    public override void Desenhar(float dt)
    {
        Fundo("casa");
        Tinta.Retangulo(new Caixa(0, 0, BichinhosGame.Largura, BichinhosGame.Altura), Color.FromHex("#2A1E4460"));

        Tinta.Cartao(Cabecalho, Tinta.Papel, Tinta.Tinteiro, 4f, 28f);
        Tinta.Texto("Guarda-roupa", new Vector2(70f, 72f), Tinta.Tinteiro, Tinta.FonteMedia);
        string moedas = P.Moedas.ToString();
        float larguraMoedas = Tinta.Medir(moedas, Tinta.FonteMedia).X;
        Tinta.Icone("moeda", new Vector2(640f - larguraMoedas - 30f, 100f), 48f);
        Tinta.Texto(moedas, new Vector2(650f, 72f), Tinta.Tinteiro, Tinta.FonteMedia, alinhar: Tinta.Alinhar.Direita);

        _visual.Desenhar(Tinta, B.Especie, B.Estagio, Pe, LadoBicho);
        _particulas.Desenhar(Tinta);

        for (int i = 0; i < Itens.Todos.Count; i++)
            DesenharItem(Itens.Todos[i], CaixaDoItem(i));

        Tinta.Paragrafo(_mensagem, new Vector2(360f, 1112f), 640f,
            _mensagemBoa ? Color.White : Color.FromHex("#FFB4A8FF"), escala: 0.85f, alinhar: Tinta.Alinhar.Centro);
        Tinta.Botao(BotaoVoltar, "Voltar", Color.FromHex("#7B61C9FF"), Toque.SegurandoEm(BotaoVoltar));
    }

    private void DesenharItem(Item item, Caixa c)
    {
        bool tem = Loja.Tem(P, item);
        bool vestido = Loja.Equipado(P, item);
        bool trancado = item.Exclusivo && !tem;
        bool apertado = Toque.SegurandoEm(c);

        var face = apertado ? new Caixa(c.X, c.Y + 4f, c.L, c.A) : c;
        var borda = vestido ? Color.FromHex("#3FA862FF") : Tinta.Tinteiro;
        Tinta.Cartao(face, trancado ? Color.FromHex("#E4DDD6FF") : Tinta.Papel, borda, vestido ? 7f : 4f, 24f);

        var cor = trancado ? Color.White.WithAlpha(0.35f) : Color.White;
        Tinta.Sprite(Tinta.Textura(item.Sprite), new Vector2(face.X + 68f, face.Y + face.A / 2f), new Vector2(92f),
            new Vector2(0.5f, 0.5f), cor);

        Tinta.Texto(item.Nome, new Vector2(face.X + 128f, face.Y + 20f), Tinta.Tinteiro, escala: 0.78f);

        float y = face.Y + 78f;
        if (vestido)
        {
            Tinta.Texto("Vestido", new Vector2(face.X + 128f, y), Color.FromHex("#2F8F4FFF"), escala: 0.85f);
            Tinta.Texto("toque para tirar", new Vector2(face.X + 128f, y + 34f), Color.FromHex("#6B5A6EFF"), escala: 0.62f);
        }
        else if (tem)
        {
            Tinta.Texto("Guardado", new Vector2(face.X + 128f, y), Tinta.Tinteiro, escala: 0.85f);
            Tinta.Texto("toque para vestir", new Vector2(face.X + 128f, y + 34f), Color.FromHex("#6B5A6EFF"), escala: 0.62f);
        }
        else if (trancado)
        {
            Tinta.Paragrafo("7 dias seguidos no presente", new Vector2(face.X + 128f, y - 4f), 170f, Color.FromHex("#6B5A6EFF"), escala: 0.7f);
        }
        else
        {
            bool pode = P.Moedas >= item.Preco;
            Tinta.Icone("moeda", new Vector2(face.X + 150f, y + 18f), 34f);
            Tinta.Texto(item.Preco.ToString(), new Vector2(face.X + 176f, y),
                pode ? Tinta.Tinteiro : Color.FromHex("#C0392BFF"), escala: 0.95f);
        }
    }
}
