using System.Numerics;

namespace Bichinhos;

/// <summary>Uma imagem de item já posicionada na tela.</summary>
public readonly record struct Peca(string Sprite, Vector2 Pos, Vector2 Tamanho, Vector2 Pivo, float Rotacao);

/// <summary>A hastezinha que liga as duas lentes dos óculos (retângulo na tela).</summary>
public readonly record struct Ponte(Vector2 Pos, Vector2 Tamanho);

/// <summary>
/// A conta de onde cada item cai, sem desenhar nada: fica separada do <see cref="BichoVisual"/>
/// para dar para testar. É a MESMA conta de <c>colocar()</c> em <c>Arte/gerar_ancoras.py</c>, que
/// monta a folha de conferência: se mudar uma, mude a outra.
///
/// <para>As âncoras vêm em % do sprite, com o pivô do bicho em (0,5; 0,9) do quadrado, como no
/// <see cref="BichoVisual"/>. O bicho encolhe e estica ao respirar, então tudo é calculado a partir
/// do <c>tamanho</c> do momento e o item acompanha.</para>
/// </summary>
public static class RoupaLayout
{
    /// <summary>Fração da altura da imagem em que o chapéu "pousa" (a base).</summary>
    public const float PivoTopo = 0.92f;

    /// <summary>O meio do lenço fica no ponto do pescoço, um pouco acima do centro.</summary>
    public const float PivoFaixa = 0.40f;

    public const float PivoLaco = 0.75f;

    /// <summary>Raio do olho (em % do sprite) até o tamanho da lente: o olho tem sua pálpebra
    /// 1,35x maior (ver <c>BichoVisual.FecharOlhos</c>) e a lente ainda sobra 12%.</summary>
    public const float FatorLente = 1.35f * 2f * 1.12f;

    /// <param name="pos">Onde o pé do bicho encosta no chão (pivô do sprite).</param>
    /// <param name="tamanho">Tamanho do sprite agora (já com a respiração).</param>
    /// <param name="olhos">[x, y, separação, raio] do <c>especies.json</c>.</param>
    public static List<Peca> Montar(Item item, ConfigItem config, FormaAncoras forma, IReadOnlyList<float> olhos,
        Vector2 pos, Vector2 tamanho, bool espelhar, out Ponte? ponte)
    {
        ponte = null;
        var pecas = new List<Peca>();
        float sinal = espelhar ? -1f : 1f;

        Vector2 Ponto(float xPorCento, float yPorCento)
        {
            float x = espelhar ? 100f - xPorCento : xPorCento;
            return pos + new Vector2((x / 100f - 0.5f) * tamanho.X, (yPorCento / 100f - 0.9f) * tamanho.Y);
        }

        switch (item.Desenho)
        {
            case Desenho.Lentes:
            {
                float lado = olhos[3] * FatorLente / 100f * tamanho.X;
                float yOlho = olhos[1];
                for (int s = -1; s <= 1; s += 2)
                {
                    var centro = Ponto(olhos[0] + s * olhos[2], yOlho);
                    pecas.Add(new Peca(item.Sprite, centro, new Vector2(lado), new Vector2(0.5f), 0f));
                }

                var esquerda = Ponto(olhos[0] - olhos[2], yOlho);
                var direita = Ponto(olhos[0] + olhos[2], yOlho);
                float x0 = MathF.Min(esquerda.X, direita.X) + lado / 2f * 0.9f;
                float x1 = MathF.Max(esquerda.X, direita.X) - lado / 2f * 0.9f;
                float grossura = MathF.Max(2f, tamanho.X * 0.012f);
                if (x1 > x0)
                    ponte = new Ponte(new Vector2(x0, esquerda.Y - grossura / 2f), new Vector2(x1 - x0, grossura));
                break;
            }

            case Desenho.Faixa:
            {
                var a = forma.Pescoco;
                float largura = a.Largura / 100f * tamanho.X * config.Escala;
                pecas.Add(new Peca(item.Sprite, Ponto(a.X, a.Y), new Vector2(largura), new Vector2(0.5f, PivoFaixa), 0f));
                break;
            }

            default:   // Topo
            {
                var a = forma.Cabeca;
                float largura = a.Largura / 100f * tamanho.X * config.Escala;
                var ponto = Ponto(a.X, a.Y) + new Vector2(sinal * config.Dx * a.Largura / 100f * tamanho.X, 0f);
                float pivoY = config.Rot != 0f ? PivoLaco : PivoTopo;
                pecas.Add(new Peca(item.Sprite, ponto, new Vector2(largura), new Vector2(0.5f, pivoY), sinal * config.Rot));
                break;
            }
        }

        return pecas;
    }
}
