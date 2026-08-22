using System.IO.Compression;

namespace Aurora.SlandSurvivor.Tools;

/// <summary>
/// Escritor de PNG mínimo (RGBA, 8 bits, sem entrelaçamento) para as capturas de tela do
/// F12. A engine só sabe <em>ler</em> imagem (StbImageSharp), e trazer uma dependência de
/// escrita para gravar um print seria exagero — o formato cabe em três blocos:
/// cabeçalho, dados comprimidos com deflate e fim.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static void Write(string path, int width, int height, byte[] rgba, bool flipVertically = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        using var file = File.Create(path);
        file.Write(Signature);

        // IHDR: largura, altura, 8 bits por canal, tipo 6 (RGBA), sem filtro/entrelaçamento.
        var header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(file, "IHDR", header);

        WriteChunk(file, "IDAT", Compress(width, height, rgba, flipVertically));
        WriteChunk(file, "IEND", []);
    }

    /// <summary>Linhas com byte de filtro 0 + deflate embrulhado no cabeçalho zlib.</summary>
    private static byte[] Compress(int width, int height, byte[] rgba, bool flip)
    {
        int stride = width * 4;
        var raw = new byte[(stride + 1) * height];

        for (int y = 0; y < height; y++)
        {
            int source = (flip ? height - 1 - y : y) * stride;
            int destination = y * (stride + 1);
            raw[destination] = 0;                                  // filtro "None"
            Array.Copy(rgba, source, raw, destination + 1, stride);
        }

        using var output = new MemoryStream();
        output.WriteByte(0x78);                                    // zlib: deflate, janela 32K
        output.WriteByte(0x01);                                    // sem dicionário, compressão rápida

        using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(raw, 0, raw.Length);

        uint adler = Adler32(raw);
        output.WriteByte((byte)(adler >> 24));
        output.WriteByte((byte)(adler >> 16));
        output.WriteByte((byte)(adler >> 8));
        output.WriteByte((byte)adler);

        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        stream.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        uint crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, (int)crc);
        stream.Write(crcBytes);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset + 0] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (byte value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;

            table[i] = c;
        }

        return table;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFFu;

        foreach (byte value in type)
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);

        foreach (byte value in data)
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);

        return crc ^ 0xFFFFFFFFu;
    }
}
