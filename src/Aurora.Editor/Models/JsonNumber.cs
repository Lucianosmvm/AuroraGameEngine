using System.Text.Json.Nodes;

namespace Aurora.Editor.Models;

/// <summary>
/// Leitura numérica tolerante de nós JSON.
/// <para>
/// <c>JsonValue.GetValue&lt;float&gt;()</c> só funciona quando o valor foi lido de um arquivo
/// (aí ele é um <c>JsonElement</c>, que converte número pra qualquer tipo numérico) ou quando o
/// tipo pedido é exatamente o tipo guardado. Um nó criado em memória com um literal inteiro —
/// <c>["Value"] = 50</c> — guarda um <c>Int32</c> boxeado e estoura
/// <c>InvalidOperationException: A value of type 'System.Int32' cannot be converted to a
/// 'System.Single'</c> na primeira leitura como float.
/// </para>
/// <para>
/// Estes helpers tentam cada tipo numérico antes de desistir, então tanto faz de onde o nó veio.
/// Use-os em vez de <c>GetValue&lt;float&gt;()</c>/<c>GetValue&lt;int&gt;()</c> ao ler cena,
/// banco de dados ou qualquer JSON que o editor também escreve.
/// </para>
/// </summary>
internal static class JsonNumber
{
    /// <summary>Lê o nó como float; devolve <paramref name="fallback"/> se não for número.</summary>
    internal static float AsFloat(this JsonNode? node, float fallback)
    {
        if (node is not JsonValue jv) return fallback;
        if (jv.TryGetValue(out float  f)) return f;
        if (jv.TryGetValue(out double d)) return (float)d;
        if (jv.TryGetValue(out long   l)) return l;
        if (jv.TryGetValue(out int    i)) return i;
        if (jv.TryGetValue(out decimal m)) return (float)m;
        return fallback;
    }

    /// <summary>Lê o nó como int, truncando decimais; <paramref name="fallback"/> se não for número.</summary>
    internal static int AsInt(this JsonNode? node, int fallback)
    {
        if (node is not JsonValue jv) return fallback;
        if (jv.TryGetValue(out int    i)) return i;
        if (jv.TryGetValue(out long   l)) return (int)l;
        if (jv.TryGetValue(out double d)) return (int)d;
        if (jv.TryGetValue(out float  f)) return (int)f;
        if (jv.TryGetValue(out decimal m)) return (int)m;
        return fallback;
    }
}
