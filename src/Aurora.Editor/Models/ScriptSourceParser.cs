using System.Text;
using System.Text.RegularExpressions;

namespace Aurora.Editor.Models;

/// <summary>
/// Lê classes [SceneScript] direto do texto do .cs, sem build e sem rodar o jogo — é o que
/// deixa um script recém-salvo no editor interno aparecer na hora no "+ Add Componente".
/// <para>É deliberadamente uma leitura de texto, não um parser de C# de verdade: o catálogo
/// oficial continua vindo de <see cref="GameScriptDiscovery"/> (reflection no assembly do jogo,
/// botão "↻"/Play), que é a fonte da verdade. Este aqui só antecipa o resultado pro caso comum
/// (campos públicos float/int/bool/string com default literal), aceitando errar em código
/// exótico — o pior caso é um campo faltando no inspector até o próximo "↻".</para>
/// </summary>
public static class ScriptSourceParser
{
    private static readonly Regex AttributeRegex = new(
        @"\[\s*SceneScript\s*(?:\(\s*(?:""(?<alias>[^""]*)"")?\s*\))?\s*\]", RegexOptions.Compiled);

    private static readonly Regex ClassRegex = new(
        @"\bclass\s+(?<name>[A-Za-z_]\w*)", RegexOptions.Compiled);

