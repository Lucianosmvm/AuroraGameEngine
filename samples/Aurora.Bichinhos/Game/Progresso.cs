using System.Text.Json;

namespace Bichinhos;

/// <summary>
/// Tudo que sobrevive a fechar o jogo: o bicho, as moedas e QUANDO o jogo foi fechado.
///
/// <para>Por que um arquivo próprio e não o <c>GameState</c> da engine: o GameState guarda float,
/// e a hora de fechar (segundos desde 1970, ~1,8 bilhão) perde minutos de precisão num float. É
/// essa hora que faz o bicho sentir fome enquanto o celular está no bolso.</para>
/// </summary>
public sealed class Progresso
{
    public Bicho? Bicho { get; set; }
    public int Moedas { get; set; } = 20;
    public DateTime UltimaVezUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Teto do tempo fora que conta: voltar depois de um mês não pode achar o bicho
    /// morto de fome sem chance de reagir. Um dia inteiro já deixa ele bem mal.</summary>
    public const double MaxHorasFora = 24.0;

    public const string Arquivo = "bichinho.json";

    private static readonly JsonSerializerOptions Opcoes = new() { WriteIndented = true };

    public static Progresso Carregar(string pasta)
    {
        string caminho = Path.Combine(pasta, Arquivo);
        if (!File.Exists(caminho))
            return new Progresso();

        try
        {
            return JsonSerializer.Deserialize<Progresso>(File.ReadAllText(caminho), Opcoes) ?? new Progresso();
        }
        catch (Exception ex)
        {
            // Save corrompido não pode travar o jogo no boot: começa de novo e avisa.
            Console.Error.WriteLine($"[Bichinhos] Save ilegível, começando do zero: {ex.Message}");
            return new Progresso();
        }
    }

    public void Salvar(string pasta)
    {
        UltimaVezUtc = DateTime.UtcNow;
        Directory.CreateDirectory(pasta);

        // Grava num temporário e troca: se o Android matar o app no meio da escrita, o save antigo
        // continua inteiro em vez de virar meio JSON.
        string caminho = Path.Combine(pasta, Arquivo);
        string temp = caminho + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Opcoes));
        File.Move(temp, caminho, overwrite: true);
    }

    /// <summary>Horas que se passaram desde o último save, já limitadas.</summary>
    public double HorasDesdeUltimaVez(DateTime agoraUtc)
        => Math.Clamp((agoraUtc - UltimaVezUtc).TotalHours, 0.0, MaxHorasFora);
}
