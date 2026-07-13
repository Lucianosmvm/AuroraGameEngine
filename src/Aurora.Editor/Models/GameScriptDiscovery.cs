using System.Diagnostics;
using System.Text.Json;

namespace Aurora.Editor.Models;

/// <summary>
/// Descobre classes [SceneScript] do projeto de jogo sem precisar abrir o jogo: roda o
/// próprio executável com <c>--describe-scripts &lt;arquivo&gt;</c> (suportado por
/// <see cref="Aurora.Runtime.Game"/> desde que o script chame ParseArgs/Run normalmente) e
/// lê o JSON resultante. Falha silenciosa em qualquer erro — só significa que o dropdown
/// "+Add Componente" não lista scripts custom até o projeto buildar com sucesso.
/// </summary>
public static class GameScriptDiscovery
{
    public sealed record ScriptField(string Name, string Kind, string Default);
    public sealed record ScriptInfo(string Name, List<ScriptField> Fields);

    public static async Task<IReadOnlyList<ScriptInfo>> DiscoverAsync(string gameProjectPath)
    {
        var psi = BuildProcessStartInfo(gameProjectPath.Trim());
        if (psi is null)
            return [];

        string tempFile = Path.Combine(Path.GetTempPath(), $"aurora-scripts-{Guid.NewGuid():N}.json");
        psi.ArgumentList.Add("--describe-scripts");
        psi.ArgumentList.Add(tempFile);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return [];

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(cts.Token);

            if (!File.Exists(tempFile))
                return [];

            string json = await File.ReadAllTextAsync(tempFile);
            return JsonSerializer.Deserialize<List<ScriptInfo>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch
        {
            return [];
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* melhor esforço */ }
        }
    }

    private static ProcessStartInfo? BuildProcessStartInfo(string project)
    {
        var common = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (project.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(project))
                return null;
            common.FileName = project;
            return common;
        }

        common.FileName = "dotnet";
        common.ArgumentList.Add("run");
        common.ArgumentList.Add("--project");
        common.ArgumentList.Add(project);
        common.ArgumentList.Add("--");
        return common;
    }
}
