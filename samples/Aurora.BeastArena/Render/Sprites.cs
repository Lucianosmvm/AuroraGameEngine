using Aurora.Runtime.Assets;
using Aurora.Runtime.Graphics;
using BeastArena.Sim;
using Silk.NET.OpenGL;

namespace BeastArena.Render;

/// <summary>
/// Os PNGs de <c>Assets/sprites/</c> (gerados por <c>Arte/gerar_sprites.py</c>), achados pelo id
/// da carta:
///
/// <list type="bullet">
/// <item><c>criaturas/&lt;id&gt;_&lt;estágio&gt;.png</c> — uma forma por estágio de evolução</item>
/// <item><c>cartas/&lt;id&gt;.png</c> — ícone de feitiço (criatura usa o estágio 0)</item>
/// <item><c>efeitos/&lt;id&gt;.png</c> — área do feitiço; <c>efeitos/&lt;id&gt;_projetil.png</c> — tiro</item>
/// </list>
///
/// <para>Carta sem arte devolve <c>null</c> e a tela cai no disco com letra de antes: dá pra
/// acrescentar carta no JSON e jogar antes de desenhar o bicho.</para>
/// </summary>
public sealed class Sprites
{
    private readonly GL _gl;
    private readonly AssetManager _assets;
    private readonly Dictionary<string, Texture2D?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Texture2D Ninho { get; }
    public Texture2D Ovo { get; }
    public Texture2D Santuario { get; }
    public Texture2D Pilar { get; }
    public IReadOnlyList<Texture2D> Pedras { get; }
    public IReadOnlyList<Texture2D> Tufos { get; }
    public IReadOnlyList<Texture2D> Flores { get; }

    public Sprites(GL gl, AssetManager assets)
    {
        _gl = gl;
        _assets = assets;

        Ninho = Obrigatoria("arena/ninho");
        Ovo = Obrigatoria("arena/ovo");
        Santuario = Obrigatoria("arena/santuario");
        Pilar = Obrigatoria("arena/pilar");
        Pedras = [Obrigatoria("arena/pedra_0"), Obrigatoria("arena/pedra_1"), Obrigatoria("arena/pedra_2")];
        Tufos = [Obrigatoria("arena/tufo_0"), Obrigatoria("arena/tufo_1")];
        Flores = [Obrigatoria("arena/flor_0"), Obrigatoria("arena/flor_1")];
    }

    /// <summary>Forma do estágio; se faltar a arte daquele estágio, a maior que existir abaixo.</summary>
    public Texture2D? Criatura(CartaDef carta, int estagio)
    {
        for (int e = estagio; e >= 0; e--)
        {
            if (Opcional($"criaturas/{carta.Id}_{e}") is { } textura)
                return textura;
        }

        return null;
    }

    public Texture2D? Icone(CartaDef carta)
        => carta.Tipo == TipoDeCarta.Feitico ? Opcional($"cartas/{carta.Id}") : Criatura(carta, 0);

    public Texture2D? AreaDoFeitico(CartaDef carta) => Opcional($"efeitos/{carta.Id}");

    public Texture2D? Projetil(CartaDef carta) => Opcional($"efeitos/{carta.Id}_projetil");

    /// <summary>
    /// Quanto o lado do sprite mede em relação ao DIÂMETRO visual da criatura. O corpo de colisão
    /// é um disco; o desenho é um bicho de perfil com cauda, chifre e asa — precisa passar do disco
    /// pra ter o mesmo "peso" visual. Vespa ocupa menos da própria tela, então cresce mais.
    /// </summary>
    public static float FatorDaCriatura(CartaDef carta) => carta.Id switch
    {
        "vespas" => 2.1f,
        "rinoceronte" => 1.6f,
        _ => 1.85f,
    };

    private Texture2D Obrigatoria(string nome)
        => Opcional(nome) ?? throw new FileNotFoundException($"Sprite obrigatório faltando: sprites/{nome}.png — rode Arte/gerar_sprites.py.");

    private Texture2D? Opcional(string nome)
    {
        if (_cache.TryGetValue(nome, out var cacheada))
            return cacheada;

        string caminho = $"sprites/{nome}.png";
        Texture2D? textura = null;
        if (_assets.Exists(caminho))
        {
            textura = _assets.LoadTexture(caminho);
            Suavizar(textura);
        }

        _cache[nome] = textura;
        return textura;
    }

    /// <summary>
    /// A engine cria toda textura com filtro Nearest (pixel art). Estes sprites são ilustração
    /// em 256 px desenhada entre 40 e 150 px: com Nearest, bicho andando cintila. Linear + mipmap
    /// deixa a redução limpa; o PNG já vem com a cor "sangrada" pra borda não escurecer.
    /// </summary>
    private void Suavizar(Texture2D textura)
    {
        _gl.BindTexture(TextureTarget.Texture2D, textura.Handle);
        _gl.GenerateMipmap(TextureTarget.Texture2D);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }
}
