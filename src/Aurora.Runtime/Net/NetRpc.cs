namespace Aurora.Runtime.Net;

/// <summary>Quem recebe um RPC.</summary>
public enum NetRpcTarget : byte
{
    /// <summary>Todo mundo na sala, inclusive quem mandou.</summary>
    All = 0,

    /// <summary>Todo mundo menos quem mandou.</summary>
    Others = 1,

    /// <summary>Só o host. É como um cliente pede alguma coisa a quem tem autoridade.</summary>
    Host = 2,

    /// <summary>Só um jogador específico.</summary>
    Player = 3,
}

/// <summary>Tipo de um argumento de RPC.</summary>
public enum NetRpcArgKind : byte
{
    Int = 0,
    Float = 1,
    Bool = 2,
    String = 3,
}

/// <summary>Um argumento de RPC.</summary>
public readonly struct NetRpcValue
{
    private NetRpcValue(NetRpcArgKind kind, int intValue, float floatValue, string stringValue)
    {
        Kind = kind;
        IntValue = intValue;
        FloatValue = floatValue;
        StringValue = stringValue;
    }

    public NetRpcArgKind Kind { get; }
    public int IntValue { get; }
    public float FloatValue { get; }
    public string StringValue { get; }

    public static NetRpcValue FromInt(int value) => new(NetRpcArgKind.Int, value, value, string.Empty);
    public static NetRpcValue FromFloat(float value) => new(NetRpcArgKind.Float, (int)value, value, string.Empty);
    public static NetRpcValue FromBool(bool value) => new(NetRpcArgKind.Bool, value ? 1 : 0, value ? 1f : 0f, string.Empty);
    public static NetRpcValue FromString(string value) => new(NetRpcArgKind.String, 0, 0f, value);

    /// <summary>
    /// Converte um valor do jogo pro formato do fio. Números estreitos (byte, short, ushort)
    /// viram int e double vira float de propósito: são os tipos que aparecem naturalmente numa
    /// chamada (<c>Send("Dano", netId, 12.5)</c>) e recusá-los só faria o jogo encher a chamada
    /// de cast sem ganhar nada.
    /// </summary>
    public static NetRpcValue From(object? value) => value switch
    {
        null => FromString(string.Empty),
        int i => FromInt(i),
        float f => FromFloat(f),
        bool b => FromBool(b),
        string s => FromString(s),
        byte b => FromInt(b),
        short sh => FromInt(sh),
        ushort us => FromInt(us),
        long l => FromInt((int)l),
        double d => FromFloat((float)d),
        Enum e => FromInt(Convert.ToInt32(e)),
        _ => throw new ArgumentException(
            $"Tipo {value.GetType().Name} não pode ir num RPC. Use int, float, bool, string ou enum — " +
            "pra mandar uma entidade, mande o NetId dela.", nameof(value)),
    };
}

/// <summary>
/// Os argumentos que chegaram num RPC, mais quem mandou. Os getters convertem entre número e
/// texto quando dá: um valor mandado como int lido como float funciona, e um índice fora da
/// lista devolve o fallback em vez de estourar — pacote vem da rede, e handler de jogo não é
/// lugar pra tratar exceção de índice.
/// </summary>
public sealed class NetRpcArgs
{
    private readonly NetRpcValue[] _values;

    internal NetRpcArgs(string name, byte senderId, NetRpcValue[] values)
    {
        Name = name;
        SenderId = senderId;
        _values = values;
    }

    /// <summary>Nome registrado do RPC.</summary>
    public string Name { get; }

    /// <summary>Jogador que originou a chamada (0 = host). No host este valor é o id real do
    /// peer que mandou o pacote, não o que ele afirmou ser — dá pra confiar pra validar.</summary>
    public byte SenderId { get; }

    public int Count => _values.Length;

    public NetRpcArgKind KindOf(int index)
        => index >= 0 && index < _values.Length ? _values[index].Kind : NetRpcArgKind.Int;

    public int GetInt(int index, int fallback = 0)
        => TryGet(index, out var value) ? value.IntValue : fallback;

    public float GetFloat(int index, float fallback = 0f)
        => TryGet(index, out var value) ? value.FloatValue : fallback;

    public bool GetBool(int index, bool fallback = false)
        => TryGet(index, out var value) ? value.IntValue != 0 : fallback;

    public string GetString(int index, string fallback = "")
    {
        if (!TryGet(index, out var value)) return fallback;

        return value.Kind switch
        {
            NetRpcArgKind.String => value.StringValue,
            NetRpcArgKind.Float => value.FloatValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NetRpcArgKind.Bool => value.IntValue != 0 ? "true" : "false",
            _ => value.IntValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    private bool TryGet(int index, out NetRpcValue value)
    {
        if (index >= 0 && index < _values.Length)
        {
            value = _values[index];
            return true;
        }

        value = default;
        return false;
    }
}

/// <summary>O que fazer quando um RPC chega.</summary>
public delegate void NetRpcHandler(NetRpcArgs args);
