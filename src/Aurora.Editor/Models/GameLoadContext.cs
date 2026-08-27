using System.Reflection;
using System.Runtime.Loader;

namespace Aurora.Editor.Models;

/// <summary>
/// Contexto colecionável onde vive o assembly do jogo do usuário, para que o editor possa
/// descarregá-lo e recarregar o código recompilado sem reiniciar.
///
/// <para>O ponto central é o que NÃO entra aqui: <c>Aurora.Runtime</c> é delegado ao contexto
/// padrão, o mesmo que o editor usa. Assim o <c>Transform</c> que o jogo cria e o
/// <c>Transform</c> que o inspector edita são o mesmo tipo, e escrever numa entidade viva é
/// atribuição direta — sem serializar, sem reflection, sem IPC. Se o runtime fosse carregado
/// aqui dentro, seriam dois tipos homônimos e incompatíveis, e todo o ganho sumiria.</para>
///
/// <para>A lista de compartilhados é explícita de propósito. Delegar "tudo que o editor já
/// carregou" pareceria mais simples, mas faria uma dependência do jogo (NVorbis, por exemplo)
/// silenciosamente virar a versão do editor — divergência de versão que só aparece em runtime,
/// no jogo do usuário, e é péssima de diagnosticar.</para>
/// </summary>
internal sealed class GameLoadContext : AssemblyLoadContext
{
    /// <summary>
    /// O contrato entre editor e jogo. Só o que precisa ter identidade de tipo compartilhada:
    /// o runtime e as dependências que aparecem na superfície pública dele (System.Numerics
    /// vem do próprio framework, que já é compartilhado por outro caminho).
    /// </summary>
    private static readonly string[] SharedAssemblies =
    [
        "Aurora.Runtime",
        "Silk.NET.OpenGL",
        "Silk.NET.Input",
        "Silk.NET.Maths",
        "Silk.NET.Core",
    ];

    private readonly AssemblyDependencyResolver _resolver;

    public GameLoadContext(string gameAssemblyPath)
        : base(name: $"AuroraGame:{Path.GetFileNameWithoutExtension(gameAssemblyPath)}",
               isCollectible: true)
        => _resolver = new AssemblyDependencyResolver(gameAssemblyPath);

    protected override Assembly? Load(AssemblyName name)
    {
        // null aqui = "resolve pelo contexto padrão", que é exatamente o que queremos para o
        // contrato compartilhado.
        if (name.Name is { } n && SharedAssemblies.Contains(n))
            return null;

        string? path = _resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}
