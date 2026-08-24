using System.Numerics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Anda entre pontos fixos, em ida-e-volta ou em ciclo. É a plataforma móvel, o guarda que faz
/// ronda, o elevador, a nuvem que atravessa a tela.
///
/// <para>Os pontos são <b>relativos à posição inicial</b> da entidade, não absolutos: assim o
/// mesmo prefab de plataforma pode ser espalhado pela fase inteira e cada cópia patrulha em
/// volta de onde foi colocada. Movimento direto de Transform, sem física — pra perseguir alguém
/// contornando parede use <see cref="NavAgent"/> com Follow.</para>
/// </summary>
public sealed class PatrolPath : Behavior
{
    /// <summary>
    /// Pontos do trajeto, relativos à posição inicial, no formato <c>"x,y; x,y; x,y"</c> —
    /// ex.: <c>"0,0; 96,0"</c> é uma plataforma que vai 96px pra direita e volta.
    ///
    /// <para>Texto em vez de lista de objetos porque é um campo só no inspector, editável sem
    /// abrir sub-editor, e um trajeto de plataforma raramente passa de três ou quatro pontos.</para>
    /// </summary>
    public string Points = "0,0; 64,0";

    /// <summary>Pixels por segundo.</summary>
    public float Speed = 60f;

    /// <summary>Segundos parado em cada ponto antes de seguir. 0 = não para.</summary>
    public float WaitAtPoint;

    /// <summary>True: ao chegar no fim volta pelo mesmo caminho (vai-e-vem). False: salta de
    /// volta pro primeiro ponto e recomeça (ciclo fechado).</summary>
    public bool PingPong = true;

    /// <summary>Espelha o sprite conforme o lado pra onde está indo, igual ao
    /// <see cref="TopDownController"/>.</summary>
    public bool FlipSpriteByDirection;

    private Vector2 _origin;
    private Vector2[] _points = [];
    private int _index;
    private int _step = 1;
    private float _waitTimer;
    private string _parsedFrom = "";

    public override void Start()
    {
        _origin = Get<Transform>()?.Position ?? Vector2.Zero;
        Reparse();
    }

    public override void Update(float deltaTime)
    {
        // Reparse quando o texto muda: permite mexer no trajeto no inspector com o jogo rodando,
        // que é como se acerta o timing de uma plataforma na prática.
        if (_parsedFrom != Points)
            Reparse();

        if (_points.Length < 2 || Get<Transform>() is not { } transform)
            return;

        if (_waitTimer > 0f)
        {
            _waitTimer -= deltaTime;
            return;
        }

        var goal = _origin + _points[_index];
        var delta = goal - transform.Position;
        float distance = delta.Length();

        if (distance <= 0.01f)
        {
            AdvanceIndex();
            _waitTimer = WaitAtPoint;
            return;
        }

        var direction = delta / distance;
        transform.Position += direction * MathF.Min(Speed * deltaTime, distance);

        if (FlipSpriteByDirection && MathF.Abs(direction.X) > 0.001f && Get<SpriteRenderer>() is { } sprite)
            sprite.FlipX = direction.X < 0f;
    }

    private void AdvanceIndex()
    {
        if (!PingPong)
        {
            _index = (_index + 1) % _points.Length;
            return;
        }

        // Inverte o sentido nas pontas em vez de deixar o índice sair da faixa. Com dois pontos
        // isso é o vai-e-vem simples; com mais, percorre a lista e volta pelo mesmo caminho.
        if (_index + _step < 0 || _index + _step >= _points.Length)
            _step = -_step;

        _index += _step;
    }

    private void Reparse()
    {
        _parsedFrom = Points;
        _points = Parse(Points);
        _index = _points.Length > 1 ? 1 : 0;
        _step = 1;
    }

    /// <summary>"0,0; 96,0" → dois pontos. Trecho malformado é ignorado em vez de derrubar a
    /// cena: um ponto digitado errado no inspector não pode matar o jogo.</summary>
    private static Vector2[] Parse(string text)
    {
        var result = new List<Vector2>();

        foreach (string part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] pair = part.Split(',', StringSplitOptions.TrimEntries);
            if (pair.Length != 2
                || !float.TryParse(pair[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(pair[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float y))
                continue;

            result.Add(new Vector2(x, y));
        }

        return [.. result];
    }
}
