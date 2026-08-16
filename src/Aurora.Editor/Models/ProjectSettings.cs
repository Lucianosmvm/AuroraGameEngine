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

    /// <summary>Orientação de tela do APK gerado por "Exportar Android…": Landscape/Portrait
    /// (fixo, sem girar) ou SensorLandscape/SensorPortrait/Sensor (gira com o aparelho — ver
    /// AndroidExporter pro histórico de compatibilidade). Null = Landscape (padrão de sempre).</summary>
    [JsonPropertyName("androidOrientation")]
    public string? AndroidOrientation { get; set; }

    /// <summary>Resolução de referência da UI (null = 1280x720). O editor resolve AnchorX/AnchorY
    /// contra ela — não contra o tamanho do painel do viewport, que muda com a janela do editor —
    /// e o jogo gerado passa a mesma resolução pro <c>Game.DesignResolution</c>. Sem os dois lados
    /// usando o MESMO número, um elemento ancorado em Center cai num lugar no preview e em outro
    /// no jogo (era exatamente esse o descasamento do editor até aqui).</summary>
    [JsonPropertyName("designWidth")]
    public int? DesignWidth { get; set; }

    /// <summary>Altura de referência da UI — ver <see cref="DesignWidth"/>.</summary>
    [JsonPropertyName("designHeight")]
    public int? DesignHeight { get; set; }

    /// <summary>Largura de referência efetiva: <see cref="DesignWidth"/> ou o padrão 1280.</summary>
    [JsonIgnore]
    public int EffectiveDesignWidth => DesignWidth is > 0 ? DesignWidth.Value : 1280;

    /// <summary>Altura de referência efetiva: <see cref="DesignHeight"/> ou o padrão 720.</summary>
    [JsonIgnore]
    public int EffectiveDesignHeight => DesignHeight is > 0 ? DesignHeight.Value : 720;

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
