using System.Diagnostics;
using System.Text.Json;

namespace Aurora.Editor.Models;

/// <summary>
/// Descobre classes [SceneScript] do projeto de jogo sem precisar abrir o jogo: roda o
/// próprio executável com <c>--describe-scripts &lt;arquivo&gt;</c> (suportado por
/// <see cref="Aurora.Runtime.Game"/> desde que o script chame ParseArgs/Run normalmente) e
/// lê o JSON resultante. Erro de build (ou qualquer outra falha) vai em
/// <see cref="DiscoveryResult.Error"/> em vez de sumir — quem chama decide o que fazer
/// (ex.: manter o catálogo antigo e avisar na status bar).
/// </summary>
public static class GameScriptDiscovery
{
    public sealed record ScriptField(string Name, string Kind, string Default);
    public sealed record ScriptInfo(string Name, List<ScriptField> Fields);

    /// <summary><paramref name="Detail"/> é o stdout+stderr completo do dotnet — mostrado como
    /// tooltip na status bar já que os processos rodam sem console (CreateNoWindow=true).</summary>
    public sealed record DiscoveryResult(IReadOnlyList<ScriptInfo> Scripts, string? Error, string? Detail = null);

    public static async Task<DiscoveryResult> DiscoverAsync(string gameProjectPath)
    {
        var psi = BuildProcessStartInfo(gameProjectPath.Trim());
        if (psi is null)
            return new DiscoveryResult([], $"caminho de projeto inválido: '{gameProjectPath}'.");

        string tempFile = Path.Combine(Path.GetTempPath(), $"aurora-scripts-{Guid.NewGuid():N}.json");
        psi.ArgumentList.Add("--describe-scripts");
        psi.ArgumentList.Add(tempFile);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return new DiscoveryResult([], "não consegui iniciar o processo do jogo.");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string stdout, stderr;
            try
            {
                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
                Task<string> stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token);
                stdout = await stdoutTask;
                stderr = await stderrTask;
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { /* já pode ter saído */ }
                return new DiscoveryResult([], "tempo esgotado (30s) — verifique se o projeto builda (dotnet build) e não trava esperando entrada.");
            }

            if (!File.Exists(tempFile))
            {
                string summary = FirstErrorLine(stdout, stderr);
                string reason = process.ExitCode != 0
                    ? $"projeto não compilou (código {process.ExitCode}): {summary}"
                    : $"não gerou a lista de scripts: {summary}";
                return new DiscoveryResult([], reason, CombineLog(stdout, stderr));
            }

            string json = await File.ReadAllTextAsync(tempFile);
            var scripts = JsonSerializer.Deserialize<List<ScriptInfo>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            return new DiscoveryResult(scripts, null);
        }
        catch (Exception ex)
        {
            return new DiscoveryResult([], ex.Message, ex.ToString());
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* melhor esforço */ }
        }
    }

    /// <summary>Primeira linha com "error" em stderr/stdout de um processo dotnet — usado
    /// pra dar uma pista curta na status bar sem despejar o log inteiro.</summary>
    internal static string FirstErrorLine(string stdout, string stderr)
    {
        string combined = stderr.Length > 0 ? stderr : stdout;
        string? line = combined.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase));
        return line ?? "veja o detalhe (passe o mouse na status bar).";
    }

    /// <summary>Resumo curto de um processo de JOGO que morreu — o formato é diferente do
    /// dotnet build: uma exceção sem handler sai como "Unhandled exception. System.X: msg" e
    /// não contém a palavra "error", então <see cref="FirstErrorLine"/> devolveria só o texto
    /// de fallback. Sem isto, um crash de runtime (asset faltando, cena inválida) aparecia
    /// pro usuário como "abriu e fechou sozinho" e nada mais.</summary>
    internal static string FirstCrashLine(string stdout, string stderr)
    {
        string combined = stderr.Trim().Length > 0 ? stderr : stdout;
        var lines = combined.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        int unhandled = lines.FindIndex(l => l.StartsWith("Unhandled exception", StringComparison.OrdinalIgnoreCase));
        if (unhandled >= 0)
        {
            // "Unhandled exception. System.IO.FileNotFoundException: msg" (tudo numa linha) ou
            // "Unhandled exception." com o tipo/mensagem na linha seguinte — os dois acontecem.
            string tail = lines[unhandled];
            int dot = tail.IndexOf('.');
            tail = dot >= 0 && dot + 1 < tail.Length ? tail[(dot + 1)..].Trim() : "";
            if (tail.Length > 0)
                return tail;
            if (unhandled + 1 < lines.Count)
                return lines[unhandled + 1];
        }

        return lines.FirstOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase))
            ?? lines.FirstOrDefault()
            ?? "sem saída no stdout/stderr (passe o mouse na status bar).";
    }

    /// <summary>Junta stdout+stderr num texto só, pra tooltip da status bar — não existe
    /// console nenhum pra apontar o usuário (esses processos rodam com CreateNoWindow=true).</summary>
    internal static string CombineLog(string stdout, string stderr)
    {
        var parts = new List<string>();
        if (stderr.Trim().Length > 0) parts.Add($"stderr:\n{stderr.Trim()}");
        if (stdout.Trim().Length > 0) parts.Add($"stdout:\n{stdout.Trim()}");
        return parts.Count > 0 ? string.Join("\n\n", parts) : "(sem saída)";
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
