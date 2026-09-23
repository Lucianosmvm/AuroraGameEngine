using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bichinhos;

public enum Tipo { Normal, Fogo, Agua, Planta }

public enum EfeitoGolpe { Nenhum, Curar, SubirAtaque, SubirDefesa, BaixarAtaque }

public sealed class Atributos
{
    public int Vida { get; set; }
    public int Ataque { get; set; }
    public int Defesa { get; set; }
    public int Velocidade { get; set; }
}

public sealed class Estagio
{
    public string Nome { get; set; } = "";

    /// <summary>[x, y, separação, raio] na tela 0..100 do sprite: onde a pálpebra fecha.</summary>
    public float[] Olhos { get; set; } = [50, 50, 9, 5];

    public string Pele { get; set; } = "#FFFFFFFF";
}

public sealed class GolpeAprendido
{
    public string Golpe { get; set; } = "";
    public int Nivel { get; set; }
}

public sealed class Especie
{
    public string Id { get; set; } = "";
    public Tipo Tipo { get; set; }
    public bool Inicial { get; set; }
    public string Descricao { get; set; } = "";
    public Atributos Base { get; set; } = new();
    public List<Estagio> Estagios { get; set; } = [];
    public List<GolpeAprendido> Golpes { get; set; } = [];

    public string Sprite(int estagio) => $"sprites/bichos/{Id}_{Math.Clamp(estagio, 0, 2)}.png";
}

public sealed class Golpe
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public Tipo Tipo { get; set; }
    public int Poder { get; set; }
    public int Precisao { get; set; } = 100;
    public EfeitoGolpe Efeito { get; set; }
}

/// <summary>
/// Espécies e golpes, lidos de <c>database/especies.json</c>. Balancear o jogo é mexer no JSON:
/// nível de evolução, atributos, quem aprende o quê.
/// </summary>
public sealed class Catalogo
{
    public const string Caminho = "database/especies.json";

    public static Catalogo Atual { get; set; } = new();

    public int[] NivelEvolucao { get; set; } = [6, 14];
    public float[] MultiplicadorEstagio { get; set; } = [1f, 1.25f, 1.55f];
    public int NivelMaximo { get; set; } = 30;
    public List<Especie> Especies { get; set; } = [];
    public List<Golpe> Golpes { get; set; } = [];

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Catalogo Carregar(string json)
        => JsonSerializer.Deserialize<Catalogo>(json, Opcoes) ?? throw new InvalidDataException("especies.json vazio.");

    public IEnumerable<Especie> Iniciais => Especies.Where(e => e.Inicial);

    public Especie Especie(string id)
        => Especies.FirstOrDefault(e => e.Id == id) ?? throw new KeyNotFoundException($"Espécie '{id}' não existe no catálogo.");

    public Golpe Golpe(string id)
        => Golpes.FirstOrDefault(g => g.Id == id) ?? throw new KeyNotFoundException($"Golpe '{id}' não existe no catálogo.");

    /// <summary>Em que estágio um bicho desse nível deveria estar.</summary>
    public int EstagioPorNivel(int nivel)
    {
        int estagio = 0;
        foreach (int limiar in NivelEvolucao)
        {
            if (nivel >= limiar)
                estagio++;
        }
        return Math.Min(estagio, 2);
    }

    /// <summary>Os golpes que o bicho usa: os quatro últimos que aprendeu até esse nível.</summary>
    public List<Golpe> GolpesNoNivel(Especie especie, int nivel)
        => especie.Golpes
            .Where(g => g.Nivel <= nivel)
            .OrderBy(g => g.Nivel)
            .TakeLast(4)
            .Select(g => Golpe(g.Golpe))
            .ToList();

    /// <summary>Atributos de batalha. A conta é a do Pokémon simplificada: o nível escala tudo
    /// linearmente, a evolução multiplica a base.</summary>
    public Atributos AtributosNoNivel(Especie especie, int nivel, int estagio)
    {
        float mult = MultiplicadorEstagio[Math.Clamp(estagio, 0, MultiplicadorEstagio.Length - 1)];

        int Stat(int b) => (int)(b * mult * 2f * nivel / 50f) + 5;

        return new Atributos
        {
            Vida = (int)(especie.Base.Vida * mult * 2f * nivel / 50f) + nivel + 10,
            Ataque = Stat(especie.Base.Ataque),
            Defesa = Stat(especie.Base.Defesa),
            Velocidade = Stat(especie.Base.Velocidade),
        };
    }
}

public static class Tipos
{
    /// <summary>Fogo queima Planta, Planta bebe Água, Água apaga Fogo. Normal é neutro com todos.</summary>
    public static float Efetividade(Tipo ataque, Tipo defensor)
    {
        if (ataque == Tipo.Normal || defensor == Tipo.Normal)
            return 1f;
        if (ataque == defensor)
            return 0.5f;

        return (ataque, defensor) switch
        {
            (Tipo.Fogo, Tipo.Planta) or (Tipo.Planta, Tipo.Agua) or (Tipo.Agua, Tipo.Fogo) => 2f,
            _ => 0.5f,
        };
    }

    public static string Nome(Tipo tipo) => tipo switch
    {
        Tipo.Fogo => "Fogo",
        Tipo.Agua => "Água",
        Tipo.Planta => "Planta",
        _ => "Normal",
    };

    public static string Cor(Tipo tipo) => tipo switch
    {
        Tipo.Fogo => "#F4702AFF",
        Tipo.Agua => "#3F95E0FF",
        Tipo.Planta => "#4FA83FFF",
        _ => "#9A8F84FF",
    };
}
