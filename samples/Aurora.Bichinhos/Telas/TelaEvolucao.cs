using System.Numerics;
using Aurora.Runtime.Graphics;

namespace Bichinhos;

/// <summary>
/// A cena da evolução, do jeito que todo mundo lembra: o bicho brilha, alterna entre a forma
/// velha e a nova cada vez mais rápido, clarão, e aparece a forma nova.
/// </summary>
public sealed class TelaEvolucao : Tela
{
    private const float DuracaoTroca = 3.2f;

    private readonly BichoVisual _visual = new();
    private readonly Particulas _particulas = new();
    private readonly string _nomeAntes;
    private readonly int _estagioAntes;
    private float _t;
    private bool _evoluiu;
    private float _alternar;
    private bool _mostraNova;

    private static readonly Vector2 Pe = new(360f, 800f);
    private const float LadoBicho = 420f;

    public TelaEvolucao(BichinhosGame jogo) : base(jogo)
    {
        _nomeAntes = jogo.Bicho!.Nome;
        _estagioAntes = jogo.Bicho.Estagio;
    }

    private Bicho B => Jogo.Bicho!;

    public override void Atualizar(float dt)
    {
        _t += dt;
        _visual.Atualizar(dt);
        _particulas.Atualizar(dt);

        if (!_evoluiu)
        {
            // Alterna mais rápido conforme o tempo passa: de 0.45 s até 0.05 s por forma.
            float p = _t / DuracaoTroca;
            _alternar -= dt;
            if (_alternar <= 0f)
            {
                _mostraNova = !_mostraNova;
                _alternar = 0.45f * (1f - p) + 0.05f;
            }

            if (_t >= DuracaoTroca)
            {
                B.Evoluir();
                Jogo.Salvar();
                _evoluiu = true;
                _t = 0f;
                _visual.Pular(100f);
                _particulas.Explosao("estrela", Pe - new Vector2(0f, 220f), 22, 54f);
            }
            return;
        }

        if (_t > 1.2f && Toque.TocouQualquer())
        {
            // Pode ter pulado dois estágios de uma vez (muito XP numa luta): evolui de novo.
            if (B.EvolucaoPendente)
                Jogo.IrPara(new TelaEvolucao(Jogo));
            else
                Jogo.IrPara(new TelaCasa(Jogo, 0, "Olha só pra mim!"));
        }
    }

    public override void Desenhar(float dt)
    {
        Fundo("escolha");

        // Raios girando atrás do bicho.
        float giro = _t * 0.6f;
        for (int i = 0; i < 12; i++)
        {
            float a = giro + i * MathF.Tau / 12f;
            var ponta = Pe - new Vector2(0f, 200f) + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 420f;
            for (int k = 0; k < 8; k++)
            {
                var p = Vector2.Lerp(Pe - new Vector2(0f, 200f), ponta, k / 8f);
                Tinta.Circulo(p, 30f - k * 3f, Color.FromHex("#FFE27A18"));
            }
        }

        if (!_evoluiu)
        {
            float p = _t / DuracaoTroca;
            int estagio = _mostraNova ? _estagioAntes + 1 : _estagioAntes;
            // Silhueta clareando: de cor normal a quase branco.
            var tom = Tinta.Misturar(Color.White, Color.FromHex("#FFFBE8FF"), p);
            _visual.Desenhar(Tinta, B.Especie, Math.Min(2, estagio), Pe, LadoBicho, tom: tom);
            Tinta.Circulo(Pe - new Vector2(0f, 200f), 60f + p * 300f, Color.White.WithAlpha(p * p * 0.8f));

            Tinta.TextoContornado($"{_nomeAntes} está evoluindo!", new Vector2(360f, 950f), Color.White, Tinta.FonteMedia);
            return;
        }

        _visual.Desenhar(Tinta, B.Especie, B.Estagio, Pe, LadoBicho);
        float clarao = MathF.Max(0f, 1f - _t / 0.6f);
        if (clarao > 0f)
            Tinta.Retangulo(new Caixa(0, 0, BichinhosGame.Largura, BichinhosGame.Altura), Color.White.WithAlpha(clarao));

        _particulas.Desenhar(Tinta);

        Tinta.TextoContornado("Parabéns!", new Vector2(360f, 120f), Color.FromHex("#FFE27AFF"), Tinta.FonteGrande);
        Tinta.Paragrafo($"{_nomeAntes} evoluiu para {B.Nome}!", new Vector2(360f, 940f), 620f, Color.White, Tinta.FonteMedia, 1f, Tinta.Alinhar.Centro);
        Tinta.Texto("Ficou bem mais forte.", new Vector2(360f, 1060f), Color.FromHex("#CFC2EAFF"), alinhar: Tinta.Alinhar.Centro);

        if (_t > 1.2f && (int)(_t * 2f) % 2 == 0)
            Tinta.Texto("toque para continuar", new Vector2(360f, 1150f), Color.FromHex("#CFC2EAFF"), alinhar: Tinta.Alinhar.Centro);
    }
}
