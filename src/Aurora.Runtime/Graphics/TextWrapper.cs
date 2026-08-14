using System.Text;

namespace Aurora.Runtime.Graphics;

/// <summary>
/// Quebra de linha por largura, separada da <see cref="Font"/> de propósito: a fonte só existe
/// com contexto de GL (o atlas de glifos vive na GPU), então o algoritmo recebe a largura de
/// cada caractere por delegate e fica testável sem janela nenhuma.
/// </summary>
public static class TextWrapper
{
    /// <summary>
    /// Devolve o texto com <c>'\n'</c> inseridos onde ele passaria de <paramref name="maxWidth"/>.
    /// Quebras já existentes no original são preservadas. A quebra acontece no último espaço da
    /// linha; palavra que sozinha não cabe é cortada no meio (vazar da caixa é pior).
    /// <paramref name="maxWidth"/> e <paramref name="advance"/> têm que estar na mesma unidade.
    /// </summary>
    /// <param name="advance">Largura de avanço de um caractere.</param>
    public static string Wrap(string text, float maxWidth, Func<char, float> advance)
    {
        ArgumentNullException.ThrowIfNull(advance);

        if (string.IsNullOrEmpty(text) || maxWidth <= 0f)
            return text;

        var result = new StringBuilder(text.Length + 16);
        int paragraphStart = 0;

        while (true)
        {
            int newline = text.IndexOf('\n', paragraphStart);
            int paragraphEnd = newline < 0 ? text.Length : newline;

            WrapParagraph(text, paragraphStart, paragraphEnd, maxWidth, advance, result);

            if (newline < 0)
                break;

            result.Append('\n');
            paragraphStart = newline + 1;
        }

        return result.ToString();
    }

    private static void WrapParagraph(string text, int start, int end, float maxWidth,
        Func<char, float> advance, StringBuilder result)
    {
        int lineStart = start;

        while (lineStart < end)
        {
            int lastSpace = -1;
            float width = 0f;
            int i = lineStart;

            for (; i < end; i++)
            {
                char c = text[i];
                if (c == ' ')
                    lastSpace = i;

                // O primeiro caractere da linha sempre entra, mesmo sendo mais largo que ela.
                // Sem essa exceção, largura minúscula (ou glifo gigante) não consumiria nada e
                // o laço externo giraria pra sempre.
                float w = advance(c);
                if (width + w > maxWidth && i > lineStart)
                    break;

                width += w;
            }

            if (i >= end)
            {
                result.Append(text, lineStart, end - lineStart);
                return;
            }

            // Preferência pelo último espaço da linha; sem espaço utilizável, corta a palavra.
            int breakAt = lastSpace > lineStart ? lastSpace : i;

            // Espaços no ponto de quebra são consumidos pra próxima linha não começar recuada.
            int nextStart = breakAt == lastSpace ? SkipSpaces(text, breakAt, end) : breakAt;

            // ...e nem a linha atual terminar em branco: lastSpace guarda o ÚLTIMO espaço da
            // sequência, então uma corrida de espaços deixaria os anteriores pendurados no fim.
            int lineEnd = breakAt;
            while (lineEnd > lineStart && text[lineEnd - 1] == ' ')
                lineEnd--;

            result.Append(text, lineStart, lineEnd - lineStart);

            // Só sobrava espaço depois da quebra: não vale abrir uma linha vazia no fim.
            if (nextStart >= end)
                return;

            result.Append('\n');
            lineStart = nextStart;
        }
    }

    private static int SkipSpaces(string text, int index, int end)
    {
        while (index < end && text[index] == ' ')
            index++;
        return index;
    }
}
