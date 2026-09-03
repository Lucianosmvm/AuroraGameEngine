using System.Numerics;
using Aurora.Runtime;

// Apelido explícito, e não o GameState solto de Aurora.Runtime: no build Android o SDK
// injeta 'using Android.App;', que tem a própria Android.App.GameState — sem o apelido o
// mesmo arquivo compila no desktop e falha no APK com CS0104 (referência ambígua).
using GameState = Aurora.Runtime.GameState;

namespace FruitNinja;

/// <summary>Texto que sobe e some no lugar onde a fruta foi cortada ("+3", "COMBO x4").</summary>
public sealed class Aviso
{
    public string Texto = "";
    public Vector2 Posicao;
    public string Cor = "#FFFFFFFF";
    public float Escala = 1f;
    public float Idade;
    public float Duracao = 0.9f;
}

/// <summary>
/// O estado de UMA partida: pontos, vidas, nível, combo e os poderes ligados.
///
/// <para>Tudo que a tela precisa mostrar mora no <see cref="GameState"/> como variável, não
/// como campo privado: assim um <c>UiText</c> escreve <c>"{Pontos}"</c> no JSON e a HUD se
/// atualiza sozinha, e o recorde/moedas entram no save sem nenhuma linha de serialização.</para>
///
/// <para><see cref="Atual"/> é estático pelo mesmo motivo do catálogo: quem corta a fruta é um
/// <c>Behavior</c> na cena, e Behavior só enxerga o <c>World</c>. O <see cref="NinjaGame"/>
/// cria a partida e publica aqui.</para>
/// </summary>
public sealed class Partida
{
    public const string VarPontos = "Pontos";
    public const string VarVidas = "Vidas";
    public const string VarNivel = "Nivel";
    public const string VarRecorde = "Recorde";
    public const string VarMoedas = "Moedas";
    public const string VarMelhorCombo = "MelhorCombo";
    public const string VarFrutasCortadas = "Cortes";

    public const int VidasIniciais = 3;

    public static Partida? Atual { get; private set; }

    private readonly GameState _estado;
    private readonly Dictionary<EfeitoDePoder, float> _efeitos = new();

    /// <summary>Avisos flutuantes vivos. O <see cref="NinjaGame"/> desenha e o
    /// <see cref="Update"/> envelhece — a lista é pública pra não precisar de um sistema de
    /// partículas de texto só pra isso.</summary>
    public List<Aviso> Avisos { get; } = [];

    /// <summary>A lâmina desta partida. Fixada no início: trocar de arma no meio do jogo
    /// mudaria o alcance do corte no meio da conta do combo.</summary>
    public LaminaDef Lamina { get; private set; } = new();

    /// <summary>Terminou de verdade (bomba ou vidas no zero). O fluxo de telas do
    /// <see cref="NinjaGame"/> observa isto.</summary>
    public bool Acabou { get; private set; }

    /// <summary>Por que acabou — vira o texto da tela de fim.</summary>
    public string MotivoDoFim { get; private set; } = "";

    /// <summary>Segundos de partida. Só pra estatística da tela de fim.</summary>
    public float Tempo { get; private set; }

    /// <summary>Moedas que o jogador tinha antes desta partida, pra tela de fim mostrar
    /// quantas ele GANHOU e não quantas tem.</summary>
    public int MoedasNoInicio { get; private set; }

    public Partida(GameState estado) => _estado = estado;

    public int Pontos => (int)_estado.GetVariable(VarPontos);
    public int Vidas => (int)_estado.GetVariable(VarVidas);
    public int Nivel => (int)_estado.GetVariable(VarNivel, 1);
    public int Recorde => (int)_estado.GetVariable(VarRecorde);

    /// <summary>Multiplica o deltaTime de tudo que voa. É o poder Congelar: 0,35 deixa a tela
    /// inteira em câmera lenta sem mexer no relógio da engine (World.Paused é tudo ou nada).</summary>
    public float EscalaTempo => Ativo(EfeitoDePoder.Congelar) ? 0.35f : 1f;

    public bool Ativo(EfeitoDePoder efeito) => _efeitos.TryGetValue(efeito, out float t) && t > 0f;

    public float Restante(EfeitoDePoder efeito) => _efeitos.TryGetValue(efeito, out float t) ? t : 0f;

    // ------------------------------------------------------------------ ciclo

    public static Partida Iniciar(GameState estado, LaminaDef lamina)
    {
        var partida = new Partida(estado)
        {
            Lamina = lamina,
            MoedasNoInicio = (int)estado.GetVariable(VarMoedas),
        };

        estado.SetVariable(VarPontos, 0);
        estado.SetVariable(VarVidas, VidasIniciais);
        estado.SetVariable(VarNivel, 1);
        estado.SetVariable(VarMelhorCombo, 0);
        estado.SetVariable(VarFrutasCortadas, 0);

        Atual = partida;
        return partida;
    }

    public void Update(float deltaTime)
    {
        if (Acabou)
            return;

        Tempo += deltaTime;

        foreach (var efeito in _efeitos.Keys.ToList())
        {
            _efeitos[efeito] -= deltaTime;
            if (_efeitos[efeito] <= 0f)
                _efeitos.Remove(efeito);
        }

        for (int i = Avisos.Count - 1; i >= 0; i--)
        {
            Avisos[i].Idade += deltaTime;
            if (Avisos[i].Idade >= Avisos[i].Duracao)
                Avisos.RemoveAt(i);
        }
    }

