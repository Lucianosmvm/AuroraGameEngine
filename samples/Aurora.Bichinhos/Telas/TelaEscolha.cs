using System.Numerics;
using Aurora.Runtime.Graphics;

namespace Bichinhos;

/// <summary>Começo do jogo: três ovos, um de cada tipo. Tocar num cartão escolhe; o ovo treme,
/// racha e nasce o filhote.</summary>
public sealed class TelaEscolha : Tela
{
    private enum Fase { Escolhendo, Chocando, Nasceu }

    private readonly List<Especie> _opcoes;
    private readonly BichoVisual _visual = new();
    private readonly Particulas _particulas = new();
    private Fase _fase = Fase.Escolhendo;
    private Especie? _escolhida;
    private float _t;

    private const float DuracaoChocar = 2.4f;

    public TelaEscolha(BichinhosGame jogo) : base(jogo)
        => _opcoes = Catalogo.Atual.Iniciais.Take(3).ToList();

    private static Caixa Cartao(int i) => new(40f, 290f + i * 270f, 640f, 240f);

    public override void Atualizar(float dt)
    {
        _t += dt;
        _visual.Atualizar(dt);
        _particulas.Atualizar(dt);

        switch (_fase)
        {
            case Fase.Escolhendo:
                for (int i = 0; i < _opcoes.Count; i++)
                {
                    if (Toque.Tocou(Cartao(i)))
                    {
                        _escolhida = _opcoes[i];
                        _fase = Fase.Chocando;
                        _t = 0f;
                    }
                }
                break;

            case Fase.Chocando:
                if (_t >= DuracaoChocar)
                {
                    _fase = Fase.Nasceu;
                    _t = 0f;
                    _visual.Pular(90f);
                    _particulas.Explosao("estrela", new Vector2(360f, 620f), 14, 46f);
                    Jogo.NovoBicho(_escolhida!.Id);
                }
                break;

            case Fase.Nasceu:
                if (_t > 0.8f && Toque.TocouQualquer())
                    Jogo.IrPara(new TelaCasa(Jogo, 0));
                break;
        }
    }

    public override void Desenhar(float dt)
    {
        Fundo("escolha");

        if (_fase == Fase.Escolhendo)
            DesenharCartoes();
        else
            DesenharNascimento();

        _particulas.Desenhar(Tinta);
    }

    private void DesenharCartoes()
    {
        Tinta.TextoContornado("Bichinhos", new Vector2(360f, 90f), Color.FromHex("#FFE27AFF"), Tinta.FonteGrande, 1.2f);
        Tinta.Texto("Escolha um ovo pra chocar", new Vector2(360f, 200f), Color.FromHex("#E8DDFBFF"), Tinta.FonteMedia, 0.85f, Tinta.Alinhar.Centro);

        for (int i = 0; i < _opcoes.Count; i++)
        {
            var e = _opcoes[i];
            var c = Cartao(i);
            bool apertado = Toque.SegurandoEm(c);
            var tipo = Color.FromHex(Tipos.Cor(e.Tipo));

            var face = apertado ? new Caixa(c.X, c.Y + 4f, c.L, c.A) : c;
            Tinta.Cartao(face, Tinta.Papel, Tinta.Tinteiro, 5f, 28f);
            Tinta.Painel(new Caixa(face.X + 5f, face.Y + 5f, 14f, face.A - 10f), tipo, 7f);

            // Ovo balançando de leve, cada um num ritmo.
            float balanco = MathF.Sin(_t * 2.4f + i * 1.7f) * 0.12f;
            Tinta.Sprite(Tinta.Textura($"sprites/bichos/ovo_{e.Id}.png"), new Vector2(face.X + 120f, face.Y + 210f),
                new Vector2(190f), new Vector2(0.5f, 0.9f), Color.White, balanco);

            Tinta.Texto(e.Estagios[0].Nome, new Vector2(face.X + 240f, face.Y + 30f), Tinta.Tinteiro, Tinta.FonteMedia);
            Tinta.Painel(new Caixa(face.X + 240f, face.Y + 86f, 130f, 40f), tipo, 20f);
            Tinta.Texto(Tipos.Nome(e.Tipo), new Vector2(face.X + 305f, face.Y + 91f), Color.White, alinhar: Tinta.Alinhar.Centro);
            Tinta.Paragrafo(e.Descricao, new Vector2(face.X + 240f, face.Y + 140f), 370f, Color.FromHex("#5B4B5EFF"), escala: 0.85f);
        }

        Tinta.Paragrafo("Fogo vence Planta, Planta vence Água, Água vence Fogo.", new Vector2(360f, 1128f), 600f,
            Color.FromHex("#CFC2EAFF"), escala: 0.85f, alinhar: Tinta.Alinhar.Centro);
    }

    private void DesenharNascimento()
    {
        var e = _escolhida!;
        var pe = new Vector2(360f, 760f);
        Tinta.Circulo(new Vector2(360f, 600f), 260f, Color.FromHex("#FFFFFF10"));

        if (_fase == Fase.Chocando)
        {
            // Treme cada vez mais rápido e forte até rachar.
            float p = _t / DuracaoChocar;
            float angulo = MathF.Sin(_t * (8f + p * 30f)) * (0.08f + p * 0.25f);
            Tinta.Sprite(Tinta.Textura($"sprites/bichos/ovo_{e.Id}.png"), pe, new Vector2(340f),
                new Vector2(0.5f, 0.9f), Color.White, angulo);
            if (p > 0.85f)
                Tinta.Circulo(new Vector2(360f, 600f), 400f * (p - 0.85f) / 0.15f * 2f, Color.White.WithAlpha((p - 0.85f) / 0.15f));
            Tinta.TextoContornado("Está chocando...", new Vector2(360f, 900f), Color.White, Tinta.FonteMedia);
            return;
        }

        float brilho = MathF.Max(0f, 1f - _t / 0.5f);
        _visual.Desenhar(Tinta, e, 0, pe, 380f);
        if (brilho > 0f)
            Tinta.Retangulo(new Caixa(0, 0, BichinhosGame.Largura, BichinhosGame.Altura), Color.White.WithAlpha(brilho));

        Tinta.TextoContornado($"Nasceu {e.Estagios[0].Nome}!", new Vector2(360f, 860f), Color.FromHex("#FFE27AFF"), Tinta.FonteGrande, 0.9f);
        Tinta.Paragrafo("Dê comida, carinho, banho e hora de dormir. Bicho bem cuidado fica forte, sobe de nível e evolui!",
            new Vector2(360f, 960f), 600f, Color.FromHex("#E8DDFBFF"), escala: 0.95f, alinhar: Tinta.Alinhar.Centro);

        if (_t > 0.8f && (int)(_t * 2f) % 2 == 0)
            Tinta.Texto("toque para continuar", new Vector2(360f, 1150f), Color.FromHex("#CFC2EAFF"), alinhar: Tinta.Alinhar.Centro);
    }
}
