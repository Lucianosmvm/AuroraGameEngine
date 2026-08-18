using System.Buffers.Binary;
using System.Text;

namespace Aurora.Runtime.Net;

/// <summary>
/// Escreve um pacote num buffer emprestado (normalmente stackalloc), já com cabeçalho.
/// Sem alocação por pacote: a rede escreve dezenas de vezes por segundo e passar por
/// MemoryStream/BinaryWriter aqui geraria lixo constante pro GC no meio do loop do jogo.
/// </summary>
public ref struct NetWriter
{
    private readonly Span<byte> _buffer;
    private int _pos;

    public NetWriter(Span<byte> buffer, NetMessageType type)
    {
        _buffer = buffer;
        _pos = 0;
        Overflowed = false;
        WriteByte(NetProtocol.Magic0);
        WriteByte(NetProtocol.Magic1);
        WriteByte(NetProtocol.Version);
        WriteByte((byte)type);
    }

    /// <summary>True quando não coube tudo. O chamador decide o que fazer; o writer nunca
    /// estoura o buffer nem lança — pacote truncado é descartado do lado de lá pelo reader.</summary>
    public bool Overflowed { get; private set; }

    public readonly ReadOnlySpan<byte> Written => _buffer[.._pos];

    public void WriteByte(byte value)
    {
        if (!Reserve(1)) return;
        _buffer[_pos++] = value;
    }

    public void WriteUInt16(ushort value)
    {
        if (!Reserve(2)) return;
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer[_pos..], value);
        _pos += 2;
    }

    public void WriteUInt32(uint value)
    {
        if (!Reserve(4)) return;
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer[_pos..], value);
        _pos += 4;
    }

    /// <summary>Posição e rotação vão como float cru. Comprimir (fixed point, quantização de
    /// ângulo) só compensa quando a contagem de entidades sincronizadas crescer — com 8
    /// jogadores, o pacote inteiro cabe folgado num datagrama e o custo é irrelevante.</summary>
    public void WriteSingle(float value)
    {
        if (!Reserve(4)) return;
        BinaryPrimitives.WriteSingleLittleEndian(_buffer[_pos..], value);
        _pos += 4;
    }

    /// <summary>Copia bytes crus, sem prefixo de tamanho. Usado pelo envelope confiável, que
    /// carrega um pacote inteiro e vai até o fim do datagrama.</summary>
    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        if (!Reserve(value.Length)) return;

        value.CopyTo(_buffer[_pos..]);
        _pos += value.Length;
    }

    /// <summary>String curta: 1 byte de tamanho + UTF-8. Corta em
    /// <see cref="NetProtocol.MaxNameLength"/> em vez de recusar — nome longo é erro de
    /// digitação do jogador, não motivo pra falhar a conexão.</summary>
    public void WriteString(string value)
    {
        if (value.Length > NetProtocol.MaxNameLength)
            value = value[..NetProtocol.MaxNameLength];

        int byteCount = Encoding.UTF8.GetByteCount(value);

        // UTF-8 multibyte (acento, emoji) faz o corte por char virar mais de 255 bytes;
        // corta de novo por char até caber no prefixo de 1 byte.
        while (byteCount > byte.MaxValue && value.Length > 0)
        {
            value = value[..(value.Length - 1)];
            byteCount = Encoding.UTF8.GetByteCount(value);
        }

        if (!Reserve(1 + byteCount)) return;

        _buffer[_pos++] = (byte)byteCount;
        Encoding.UTF8.GetBytes(value, _buffer[_pos..]);
        _pos += byteCount;
    }

    private bool Reserve(int count)
    {
        if (_pos + count > _buffer.Length)
        {
            Overflowed = true;
            return false;
        }
        return true;
    }
}
