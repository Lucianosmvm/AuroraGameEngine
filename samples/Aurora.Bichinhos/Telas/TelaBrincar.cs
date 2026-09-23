using System.Numerics;
using Aurora.Runtime.Graphics;

namespace Bichinhos;

/// <summary>
/// A brincadeira clássica do Tamagotchi: "pra que lado ele vai?". Cinco rodadas; acertar três
/// ou mais conta como vitória (mais alegria, XP e moedas).
/// </summary>
public sealed class TelaBrincar : Tela
{
    private enum Fase { Palpite, Revelando, Fim }

    private const int Rodadas = 5;
    private const int ParaGanhar = 3;
    private const int MoedasVitoria = 3;

    private readonly BichoVisual _visual = new();
    private readonly Particulas _particulas = new();
    private Fase _fase = Fase.Palpite;
    private int _rodada;
    private int _acertos;
    private int _lado;          // -1 esquerda, +1 direita (pra onde o bicho foi)
    private bool _acertou;
    private float _t;
    private string _resultado = "";
    private readonly bool[] _acertosPorRodada = new bool[Rodadas];

    private static readonly Caixa BotaoEsquerda = new(40f, 1000f, 300f, 150f);
    private static readonly Caixa BotaoDireita = new(380f, 1000f, 300f, 150f);
    private static readonly Caixa BotaoVoltar = new(160f, 1040f, 400f, 100f);
    private static readonly Vector2 Pe = new(360f, 820f);

    public TelaBrincar(BichinhosGame jogo) : base(jogo) { }

    private Bicho B => Jogo.Bicho!;

    public override void Atualizar(float dt)
    {
        _t += dt;
        _visual.Atualizar(dt);
        _particulas.Atualizar(dt);

        switch (_fase)
        {
            case Fase.Palpite:
                // Balança de um lado pro outro, indeciso, enquanto espera o palpite.
                _visual.Deslocamento = new Vector2(MathF.Sin(_t * 5f) * 14f, 0f);
                if (Toque.Tocou(BotaoEsquerda)) Revelar(-1);
                else if (Toque.Tocou(BotaoDireita)) Revelar(1);
                break;

            case Fase.Revelando:
            {
                float p = MathF.Min(1f, _t / 0.3f);
                _visual.Deslocamento = new Vector2(_lado * 150f * p, 0f);
                if (_t > 1.3f)
                {
                    _rodada++;
                    _t = 0f;
                    _visual.Deslocamento = Vector2.Zero;
                    _visual.Espelhar = false;
                    if (_rodada >= Rodadas) Terminar();
                    else _fase = Fase.Palpite;
                }
                break;
            }

            case Fase.Fim:
                if (Toque.Tocou(BotaoVoltar))
                {
                    Jogo.Salvar();
                    Jogo.IrPara(new TelaCasa(Jogo, 0, _acertos >= ParaGanhar ? "Foi divertido!" : "Brinca de novo?"));
                }
                break;
        }
    }

    private void Revelar(int palpite)
    {
        _lado = Jogo.Rng.Next(2) == 0 ? -1 : 1;
        _acertou = palpite == _lado;
        if (_acertou) _acertos++;
        _acertosPorRodada[_rodada] = _acertou;

        _fase = Fase.Revelando;
        _t = 0f;
        _visual.Espelhar = _lado < 0;
        _visual.Pular(50f);

        var cabeca = Pe + new Vector2(_lado * 150f, -330f);
        if (_acertou)
            _particulas.Icone("coracao", cabeca, 70f);
        else
            _particulas.Texto("Errou!", cabeca, Color.FromHex("#FF8A8AFF"), grande: true);
    }

    private void Terminar()
    {
        _fase = Fase.Fim;
        bool ganhou = _acertos >= ParaGanhar;
        B.Brincou(ganhou);

        var (niveis, _) = B.GanharXp(ganhou ? 8 : 3);
        if (ganhou)
        {
            Jogo.Progresso.Moedas += MoedasVitoria;
            _particulas.Explosao("estrela", Pe - new Vector2(0f, 200f), 14, 44f);
            _visual.Pular(90f);
        }

        _resultado = ganhou ? $"Você ganhou! +{MoedasVitoria} moedas" : "Quase! Mas ele se divertiu.";
        if (niveis.Count > 0)
            _resultado += $"\nSubiu pro nível {B.Nivel}!";

        Jogo.Salvar();
    }

    public override void Desenhar(float dt)
    {
        Fundo("casa");
        Tinta.Retangulo(new Caixa(0, 0, BichinhosGame.Largura, BichinhosGame.Altura), Color.FromHex("#2A1E4460"));

        var topo = new Caixa(40f, 60f, 640f, 200f);
        Tinta.Cartao(topo, Tinta.Papel, Tinta.Tinteiro, 4f, 28f);
        Tinta.Texto("Pra que lado ele vai?", new Vector2(360f, 84f), Tinta.Tinteiro, Tinta.FonteMedia, alinhar: Tinta.Alinhar.Centro);

        // Bolinhas das rodadas: cheia = acerto, vazia = erro, cinza = ainda não jogou.
        for (int i = 0; i < Rodadas; i++)
        {
            var centro = new Vector2(360f + (i - 2) * 76f, 190f);
            Tinta.Circulo(centro, 28f, Tinta.Tinteiro);
            Color cor = i < _rodada || (i == _rodada && _fase == Fase.Revelando)
                ? (_acertosPorRodada[i] ? Color.FromHex("#4FC3A1FF") : Color.FromHex("#E0607EFF"))
                : Color.FromHex("#E6DDD4FF");
            Tinta.Circulo(centro, 24f, cor);
        }

        _visual.Desenhar(Tinta, B.Especie, B.Estagio, Pe, 340f);
        _particulas.Desenhar(Tinta);

        if (_fase == Fase.Fim)
        {
            var painel = new Caixa(60f, 870f, 600f, 150f);
            Tinta.Cartao(painel, Tinta.Papel, Tinta.Tinteiro, 4f, 26f);
            Tinta.Paragrafo(_resultado, new Vector2(360f, painel.Y + 30f), 540f, Tinta.Tinteiro, Tinta.FonteMedia, 0.8f, Tinta.Alinhar.Centro);
            Tinta.Botao(BotaoVoltar, "Voltar pra casa", Color.FromHex("#7B61C9FF"), Toque.SegurandoEm(BotaoVoltar));
            return;
        }

        bool ativo = _fase == Fase.Palpite;
        Tinta.Botao(BotaoEsquerda, "Esquerda", Color.FromHex("#3F95E0FF"), ativo && Toque.SegurandoEm(BotaoEsquerda), null, ativo, Tinta.FonteMedia);
        Tinta.Botao(BotaoDireita, "Direita", Color.FromHex("#F08A3CFF"), ativo && Toque.SegurandoEm(BotaoDireita), null, ativo, Tinta.FonteMedia);
    }
}
