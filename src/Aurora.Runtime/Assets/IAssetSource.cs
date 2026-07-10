namespace Aurora.Runtime.Assets;

/// <summary>
/// Origem dos assets do jogo. Desktop lê do disco (<see cref="FileAssetSource"/>);
/// Android lê dos assets empacotados no APK (AndroidAssetSource, no projeto Android).
/// </summary>
public interface IAssetSource
{
    /// <summary>Abre um asset para leitura. Caminho relativo com '/' (ex.: "sprites/player.png").</summary>
    Stream Open(string path);

    bool Exists(string path);
}

/// <summary>Lê assets de uma pasta no disco. Caminho relativo é resolvido a partir do executável.</summary>
public sealed class FileAssetSource : IAssetSource
{
    private readonly string _root;

    public FileAssetSource(string root = "Assets")
    {
        // Relativo ao executável, não ao diretório de trabalho — "dotnet run" roda com
        // cwd na raiz do repositório, mas os assets são copiados para a pasta de saída.
        _root = Path.IsPathRooted(root) ? root : Path.Combine(AppContext.BaseDirectory, root);
    }

    public Stream Open(string path)
    {
        string full = Path.Combine(_root, path);
        if (!File.Exists(full))
            throw new FileNotFoundException($"Asset não encontrado: {path} (procurado em {full})", full);
        return File.OpenRead(full);
    }

    public bool Exists(string path) => File.Exists(Path.Combine(_root, path));
}
