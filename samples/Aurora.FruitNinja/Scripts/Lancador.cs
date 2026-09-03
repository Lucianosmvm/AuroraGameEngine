using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace FruitNinja;

/// <summary>
/// O lançador: decide QUANDO e O QUÊ sobe pela borda de baixo.
///
/// <para>Ele não conhece fruta nenhuma pelo nome — pergunta ao <see cref="CatalogoFrutas"/> e
/// à <see cref="CurvaNivel"/>. É o que faz "quero uma fruta nova" ser uma entrada de JSON, e
/// "quero o jogo mais difícil" ser um número na curva.</para>
///
/// <para>O arremesso é balística de escola: escolhe-se a ALTURA que a fruta deve alcançar e a
/// velocidade sai dela (<c>v = √(2·g·h)</c>). Sortear a velocidade direto daria fruta que sai
/// pelo topo da tela e fruta que mal levanta.</para>
/// </summary>
[SceneScript]
public sealed class Lancador : Behavior
{
    /// <summary>Silêncio antes da primeira leva, pra o jogador ver a tela antes de reagir.</summary>
    public float AtrasoInicial = 0.9f;

    /// <summary>Faixa horizontal onde as frutas nascem, como fração da largura da arena.
    /// 0.32 = do meio até um terço pra cada lado — nascer colado na borda dá fruta que sobe
    /// e cai fora da tela.</summary>
    public float Espalhamento = 0.32f;

    /// <summary>Altura mínima e máxima do ápice, em pixels acima do topo da tela ser negativo.
    /// O ápice é o ponto mais alto do voo: quanto mais alto, mais tempo no ar.</summary>
    public float ApiceMinimo = -80f;
    public float ApiceMaximo = -520f;

    private readonly Random _rng = new();
    private readonly List<(FrutaDef Def, float Atraso, float Forca)> _fila = [];
    private float _proximaOnda;

    public override void Start() => _proximaOnda = AtrasoInicial;

    public override void Update(float deltaTime)
    {
        if (World is null || Partida.Atual is not { } partida || partida.Acabou)
            return;

        float dt = deltaTime * partida.EscalaTempo;

        // Frutas da leva atual saindo uma a uma: soltar as cinco no mesmo frame empilharia
        // todas no mesmo instante do voo e o leque viraria uma coluna só.
        for (int i = _fila.Count - 1; i >= 0; i--)
        {
            var item = _fila[i];
            item.Atraso -= dt;

            if (item.Atraso > 0f)
            {
                _fila[i] = item;
                continue;
            }

            _fila.RemoveAt(i);
            Lancar(item.Def, item.Forca);
        }

        _proximaOnda -= dt;
        if (_proximaOnda > 0f)
            return;

        MontarOnda(partida);
    }

    private void MontarOnda(Partida partida)
    {
        var onda = CurvaNivel.Da(partida.Nivel);
        bool frenesi = partida.Ativo(EfeitoDePoder.Frenesi);

        // Frenesi: chuva de fruta e nenhuma bomba, igual ao original — é o momento de cortar
        // sem pensar, e uma bomba no meio disso puniria o jogador por fazer o que o poder pede.
        int quantidade = _rng.Next(onda.QuantidadeMinima, onda.QuantidadeMaxima + 1)
            + (frenesi ? 3 : 0);

        _proximaOnda = frenesi ? onda.Intervalo * 0.30f : onda.Intervalo;

        for (int i = 0; i < quantidade; i++)
        {
            var def = Sortear(partida.Nivel, onda, frenesi);
            if (def is null)
                continue;

            _fila.Add((def, i * (float)(0.05 + _rng.NextDouble() * 0.16), onda.MultiplicadorDeForca));
        }
    }

    private FrutaDef? Sortear(int nivel, Onda onda, bool frenesi)
    {
        double dado = _rng.NextDouble();

        if (!frenesi && dado < onda.ChanceDeBomba
            && CatalogoFrutas.Atual.Sortear(nivel, TipoDeFruta.Bomba, _rng) is { } bomba)
            return bomba;

        if (dado > 1.0 - onda.ChanceDePoder
            && CatalogoFrutas.Atual.Sortear(nivel, TipoDeFruta.Poder, _rng) is { } poder)
            return poder;

        return CatalogoFrutas.Atual.Sortear(nivel, TipoDeFruta.Fruta, _rng);
    }

    private void Lancar(FrutaDef def, float forca)
    {
        if (World?.Assets is null)
            return;

        float x0 = (float)(_rng.NextDouble() * 2.0 - 1.0) * Arena.Largura * Espalhamento;
        float apice = ApiceMinimo + (float)_rng.NextDouble() * (ApiceMaximo - ApiceMinimo);

        float subida = Arena.AlturaDeLancamento - apice;
        float velY = -MathF.Sqrt(2f * Arena.Gravidade * subida) * forca;

        // Tempo do voo inteiro (sobe e volta ao ponto de partida): é o que converte "quero que
        // ela caia mais ou menos ali" em velocidade horizontal.
        float tempoNoAr = 2f * MathF.Abs(velY) / Arena.Gravidade;
        float destinoX = -MathF.Sign(x0 == 0f ? 1f : x0)
            * (float)_rng.NextDouble() * Arena.Largura * 0.34f;
        float velX = (destinoX - x0) / MathF.Max(tempoNoAr, 0.1f);

        var entidade = World.CreateEntity(def.Id);
        entidade.Add(new Transform(new Vector2(x0, Arena.AlturaDeLancamento)));
        entidade.Add(new SpriteRenderer(World.Assets.LoadTexture(def.Sprite), layer: 10)
        {
            Size = new Vector2(def.Tamanho, def.Tamanho),
        });
        entidade.Add(new Fruta
        {
            Id = def.Id,
            VelX = velX,
            VelY = velY,
            Giro = (float)(_rng.NextDouble() * 2.0 - 1.0) * 3.2f,
            Raio = def.Tamanho * def.RaioCorte,
        });
    }
}
