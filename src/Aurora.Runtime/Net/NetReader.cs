using System.Buffers.Binary;
using System.Text;

namespace Aurora.Runtime.Net;

/// <summary>
/// Lê um pacote recebido. Nenhum método lança: o conteúdo vem da rede, ou seja, de fonte
/// não confiável — um pacote cortado, adulterado ou de outra versão precisa virar
/// "descarta esse" e não uma exceção que mata o frame. Todo TryRead falho marca
/// <see cref="Failed"/> e devolve false.
/// </summary>
public ref struct NetReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _pos;

    private NetReader(ReadOnlySpan<byte> buffer, NetMessageType type)
    {
        _buffer = buffer;
        _pos = NetProtocol.HeaderSize;
        Type = type;
        Failed = false;
    }

    public NetMessageType Type { get; }

    /// <summary>True se alguma leitura passou do fim do pacote. Trate como "descarte".</summary>
    public bool Failed { get; private set; }

    /// <summary>
    /// Valida cabeçalho (magic + versão) e devolve um reader posicionado no payload.
    /// Falha em lixo, em pacote curto demais e em versão de protocolo diferente.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> packet, out NetReader reader)
    {
        reader = default;

        if (packet.Length < NetProtocol.HeaderSize) return false;
        if (packet[0] != NetProtocol.Magic0 || packet[1] != NetProtocol.Magic1) return false;
        if (packet[2] != NetProtocol.Version) return false;

        reader = new NetReader(packet, (NetMessageType)packet[3]);
        return true;
    }

    /// <summary>Versão do protocolo declarada no pacote. Só útil quando
    /// <see cref="TryParse"/> falhou e você quer dizer ao jogador "build diferente".</summary>
    public static bool TryPeekVersion(ReadOnlySpan<byte> packet, out byte version)
    {
        version = 0;
        if (packet.Length < NetProtocol.HeaderSize) return false;
        if (packet[0] != NetProtocol.Magic0 || packet[1] != NetProtocol.Magic1) return false;

        version = packet[2];
        return true;
    }

    public bool TryReadByte(out byte value)
    {
        value = 0;
        if (!Has(1)) return false;

        value = _buffer[_pos++];
        return true;
    }

    public bool TryReadUInt16(out ushort value)
    {
        value = 0;
        if (!Has(2)) return false;

        value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer[_pos..]);
        _pos += 2;
        return true;
    }

    public bool TryReadUInt32(out uint value)
    {
        value = 0;
        if (!Has(4)) return false;

        value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer[_pos..]);
        _pos += 4;
        return true;
    }

    public bool TryReadSingle(out float value)
    {
        value = 0f;
        if (!Has(4)) return false;

        value = BinaryPrimitives.ReadSingleLittleEndian(_buffer[_pos..]);
        _pos += 4;
        return true;
    }

    public bool TryReadString(out string value)
    {
        value = string.Empty;
        if (!TryReadByte(out byte length)) return false;
        if (!Has(length)) return false;

        value = Encoding.UTF8.GetString(_buffer.Slice(_pos, length));
        _pos += length;
        return true;
    }

    /// <summary>Tudo que sobrou do pacote. Par do <see cref="NetWriter.WriteBytes"/>: só faz
    /// sentido no último campo, porque consome até o fim.</summary>
    public bool TryReadRemaining(out ReadOnlySpan<byte> value)
    {
        value = _buffer[_pos..];
        _pos = _buffer.Length;
        return value.Length > 0;
    }

    private bool Has(int count)
    {
        if (_pos + count > _buffer.Length)
        {
            Failed = true;
            return false;
        }
        return true;
    }
}