    // "public float Speed = 200f;" — modificador extra (readonly/volatile/required) passa;
    // static/const não casam de propósito: o runtime só lê membros de instância
    // (ver SceneSerializer.GetScriptableMembers).
    private static readonly Regex FieldRegex = new(
        @"^[ \t]*public\s+(?:(?:readonly|volatile|required)\s+)*(?<type>float|int|bool|string)\s+(?<name>[A-Za-z_]\w*)\s*(?:=\s*(?<default>[^;]+?))?\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // "public float Speed { get; set; } = 200f;" — precisa de get E set, idem runtime.
    private static readonly Regex PropertyRegex = new(
        @"^[ \t]*public\s+(?<type>float|int|bool|string)\s+(?<name>[A-Za-z_]\w*)\s*\{\s*get\s*;\s*set\s*;\s*\}\s*(?:=\s*(?<default>[^;]+?)\s*;)?",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Scripts declarados no arquivo. Lista vazia se o arquivo não tiver [SceneScript]
    /// (ou não puder ser lido) — nunca lança; quem chama trata "vazio" como "nada a registrar".</summary>
    public static IReadOnlyList<GameScriptDiscovery.ScriptInfo> ParseFile(string path)
    {
        try { return Parse(File.ReadAllText(path)); }
        catch { return []; }
    }

    public static IReadOnlyList<GameScriptDiscovery.ScriptInfo> Parse(string source)
    {
        string code = StripComments(source);
        var result = new List<GameScriptDiscovery.ScriptInfo>();

        foreach (Match attribute in AttributeRegex.Matches(code))
        {
            int after = attribute.Index + attribute.Length;
            var classMatch = ClassRegex.Match(code, after);
            if (!classMatch.Success)
                continue;

            // "abstract" entre o atributo e a classe: o runtime pula tipos abstratos.
            if (code[after..classMatch.Index].Contains("abstract"))
                continue;

            string body = ExtractBody(code, classMatch.Index + classMatch.Length);
            string alias = attribute.Groups["alias"].Value;
            string name = alias.Length > 0 ? alias : classMatch.Groups["name"].Value;
            result.Add(new GameScriptDiscovery.ScriptInfo(name, ParseFields(body)));
        }

        return result;
    }

    /// <summary>Nome da primeira classe [SceneScript] do arquivo — o nome do <em>tipo</em>, não o
    /// alias de <c>[SceneScript("X")]</c>, porque quem usa isso é o nome do arquivo .cs.
    /// Null quando não há classe marcada.</summary>
    public static string? FindPrimaryClassName(string source)
    {
        string code = StripComments(source);
        var attribute = AttributeRegex.Match(code);
        if (!attribute.Success)
            return null;

        var classMatch = ClassRegex.Match(code, attribute.Index + attribute.Length);
        return classMatch.Success ? classMatch.Groups["name"].Value : null;
    }

    private static List<GameScriptDiscovery.ScriptField> ParseFields(string body)
    {
        var fields = new List<GameScriptDiscovery.ScriptField>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Propriedade primeiro: "public float X { get; set; } = 1f;" também casa o regex de
        // campo (na parte "public float X"), então quem chegar depois com o mesmo nome perde.
        foreach (Match match in PropertyRegex.Matches(body).Concat(FieldRegex.Matches(body)))
        {
            string name = match.Groups["name"].Value;
            if (!seen.Add(name))
                continue;
            string kind = match.Groups["type"].Value;
            fields.Add(new GameScriptDiscovery.ScriptField(
                name, kind, NormalizeDefault(kind, match.Groups["default"].Value)));
        }

        return fields;
    }

    /// <summary>Literal C# → o mesmo texto que o describe-scripts do runtime produziria
    /// ("200f" → "200", <c>"Pocao"</c> → Pocao). Expressão que não seja literal simples vira o
    /// default do tipo, igual ao que o campo teria sem inicializador.</summary>
    internal static string NormalizeDefault(string kind, string literal)
    {
        string text = literal.Trim();
        if (text.Length == 0)
            return kind switch { "float" => "0", "int" => "0", "bool" => "false", _ => "" };

        switch (kind)
        {
            case "string":
                if (text.StartsWith("@\"", StringComparison.Ordinal))
                    text = text[1..];
                return text.Length >= 2 && text[0] == '"' && text[^1] == '"'
                    ? text[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\")
                    : "";
            case "bool":
                return text is "true" or "false" ? text : "false";
            case "int":
                return int.TryParse(text.Replace("_", ""), out int i)
                    ? i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "0";
            default:
                string number = text.TrimEnd('f', 'F', 'd', 'D', 'm', 'M').Replace("_", "");
                return float.TryParse(number, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float f)
                    ? f.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "0";
        }
    }

    /// <summary>Corpo entre chaves a partir de <paramref name="start"/>, por contagem de
    /// profundidade. Tipos aninhados entram junto, mas os regex de campo exigem "public" no
    /// começo da linha, então o estrago fica em campo a mais, nunca em campo errado de tipo.</summary>
    private static string ExtractBody(string code, int start)
    {
        int open = code.IndexOf('{', start);
        if (open < 0)
            return "";

        int depth = 0;
        for (int i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0)
                return code[(open + 1)..i];
        }
        return code[(open + 1)..];
    }

    /// <summary>Troca comentário por espaço preservando literais — um default tipo
    /// <c>"http://x"</c> não pode virar comentário, e um <c>// [SceneScript]</c> comentado não
    /// pode virar script.</summary>
    internal static string StripComments(string source)
    {
        var output = new StringBuilder(source.Length);

        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                output.Append('\n');
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? source.Length - 1 : end + 1;
                output.Append(' ');
                continue;
            }

            if (c is '"' or '\'')
            {
                i = SkipLiteral(source, i, out string literal);
                output.Append(literal);
                continue;
            }

            output.Append(c);
        }

        return output.ToString();
    }

    /// <summary>Índice do último caractere do literal que começa em <paramref name="start"/>
    /// (aspas simples, duplas e @"..." — literal raw """...""" cai no caso duplo e no pior
    /// caso o parser perde um campo, que o "↻" recupera).</summary>
    private static int SkipLiteral(string source, int start, out string literal)
    {
        char quote = source[start];
        bool verbatim = start > 0 && quote == '"' && source[start - 1] == '@';

        for (int i = start + 1; i < source.Length; i++)
        {
            if (!verbatim && source[i] == '\\')
            {
                i++;
                continue;
            }
            if (source[i] == quote)
            {
                if (verbatim && i + 1 < source.Length && source[i + 1] == quote)
                {
                    i++;
                    continue;
                }
                literal = source[start..(i + 1)];
                return i;
            }
            if (!verbatim && source[i] == '\n')
                break;
        }

        literal = source[start..];
        return source.Length - 1;
    }
}
