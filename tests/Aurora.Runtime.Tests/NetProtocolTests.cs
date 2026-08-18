using Aurora.Runtime.Net;

namespace Aurora.Runtime.Tests;

/// <summary>Formato do fio. Tudo que chega aqui veio da rede, ou seja, de fonte não
/// confiável — o foco é o leitor recusar lixo em vez de lançar exceção no meio do frame.</summary>
public class NetProtocolTests
{
    [Fact]
    public void PacoteEscritoVoltaIgualNaLeitura()
    {
        Span<byte> buffer = stackalloc byte[NetProtocol.MaxPacketSize];
        var writer = new NetWriter(buffer, NetMessageType.JoinAccepted);
        writer.WriteByte(3);
        writer.WriteString("Ana");
        writer.WriteUInt16(60000);

        Assert.True(NetReader.TryParse(writer.Written, out var reader));
        Assert.Equal(NetMessageType.JoinAccepted, reader.Type);

        Assert.True(reader.TryReadByte(out byte id));
        Assert.True(reader.TryReadString(out string name));
        Assert.True(reader.TryReadUInt16(out ushort port));

        Assert.Equal(3, id);
        Assert.Equal("Ana", name);
        Assert.Equal(60000, port);
        Assert.False(reader.Failed);
    }

    [Fact]
    public void LixoSemMagicNaoEAceito()
    {
        byte[] garbage = [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02];

        Assert.False(NetReader.TryParse(garbage, out _));
    }

    [Fact]
    public void PacoteMenorQueOCabecalhoNaoEAceito()
    {
        byte[] tiny = [NetProtocol.Magic0, NetProtocol.Magic1];

        Assert.False(NetReader.TryParse(tiny, out _));
    }

    [Fact]
    public void VersaoDiferenteNaoEAceitaMasEIdentificavel()
    {
        byte[] outraVersao =
        [
            NetProtocol.Magic0, NetProtocol.Magic1, NetProtocol.Version + 1, (byte)NetMessageType.Join,
        ];

        Assert.False(NetReader.TryParse(outraVersao, out _));

        Assert.True(NetReader.TryPeekVersion(outraVersao, out byte version));
        Assert.Equal(NetProtocol.Version + 1, version);
    }

    [Fact]
    public void LeituraAlemDoFimFalhaEmVezDeLancar()
    {
        Span<byte> buffer = stackalloc byte[NetProtocol.MaxPacketSize];
        var writer = new NetWriter(buffer, NetMessageType.Ping);
        writer.WriteByte(7);

        Assert.True(NetReader.TryParse(writer.Written, out var reader));
        Assert.True(reader.TryReadByte(out _));

        Assert.False(reader.TryReadByte(out _));
        Assert.True(reader.Failed);
    }

    [Fact]
    public void StringTruncadaNaoQuebraALeitura()
    {
        Span<byte> buffer = stackalloc byte[NetProtocol.MaxPacketSize];
        var writer = new NetWriter(buffer, NetMessageType.PeerJoined);
        writer.WriteString("Ana");

        // Corta no meio da string: é exatamente o que um datagrama truncado entrega.
        var cortado = writer.Written[..^1].ToArray();

        Assert.True(NetReader.TryParse(cortado, out var reader));
        Assert.False(reader.TryReadString(out _));
        Assert.True(reader.Failed);
    }

    [Fact]
    public void NomeMaiorQueOLimiteECortadoEmVezDeRecusado()
    {
        string longo = new('a', NetProtocol.MaxNameLength + 20);

        Span<byte> buffer = stackalloc byte[NetProtocol.MaxPacketSize];
        var writer = new NetWriter(buffer, NetMessageType.Join);
        writer.WriteString(longo);

        Assert.True(NetReader.TryParse(writer.Written, out var reader));
        Assert.True(reader.TryReadString(out string lido));
        Assert.Equal(NetProtocol.MaxNameLength, lido.Length);
    }

    [Fact]
    public void BufferPequenoMarcaOverflowEmVezDeEstourar()
    {
        // Cabeçalho + 1 byte: a string não cabe de jeito nenhum.
        Span<byte> buffer = stackalloc byte[NetProtocol.HeaderSize + 1];
        var writer = new NetWriter(buffer, NetMessageType.Join);
        writer.WriteString("nome que não cabe");

        Assert.True(writer.Overflowed);
        Assert.Equal(NetProtocol.HeaderSize, writer.Written.Length);
    }

    [Fact]
    public void JoinAcceptedComSalaCheiaCabeNumPacoteSo()
    {
        // Pior caso real do maior pacote da fase 1: 8 jogadores, todos com nome no limite.
        Span<byte> buffer = stackalloc byte[NetProtocol.MaxPacketSize];
        var writer = new NetWriter(buffer, NetMessageType.JoinAccepted);
        writer.WriteByte(7);
        writer.WriteByte(NetProtocol.MaxPlayersLimit);
        writer.WriteByte(NetProtocol.MaxPlayersLimit);

        for (byte i = 0; i < NetProtocol.MaxPlayersLimit; i++)
        {
            writer.WriteByte(i);
            writer.WriteString(new string('x', NetProtocol.MaxNameLength));
        }

        Assert.False(writer.Overflowed);
    }
}
