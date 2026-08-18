using System.Numerics;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Input;

namespace Aurora.Coop;

/// <summary>
/// Analógico virtual que nasce onde o dedo encosta, em vez de ficar num canto fixo.
/// <para>Fixo obriga o jogador a olhar pra baixo pra achar o controle; flutuante ele acerta
/// de primeira toda vez. Só reage à metade esquerda da tela, deixando a direita livre pros
/// botões — e o dedo é acompanhado pelo id do toque, então o segundo dedo em outro lugar não
/// rouba o analógico.</para>
/// </summary>
public sealed class TouchStick
{
    private int? _touchId;
    private Vector2 _center;

    public float Radius { get; set; } = 90f;

    /// <summary>Direção de -1 a 1 nos dois eixos. Zero quando ninguém está tocando.</summary>
    public Vector2 Value { get; private set; }

    public bool Active => _touchId is not null;

    public void Update(InputManager input, float maxX)
    {
        var touches = input.ActiveTouches;

        if (_touchId is { } id)
        {
            foreach (var (touchId, position) in touches)
            {
                if (touchId != id) continue;

                Follow(position);
                return;
            }

            // Dedo levantou.
            _touchId = null;
            Value = Vector2.Zero;
            return;
        }

        foreach (var (touchId, position) in touches)
        {
            if (position.X > maxX) continue;

            _touchId = touchId;
            _center = position;
            Value = Vector2.Zero;
            return;
        }

        Value = Vector2.Zero;
    }

    private void Follow(Vector2 position)
    {
        var delta = position - _center;
        float distance = delta.Length();

        if (distance < 0.001f)
        {
            Value = Vector2.Zero;
            return;
        }

        // Passando do raio, continua valendo 1 em vez de crescer: o limite de velocidade tem
        // que ser o mesmo pra quem arrasta 90 px e pra quem arrasta a tela inteira.
        Value = delta / distance * MathF.Min(distance / Radius, 1f);
    }

    public void Draw(SpriteBatch batch)
    {
        if (!Active) return;

        batch.DrawGlow(_center, Radius, new Color(1f, 1f, 1f, 0.18f));
        batch.DrawGlow(_center + Value * Radius, Radius * 0.42f, new Color(1f, 1f, 1f, 0.40f));
    }
}
