using System.Diagnostics;
using System.Reflection;
using Aurora.Runtime;

namespace Aurora.Editor.Models;

/// <summary>Resultado de uma tentativa de build ou load, com log pra mostrar no editor.</summary>
/// <param name="Ok">Se deu certo.</param>
/// <param name="Message">Uma linha pro status bar.</param>
/// <param name="Detail">Log completo, pro painel de detalhe — vazio quando deu certo.</param>
internal readonly record struct HostResult(bool Ok, string Message, string Detail = "")
{
    public static HostResult Success(string message) => new(true, message);
    public static HostResult Failure(string message, string detail = "") => new(false, message, detail);
}

/// <summary>
/// Compila o projeto do jogo, carrega o assembly resultante num contexto colecionável e mantém
/// viva a instância de <see cref="Game"/> — sem saber nada de OpenGL. Quem é dono do contexto
/// gráfico e do loop de frame é o controle do viewport, que dirige o Game pelos passos públicos
/// (Initialize/Tick/RenderFrame).
///
/// <para>Descarregar de verdade é a razão de tudo isto existir: enquanto o assembly do jogo
/// estiver vivo, recompilar não troca o código que está rodando. Ver <see cref="Stop"/>.</para>
/// </summary>
internal sealed class GameHost : IDisposable
{
    private GameLoadContext? _context;
    private WeakReference? _contextWatch;

    /// <summary>A instância do jogo, ou null se nada está carregado.</summary>
    public Game? Game { get; private set; }

    public bool IsLoaded => Game is not null;

