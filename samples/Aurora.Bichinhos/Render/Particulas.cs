using System.Numerics;
using Aurora.Runtime.Graphics;

namespace Bichinhos;

/// <summary>Coraçõezinhos, notas, "Z", bolhas e números de dano que sobem e somem.</summary>
public sealed class Particulas
{
    private sealed class P
    {
        public string? Icone;
        public string? Texto;
        public Color Cor = Color.White;
        public Vector2 Pos;
        public Vector2 Vel;
        public float Vida;
        public float Duracao;
        public float Lado;
        public float Balanco;
        public bool Grande;
    }

    private readonly List<P> _lista = [];
    private readonly Random _rng = new();

    public void Icone(string icone, Vector2 pos, float lado = 56f, Vector2? vel = null, float duracao = 1.2f)
        => _lista.Add(new P
        {
            Icone = icone,
            Pos = pos,
            Vel = vel ?? new Vector2(((float)_rng.NextDouble() - 0.5f) * 60f, -110f),
            Duracao = duracao,
            Lado = lado,
            Balanco = (float)_rng.NextDouble() * 6f,
        });

    public void Texto(string texto, Vector2 pos, Color cor, bool grande = false, float duracao = 1.1f)
        => _lista.Add(new P { Texto = texto, Pos = pos, Vel = new Vector2(0f, -70f), Duracao = duracao, Cor = cor, Grande = grande });

    /// <summary>Vários ícones de uma vez espalhados em volta de um ponto.</summary>
    public void Explosao(string icone, Vector2 centro, int n, float lado = 40f)
    {
        for (int i = 0; i < n; i++)
        {
            float a = (float)_rng.NextDouble() * MathF.Tau;
            float v = 120f + (float)_rng.NextDouble() * 160f;
            Icone(icone, centro, lado * (0.7f + (float)_rng.NextDouble() * 0.6f),
                new Vector2(MathF.Cos(a) * v, MathF.Sin(a) * v - 80f), 0.9f);
        }
    }

    public void Atualizar(float dt)
    {
        for (int i = _lista.Count - 1; i >= 0; i--)
        {
            var p = _lista[i];
            p.Vida += dt;
            p.Pos += p.Vel * dt;
            p.Vel *= MathF.Pow(0.35f, dt);
            if (p.Vida >= p.Duracao)
                _lista.RemoveAt(i);
        }
    }

    public void Desenhar(Tinta tinta)
    {
        foreach (var p in _lista)
        {
            float t = p.Vida / p.Duracao;
            float alfa = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
            float cresce = MathF.Min(1f, p.Vida / 0.12f);

            if (p.Icone is not null)
            {
                var pos = p.Pos + new Vector2(MathF.Sin(p.Vida * 5f + p.Balanco) * 8f, 0f);
                tinta.Icone(p.Icone, pos, p.Lado * cresce, Color.White.WithAlpha(alfa));
            }
            else if (p.Texto is not null)
            {
                var fonte = p.Grande ? tinta.FonteMedia : tinta.Fonte;
                tinta.TextoContornado(p.Texto, p.Pos, p.Cor.WithAlpha(alfa), fonte, cresce);
            }
        }
    }

    public void Limpar() => _lista.Clear();
}