    // ------------------------------------------------------------------ pontuação

    /// <summary>Conta uma fruta cortada e devolve quantos pontos ela valeu — o
    /// <see cref="Lamina"/> usa o número no aviso flutuante.</summary>
    public int Cortou(FrutaDef fruta)
    {
        if (Acabou)
            return 0;

        _estado.AddVariable(VarFrutasCortadas, 1);

        if (fruta.VidasAoCortar != 0)
            GanharVidas(fruta.VidasAoCortar);

        if (fruta.Tipo == TipoDeFruta.Poder)
        {
            Ligar(fruta);
            return 0;
        }

        int pontos = Math.Max(0, (int)MathF.Round(
            fruta.Pontos * Lamina.MultiplicadorPontos * (Ativo(EfeitoDePoder.Dobro) ? 2f : 1f)));

        Somar(pontos);
        _estado.AddVariable(VarMoedas, fruta.Moedas);
        return pontos;
    }

    /// <summary>Fecha um golpe: várias frutas no mesmo traço dão bônus, como o combo do
    /// original. Devolve o bônus (0 quando não houve combo).</summary>
    public int FecharGolpe(int frutasNoGolpe, Vector2 onde)
    {
        if (frutasNoGolpe > _estado.GetVariable(VarMelhorCombo))
            _estado.SetVariable(VarMelhorCombo, frutasNoGolpe);

        int bonus = CurvaNivel.BonusDeCombo(frutasNoGolpe);
        if (bonus <= 0)
            return 0;

        Somar(bonus);
        Anunciar($"COMBO x{frutasNoGolpe}  +{bonus}", onde, "#FFD54FFF", 1.4f, 1.3f);
        return bonus;
    }

    private void Somar(int pontos)
    {
        if (pontos == 0)
            return;

        _estado.AddVariable(VarPontos, pontos);

        int nivel = CurvaNivel.NivelDosPontos(Pontos);
        if (nivel <= Nivel)
            return;

        _estado.SetVariable(VarNivel, nivel);
        Anunciar($"NÍVEL {nivel}", new Vector2(0f, -120f), "#8AD8FFFF", 1.8f, 1.6f);
    }

    // ------------------------------------------------------------------ vidas e fim

    /// <summary>Fruta que caiu sem ser cortada. Bomba e poder não chegam aqui (a ficha diz
    /// <c>PerdeVidaSeEscapar: false</c>).</summary>
    public void Escapou(FrutaDef fruta, Vector2 onde)
    {
        if (Acabou || !fruta.PerdeVidaSeEscapar)
            return;

        GanharVidas(-1);
        Anunciar("X", new Vector2(onde.X, Arena.Base - 90f), "#E23B3BFF", 2.2f, 0.8f);

        if (Vidas <= 0)
            Terminar("Você deixou frutas demais escaparem.");
    }

    /// <summary>Cortou uma bomba. No Fruit Ninja clássico isso encerra a partida na hora, e é o
    /// que a ficha da bomba pede com <c>MataAoCortar</c>.</summary>
    public void Explodiu(FrutaDef bomba, Vector2 onde)
    {
        if (Acabou)
            return;

        Anunciar("BOOM!", onde, "#FF7043FF", 2.4f, 1.4f);

        if (bomba.MataAoCortar)
        {
            Terminar("Você cortou uma bomba.");
            return;
        }

        GanharVidas(-1);
        if (Vidas <= 0)
            Terminar("As bombas te pegaram.");
    }

    private void GanharVidas(int quantas)
        => _estado.SetVariable(VarVidas, Math.Clamp(Vidas + quantas, 0, 9));

    private void Terminar(string motivo)
    {
        Acabou = true;
        MotivoDoFim = motivo;
        _efeitos.Clear();

        if (Pontos > Recorde)
            _estado.SetVariable(VarRecorde, Pontos);
    }

    // ------------------------------------------------------------------ poderes

    private void Ligar(FrutaDef fruta)
    {
        if (fruta.Efeito == EfeitoDePoder.Nenhum)
            return;

        // Soma em vez de substituir: pegar duas bananas iguais seguidas estende o efeito, que é
        // o que o jogador espera — substituir encurtaria o poder por acertar bem.
        _efeitos[fruta.Efeito] = Restante(fruta.Efeito) + fruta.DuracaoEfeito;

        Anunciar(fruta.Efeito switch
        {
            EfeitoDePoder.Congelar => "CONGELOU!",
            EfeitoDePoder.Frenesi => "FRENESI!",
            EfeitoDePoder.Dobro => "PONTOS EM DOBRO!",
            _ => fruta.Nome,
        }, new Vector2(0f, -60f), "#B388FFFF", 1.7f, 1.4f);
    }

    // ------------------------------------------------------------------ avisos

    public void Anunciar(string texto, Vector2 onde, string cor, float escala = 1f, float duracao = 0.9f)
        => Avisos.Add(new Aviso
        {
            Texto = texto, Posicao = onde, Cor = cor, Escala = escala, Duracao = duracao,
        });
}
