using System.Numerics;

namespace Bichinhos;

public enum TipoItem { Maca, Doce, Estrela, Coco }

public sealed class ItemCaindo
{
    public TipoItem Tipo;
    public float X;
    public float Y;
    public float Velocidade;
}

/// <summary>Algo que aconteceu no frame (pegou, errou, caiu no chão), pra a tela soltar som e
/// partículas sem precisar adivinhar.</summary>
public readonly record struct EventoPegar(TipoItem Tipo, Vector2 Posicao, bool Pegou, int Pontos);

/// <summary>
/// A brincadeira de habilidade: frutas e doces caem, o bichinho corre de um lado pro outro
/// debaixo delas. É a regra pura (nada de desenho), pra testar sem janela.
///
/// <para>Maçã vale 1, doce 3, estrela 5; cocô tira 2 (a pontuação nunca fica negativa). Dura 20 segundos e acelera: no começo dá tempo de pensar,
/// no fim é reflexo. A fruta que passa batida não custa nada; só o cocô pego.</para>
/// </summary>
public sealed class PegaFrutas
{
    public const float Largura = 720f;
    public const float Duracao = 20f;

    /// <summary>Altura onde o item é apanhado (a boca do bichinho) e onde ele some no chão.</summary>
    public const float LinhaDaBoca = 1000f;
    public const float Chao = 1190f;

    public const float RaioDaBoca = 105f;
    public const float Margem = 90f;
    private const float VelocidadeDoBicho = 1300f;

    private readonly Random _rng;
    private readonly List<EventoPegar> _eventos = [];
    private float _proximoItem = 0.6f;

    public PegaFrutas(Random rng) => _rng = rng;

    public float Tempo { get; private set; }
    public float Restante => MathF.Max(0f, Duracao - Tempo);
    public bool Terminou => Tempo >= Duracao;
    public int Pontos { get; private set; }
    public int Pegos { get; private set; }
    public float BichoX { get; private set; } = Largura / 2f;
    public List<ItemCaindo> Itens { get; } = [];

    /// <summary>Quão avançado o jogo está, de 0 a 1.</summary>
    public float Progresso => Math.Clamp(Tempo / Duracao, 0f, 1f);

    public void Atualizar(float dt, float alvoX)
    {
        if (Terminou)
            return;

        Tempo += dt;

        float destino = Math.Clamp(alvoX, Margem, Largura - Margem);
        float passo = MathF.Min(MathF.Abs(destino - BichoX), VelocidadeDoBicho * dt);
        BichoX += MathF.Sign(destino - BichoX) * passo;

        _proximoItem -= dt;
        if (_proximoItem <= 0f && Restante > 0.8f)
        {
            Soltar();
            _proximoItem = Lerp(0.8f, 0.4f, Progresso) * (0.8f + (float)_rng.NextDouble() * 0.4f);
        }

        for (int i = Itens.Count - 1; i >= 0; i--)
        {
            var item = Itens[i];
            item.Y += item.Velocidade * dt;

            bool naBoca = item.Y >= LinhaDaBoca && MathF.Abs(item.X - BichoX) <= RaioDaBoca;
            if (naBoca)
            {
                int pontos = ValorDe(item.Tipo);
                int antes = Pontos;
                Pontos = Math.Max(0, Pontos + pontos);
                if (item.Tipo != TipoItem.Coco)
                    Pegos++;
                _eventos.Add(new EventoPegar(item.Tipo, new Vector2(item.X, item.Y), true, Pontos - antes));
                Itens.RemoveAt(i);
            }
            else if (item.Y >= Chao)
            {
                _eventos.Add(new EventoPegar(item.Tipo, new Vector2(item.X, Chao), false, 0));
                Itens.RemoveAt(i);
            }
        }
    }

    /// <summary>Entrega (e esvazia) o que aconteceu desde a última chamada.</summary>
    public List<EventoPegar> DrenarEventos()
    {
        var copia = new List<EventoPegar>(_eventos);
        _eventos.Clear();
        return copia;
    }

    public static int ValorDe(TipoItem tipo) => tipo switch
    {
        TipoItem.Maca => 1,
        TipoItem.Doce => 3,
        TipoItem.Estrela => 5,
        _ => -2,
    };

    private void Soltar()
    {
        double sorte = _rng.NextDouble();
        var tipo = sorte < 0.55 ? TipoItem.Maca
            : sorte < 0.70 ? TipoItem.Doce
            : sorte < 0.78 ? TipoItem.Estrela
            : TipoItem.Coco;

        float velocidade = Lerp(300f, 520f, Progresso) + (float)_rng.NextDouble() * 60f;
        if (tipo == TipoItem.Doce) velocidade += 60f;
        if (tipo == TipoItem.Estrela) velocidade += 130f;

        Itens.Add(new ItemCaindo
        {
            Tipo = tipo,
            X = Margem + (float)_rng.NextDouble() * (Largura - 2 * Margem),
            Y = 260f,
            Velocidade = velocidade,
        });
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // ------------------------------------------------------------------ recompensa

    public const int PontosParaGanhar = 10;

    /// <summary>O que a partida rende. Dez pontos já contam como vitória (como no palpite, onde
    /// acertar 3 de 5 ganha).</summary>
    public static (int Moedas, int Xp, bool Ganhou) Recompensa(int pontos)
        => (Math.Min(10, pontos / 3), 3 + pontos / 2, pontos >= PontosParaGanhar);
}