    /// <summary>
    /// Compila o projeto do jogo e devolve o caminho do assembly gerado.
    ///
    /// <para><c>--getProperty:TargetPath</c> compila e responde onde o .dll saiu na mesma
    /// chamada. Montar o caminho na mão (bin/Debug/net10.0/…) quebraria em projeto com
    /// Configuration, TargetFramework ou AssemblyName diferentes do padrão.</para>
    /// </summary>
    public static async Task<(HostResult result, string? assemblyPath)> BuildAsync(
        string projectPath, CancellationToken cancellation = default)
    {
        if (!File.Exists(projectPath))
            return (HostResult.Failure($"Projeto do jogo não encontrado: {projectPath}"), null);

        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add(projectPath);

        // -t:Build explícito NÃO é redundante com "dotnet build": sozinho, --getProperty faz o
        // MSBuild parar depois da avaliação e só imprimir o valor — ele responde onde o .dll
        // SAIRIA e não compila nada. O sintoma é cruel: caminho correto na saída, arquivo
        // inexistente no disco (ou, pior, o .dll velho de um build anterior, e o editor roda
        // código antigo achando que recompilou).
        psi.ArgumentList.Add("-t:Build");
        psi.ArgumentList.Add("--getProperty:TargetPath");

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("dotnet build não iniciou.");

            string stdout = await process.StandardOutput.ReadToEndAsync(cancellation);
            string stderr = await process.StandardError.ReadToEndAsync(cancellation);
            await process.WaitForExitAsync(cancellation);

            if (process.ExitCode != 0)
                return (HostResult.Failure(
                    $"Build falhou (código {process.ExitCode}): {FirstErrorLine(stdout, stderr)}",
                    Combine(stdout, stderr)), null);

            // Com --getProperty a saída é só o valor; erro de build já caiu no ExitCode acima.
            string path = stdout.Trim();
            if (path.Length == 0 || !File.Exists(path))
                return (HostResult.Failure(
                    "Build passou mas não achei o assembly gerado.", Combine(stdout, stderr)), null);

            return (HostResult.Success($"Build OK — {Path.GetFileName(path)}"), path);
        }
        catch (Exception ex)
        {
            return (HostResult.Failure($"Erro ao compilar: {ex.Message}", ex.ToString()), null);
        }
    }

    /// <summary>
    /// Carrega o assembly do jogo e instancia a subclasse de <see cref="Game"/> que houver nele.
    /// O jogo ainda NÃO está inicializado ao voltar daqui — falta o contexto de GL, que só o
    /// viewport tem.
    /// </summary>
    public HostResult Load(string assemblyPath)
    {
        if (IsLoaded)
            return HostResult.Failure("Já existe um jogo carregado — pare antes de carregar de novo.");

        try
        {
            _context = new GameLoadContext(assemblyPath);
            _contextWatch = new WeakReference(_context);

            // De bytes, não do caminho: LoadFromAssemblyPath tranca o arquivo no Windows, e o
            // próximo build do usuário falharia com "arquivo em uso" mesmo depois do Unload.
            using var bytes = new MemoryStream(File.ReadAllBytes(assemblyPath));
            var assembly = _context.LoadFromStream(bytes);

            var gameTypes = assembly.GetTypes()
                .Where(t => typeof(Game).IsAssignableFrom(t) && t is { IsAbstract: false })
                .ToArray();

            if (gameTypes.Length == 0)
                return Abort($"Nenhuma subclasse de Game em {Path.GetFileName(assemblyPath)}.");

            if (gameTypes.Length > 1)
                return Abort("Mais de uma subclasse de Game: "
                    + string.Join(", ", gameTypes.Select(t => t.Name)));

            if (Activator.CreateInstance(gameTypes[0]) is not Game game)
                return Abort($"{gameTypes[0].Name} não pôde ser instanciada como Game.");

            Game = game;
            return HostResult.Success($"Carregado {gameTypes[0].Name}");
        }
        catch (ReflectionTypeLoadException ex)
        {
            return Abort("Falha ao ler os tipos do jogo.",
                string.Join(Environment.NewLine, ex.LoaderExceptions.Select(e => e?.Message)));
        }
        catch (Exception ex)
        {
            return Abort($"Falha ao carregar o jogo: {ex.Message}", ex.ToString());
        }
    }

    /// <summary>Desfaz um Load que não completou, pra não deixar contexto órfão segurando memória.</summary>
    private HostResult Abort(string message, string detail = "")
    {
        _context?.Unload();
        _context = null;
        _contextWatch = null;
        return HostResult.Failure(message, detail);
    }

    /// <summary>
    /// Descarrega o jogo. <paramref name="shutdownGame"/> deve ser false se o contexto de GL já
    /// morreu — <see cref="Game.Shutdown"/> libera recursos de GL e precisa do contexto vivo.
    ///
    /// <para>Solta a referência ao Game ANTES do Unload: enquanto o editor segurar qualquer
    /// objeto de tipo do jogo, o contexto fica preso e o código novo nunca substitui o antigo.
    /// É por isso que nada aqui devolve tipos do jogo pro resto do editor.</para>
    /// </summary>
    public void Stop(bool shutdownGame = true)
    {
        if (shutdownGame && Game is { } game)
        {
            try
            {
                game.Shutdown();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[GameHost] Shutdown do jogo falhou: {ex.Message}");
            }
        }

        Game = null;
        _context?.Unload();
        _context = null;
    }

    /// <summary>
    /// Se o contexto do último <see cref="Stop"/> realmente foi coletado. Falso depois de um GC
    /// completo significa vazamento: alguém no editor ainda segura um objeto do jogo, e o
    /// próximo Play vai rodar o código velho. Serve pra diagnóstico e pros testes.
    /// </summary>
    public bool LastContextCollected
    {
        get
        {
            if (_contextWatch is null)
                return true;

            for (int i = 0; i < 10 && _contextWatch.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            return !_contextWatch.IsAlive;
        }
    }

    public void Dispose() => Stop();

    private static string FirstErrorLine(string stdout, string stderr)
    {
        foreach (string line in Combine(stdout, stderr).Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Contains(": error ", StringComparison.OrdinalIgnoreCase))
                return trimmed;
        }

        return "veja o log completo.";
    }

    private static string Combine(string stdout, string stderr)
        => string.Join(Environment.NewLine,
            new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
