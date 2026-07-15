using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aurora.Editor.Models;

/// <summary>
/// Configurações do projeto do jogo: onde fica o executável/.csproj e qual cena iniciar.
/// Armazenadas em aurora.project.json; o editor busca subindo a partir da pasta da cena.
/// </summary>
public sealed class ProjectSettings
{
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };
    private const string FileName = "aurora.project.json";

    /// <summary>Caminho para o .csproj, diretório do projeto ou .exe compilado.</summary>
    [JsonPropertyName("gameProject")]
    public string? GameProject { get; set; }

    /// <summary>Caminho da última cena aberta, relativo à pasta do projeto — usado por
    /// "Abrir Projeto…" pra reabrir de onde parou, estilo Unity.</summary>
    [JsonPropertyName("lastScene")]
    public string? LastScene { get; set; }

    /// <summary>Caminho absoluto do arquivo aurora.project.json em disco.</summary>
    [JsonIgnore]
    public string FilePath { get; private set; } = "";

    /// <summary>
    /// Sobe a partir da pasta da cena procurando aurora.project.json.
    /// Se não achar, retorna uma instância vazia apontando para a pasta da cena.
    /// </summary>
    public static ProjectSettings Find(string scenePath)
    {
        string sceneDir = Path.GetDirectoryName(Path.GetFullPath(scenePath))!;

        for (var dir = new DirectoryInfo(sceneDir); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, FileName);
            if (!File.Exists(candidate))
                continue;

            try
            {
                var loaded = JsonSerializer.Deserialize<ProjectSettings>(File.ReadAllText(candidate)) ?? new();
                loaded.FilePath = candidate;
                return loaded;
            }
            catch { /* arquivo corrompido — ignora e continua subindo */ }
        }

        return new ProjectSettings { FilePath = Path.Combine(sceneDir, FileName) };
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(FilePath))
            throw new InvalidOperationException("FilePath não definido.");

        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, _opts));
    }
}
