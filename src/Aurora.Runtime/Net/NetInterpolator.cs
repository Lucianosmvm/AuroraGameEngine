using System.Numerics;

namespace Aurora.Runtime.Net;

/// <summary>
/// Histórico curto de posições de UMA entidade remota, com leitura no passado.
/// <para>Pacotes chegam a 20 Hz e o jogo desenha a 60: aplicar a posição crua faria o boneco
/// andar aos trancos, três frames parado e um pulo. A saída é atrasar de propósito o que se
/// mostra em ~100 ms e interpolar entre as duas amostras que cercam esse instante — assim
/// sempre existe um "próximo" conhecido pra onde caminhar, e o movimento fica contínuo.</para>
/// <para>Por isso não se extrapola: adivinhar além da última amostra acerta enquanto o outro
/// anda reto e erra feio quando ele vira, e o conserto aparece como teleporte na tela.</para>
/// </summary>
internal sealed class NetInterpolator
{
    /// <summary>Amostras guardadas. 8 a 20 Hz = 400 ms de histórico, folga suficiente pro
    /// atraso de interpolação padrão mais uma engasgada de rede.</summary>
    private const int Capacity = 8;

    private readonly (float Time, Vector2 Position, float Rotation)[] _samples =
        new (float, Vector2, float)[Capacity];

    private int _count;

    public bool HasData => _count > 0;

    /// <summary>Guarda um estado recebido. Amostra mais velha que a última é descartada:
    /// UDP reordena, e voltar no tempo faria o boneco andar de ré por um frame.</summary>
    public void Push(float time, Vector2 position, float rotation)
    {
        if (_count > 0 && time <= _samples[_count - 1].Time) return;

        if (_count == Capacity)
        {
            Array.Copy(_samples, 1, _samples, 0, Capacity - 1);
            _count--;
        }

        _samples[_count++] = (time, position, rotation);
    }

    /// <summary>
    /// Posição no instante pedido. Antes da primeira amostra devolve a mais antiga; depois da
    /// última, segura a última (rede atrasou — melhor congelar do que chutar).
    /// </summary>
    public bool Sample(float time, out Vector2 position, out float rotation)
    {
        position = Vector2.Zero;
        rotation = 0f;

        if (_count == 0) return false;

        if (time <= _samples[0].Time)
        {
            (_, position, rotation) = _samples[0];
            return true;
        }

        for (int i = 1; i < _count; i++)
        {
            if (_samples[i].Time < time) continue;

            var a = _samples[i - 1];
            var b = _samples[i];
            float span = b.Time - a.Time;
            float t = span <= 0f ? 1f : (time - a.Time) / span;

            position = Vector2.Lerp(a.Position, b.Position, t);
            rotation = LerpAngle(a.Rotation, b.Rotation, t);
            return true;
        }

        (_, position, rotation) = _samples[_count - 1];
        return true;
    }

    public void Clear() => _count = 0;

    /// <summary>Interpola ângulo pelo caminho curto. Sem isso, ir de 350° a 10° faz o boneco
    /// girar 340° no sentido errado em vez de 20° no certo.</summary>
    private static float LerpAngle(float from, float to, float t)
    {
        float delta = (to - from) % MathF.Tau;
        if (delta > MathF.PI) delta -= MathF.Tau;
        else if (delta < -MathF.PI) delta += MathF.Tau;

        return from + delta * t;
    }
}
