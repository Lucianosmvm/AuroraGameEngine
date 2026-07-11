namespace Aurora.Runtime.Audio;

/// <summary>Parser mínimo de RIFF/WAV (PCM 8 ou 16 bits, mono ou estéreo).</summary>
internal static class WavReader
{
    public static (short[] Samples, int Channels, int SampleRate) Read(Stream stream)
    {
        // Garante seekabilidade sem alocar o stream inteiro duas vezes.
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Seek(0, SeekOrigin.Begin);

        using var reader = new BinaryReader(ms, System.Text.Encoding.ASCII, leaveOpen: true);

        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("WAV inválido: sem cabeçalho RIFF.");
        reader.ReadInt32(); // tamanho total - 8
        if (new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("WAV inválido: não é WAVE.");

        int channels = 0, sampleRate = 0, bitsPerSample = 0;
        byte[]? pcmData = null;

        while (ms.Position < ms.Length - 7)
        {
            char[] id = reader.ReadChars(4);
            if (id.Length < 4) break;
            string chunkId = new(id);
            int chunkSize = reader.ReadInt32();
            long chunkEnd = ms.Position + chunkSize;

            if (chunkId == "fmt ")
            {
                reader.ReadInt16(); // audioFormat (1=PCM)
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // byteRate
                reader.ReadInt16(); // blockAlign
                bitsPerSample = reader.ReadInt16();
            }
            else if (chunkId == "data")
            {
                pcmData = reader.ReadBytes(chunkSize);
            }

            // Avança para o próximo chunk (alinhamento de 2 bytes).
            ms.Position = chunkEnd + (chunkSize & 1);
        }

        if (pcmData is null || channels == 0 || sampleRate == 0)
            throw new InvalidDataException("WAV incompleto: chunks 'fmt ' ou 'data' ausentes.");

        short[] samples;
        if (bitsPerSample == 16)
        {
            samples = new short[pcmData.Length / 2];
            Buffer.BlockCopy(pcmData, 0, samples, 0, pcmData.Length);
        }
        else if (bitsPerSample == 8)
        {
            // 8-bit WAV é unsigned (0..255); converte para signed 16-bit.
            samples = new short[pcmData.Length];
            for (int i = 0; i < pcmData.Length; i++)
                samples[i] = (short)((pcmData[i] - 128) << 8);
        }
        else
        {
            throw new InvalidDataException(
                $"WAV com {bitsPerSample} bits/sample não suportado. Use 8 ou 16 bits.");
        }

        return (samples, channels, sampleRate);
    }
}
