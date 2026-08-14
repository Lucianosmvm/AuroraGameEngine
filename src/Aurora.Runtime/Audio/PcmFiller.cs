namespace Aurora.Runtime.Audio;

/// <summary>
/// Fonte de amostras PCM em float, intercaladas por canal. Existe pra separar a lógica de
/// preencher um buffer de streaming (com loop de faixa) do decodificador concreto — o
/// <see cref="NVorbis.VorbisReader"/> só roda com um arquivo ogg de verdade em mãos, e é
/// justamente essa lógica que precisa ser testável.
/// </summary>
internal interface IPcmSource
{
    /// <summary>Lê até <paramref name="count"/> amostras a partir de <paramref name="offset"/>.
    /// Devolve quantas leu; 0 significa fim do fluxo.</summary>
    int Read(float[] buffer, int offset, int count);

    /// <summary>Volta ao início da faixa (loop).</summary>
    void Rewind();
}

internal static class PcmFiller
{
    /// <summary>
    /// Enche <paramref name="floats"/> até o topo (ou até a fonte secar) e converte o que foi
    /// lido pra PCM 16 bits em <paramref name="pcm"/>. Devolve quantas amostras foram escritas;
    /// 0 quer dizer que não veio nada — fim da faixa sem loop, ou fonte vazia.
    /// </summary>
    public static int Fill(IPcmSource source, float[] floats, short[] pcm, bool looping)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (pcm.Length < floats.Length)
            throw new ArgumentException("O buffer PCM precisa ser pelo menos do tamanho do buffer float.", nameof(pcm));

        int total = 0;
        while (total < floats.Length)
        {
            int read = source.Read(floats, total, floats.Length - total);
            if (read > 0)
            {
                total += read;
                continue;
            }

            if (!looping)
                break;

            // Rebobina e tenta de novo. Se nem depois de voltar ao início vem amostra, a fonte
            // não tem áudio nenhum — sair aqui é o que impede o laço de girar pra sempre.
            source.Rewind();
            read = source.Read(floats, total, floats.Length - total);
            if (read <= 0)
                break;

            total += read;
        }

        for (int i = 0; i < total; i++)
            pcm[i] = ToPcm16(floats[i]);

        return total;
    }

    /// <summary>Converte uma amostra -1..1 pra PCM 16 bits, com saturação (sinal fora da faixa
    /// satura em vez de dar wrap e virar estalo).</summary>
    public static short ToPcm16(float sample)
        => (short)Math.Clamp((int)(sample * 32767f), short.MinValue, short.MaxValue);
}
