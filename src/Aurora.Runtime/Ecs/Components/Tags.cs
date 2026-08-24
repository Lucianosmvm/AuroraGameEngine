namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Etiquetas da entidade — o jeito de dizer "isto é um inimigo" sem depender do nome.
///
/// <para>Um jogo tem slime, slimeazul, slime_de_gelo, morcego. Mirar por nome exato obriga uma
/// ação pra cada tipo, e mirar por prefixo obriga a batizar todo mundo com o mesmo começo (e
/// impede pertencer a dois grupos). Com etiqueta, o nome fica livre pro que ele serve — dizer
/// QUEM é aquela entidade — e o grupo vira um dado à parte: <c>"inimigo, voador"</c>.</para>
///
/// <para>Onde aceita alvo (ação Damage/Heal/Destroy/..., ContactDamage.TargetPrefix,
/// Projectile.TargetPrefix), <c>#inimigo</c> significa "todos que têm esta etiqueta".</para>
/// </summary>
public sealed class Tags : IComponent
{
    /// <summary>Etiquetas separadas por vírgula ou espaço, sem o <c>#</c>: <c>"inimigo, voador"</c>.
    /// Maiúsculas não importam.</summary>
    public string Value = "";

    // Recortar a string a cada teste sujaria o GC num caminho que roda por contato de colisão.
    // A cache é invalidada por comparação com a string que a gerou: o campo é público e pode
    // mudar em jogo (script que promove o bicho a "chefe"), então guardar só o resultado
    // deixaria a etiqueta velha valendo pra sempre.
    private string _parsedFrom = "\0";
    private string[] _tags = [];

    private string[] Parsed
    {
        get
        {
            if (!ReferenceEquals(_parsedFrom, Value) && _parsedFrom != Value)
            {
                _parsedFrom = Value;
                _tags = Value.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries
                                                   | StringSplitOptions.TrimEntries);
            }
            return _tags;
        }
    }

    /// <summary>Tem esta etiqueta? Aceita com ou sem <c>#</c>, ignorando maiúsculas.</summary>
    public bool Has(string tag)
    {
        var wanted = tag.AsSpan().TrimStart('#').Trim();
        if (wanted.IsEmpty)
            return false;

        foreach (string mine in Parsed)
        {
            if (wanted.Equals(mine, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Filtro de alvo compartilhado pelos componentes que miram um grupo. Três formas, na ordem
    /// em que são testadas:
    /// <list type="bullet">
    ///   <item>vazio — passa qualquer entidade;</item>
    ///   <item><c>#etiqueta</c> — só quem tem a etiqueta;</item>
    ///   <item>qualquer outra coisa — prefixo do nome (o comportamento antigo, preservado pras
    ///   cenas que já existem).</item>
    /// </list>
    /// </summary>
    public static bool Matches(Entity entity, string filter)
    {
        if (filter.Length == 0)
            return true;

        if (filter[0] == '#')
            return entity.Get<Tags>()?.Has(filter) == true;

        return entity.Name.StartsWith(filter, StringComparison.OrdinalIgnoreCase);
    }
}
