using System.Text.Json;

namespace Aurora.Runtime.Saves;

/// <summary>
/// Preferências do jogador — volume, e o que mais o jogo quiser guardar.
///
/// <para>Existe separado do <see cref="GameState"/> por uma razão concreta: o GameState vive
/// DENTRO do slot de save. Volume guardado lá seria esquecido a cada jogo novo, e cada slot
/// teria o seu — o jogador baixaria o som, começaria outra partida, e o som voltaria alto. Isso
/// não é progresso de jogo, é preferência da pessoa; vale pra máquina inteira e atravessa
/// qualquer partida.</para>
///
/// <para>Fica em <c>%LocalAppData%/[GameName]/settings.json</c>, ao lado da pasta de saves.</para>
/// </summary>
public sealed class GameSettings
{
    /// <summary>Chaves que a própria engine consome — o <see cref="Game"/> repassa pro
    /// AudioManager sempre que mudam. Constantes pra não depender de digitar igual nos dois
    /// lados (uma delas escrita errada seria um slider que não faz nada e não avisa).</summary>
    public const string MasterVolume = "MasterVolume";
    public const string MusicVolume = "MusicVolume";
    public const string SfxVolume = "SfxVolume";

    private readonly Dictionary<string, float> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _texts = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;

    /// <summary>Disparado a cada mudança. É por aqui que o volume alcança o áudio.</summary>
    public event Action? Changed;

    /// <summary>Arquivo explícito de preferências.</summary>
    public GameSettings(string filePath) => _path = filePath;

    /// <summary>
    /// O arquivo padrão do jogo: <c>%LocalAppData%/[GameName]/settings.json</c>, irmão da pasta
    /// de saves. Fábrica em vez de outro construtor porque os dois receberiam uma string e
    /// ninguém adivinharia qual é caminho e qual é nome de jogo.
    /// </summary>
    public static GameSettings ForGame(string gameName = "AuroraGame")
        => new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Sanitize(gameName), "settings.json"));

    public IReadOnlyDictionary<string, float> Values => _values;
    public IReadOnlyDictionary<string, string> Texts => _texts;

    /// <summary>
    /// Valor numérico guardado. O <paramref name="fallback"/> é o que vale antes de o jogador
    /// mexer em qualquer coisa — por isso volume pede 1f, e não 0: um jogo recém-instalado que
    /// abre mudo parece quebrado.
    /// </summary>
    public float Get(string key, float fallback = 0f)
        => _values.TryGetValue(key, out float value) ? value : fallback;

    public void Set(string key, float value)
    {
        if (_values.TryGetValue(key, out float current) && current.Equals(value))
            return;

        _values[key] = value;
        Changed?.Invoke();
    }

    public bool Has(string key) => _values.ContainsKey(key);

    public string GetText(string key, string fallback = "")
        => _texts.TryGetValue(key, out string? value) ? value : fallback;

    public void SetText(string key, string value)
    {
        if (_texts.TryGetValue(key, out string? current) && current == value)
            return;

        _texts[key] = value ?? "";
        Changed?.Invoke();
    }

    /// <summary>Volta tudo ao padrão (botão "restaurar" de menu de opções).</summary>
    public void Clear()
    {
        if (_values.Count == 0 && _texts.Count == 0)
            return;

        _values.Clear();
        _texts.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// Lê do disco. Arquivo ausente ou corrompido não é erro: volta tudo ao padrão e segue —
    /// derrubar o jogo porque o arquivo de preferências quebrou seria trocar um som alto demais
    /// por uma tela que não abre.
    /// </summary>
    public void Load()
    {
        if (!File.Exists(_path))
            return;

        try
        {
            var dto = JsonSerializer.Deserialize<SettingsDto>(File.ReadAllText(_path));
            if (dto is null)
                return;

            _values.Clear();
            _texts.Clear();
            if (dto.Values is not null)
                foreach (var (k, v) in dto.Values) _values[k] = v;
            if (dto.Texts is not null)
                foreach (var (k, v) in dto.Texts) _texts[k] = v;

            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GameSettings] '{_path}' ilegível ({ex.Message}) — usando os padrões.");
        }
    }

    /// <summary>
    /// Grava no disco. Falha (disco cheio, pasta sem permissão) é logada e engolida: perder a
    /// preferência é irritante, fechar o jogo por causa dela é pior.
    /// </summary>
    public void Save()
    {
        try
        {
            string? dir = System.IO.Path.GetDirectoryName(_path);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(
                new SettingsDto(new(_values), new(_texts)),
                new JsonSerializerOptions { WriteIndented = true });

            // Mesma escrita atômica dos saves: uma queda no meio da gravação não pode deixar o
            // arquivo pela metade e o jogo abrindo com preferências corrompidas na próxima vez.
            string temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            try
            {
                File.Move(temp, _path, overwrite: true);
            }
            catch
            {
                try { File.Delete(temp); } catch { }
                throw;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GameSettings] não consegui gravar '{_path}': {ex.Message}");
        }
    }

    private static string Sanitize(string name)
        => string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private sealed record SettingsDto(
        Dictionary<string, float>? Values,
        Dictionary<string, string>? Texts);
}
