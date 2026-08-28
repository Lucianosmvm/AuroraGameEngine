using Aurora.Runtime;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Variáveis de texto do <see cref="GameState"/> — nome do jogador, resposta digitada. O caso
/// que motivou: pedir o nome numa tela e usar depois, sem escrever código.
/// </summary>
public sealed class TextVariableTests
{
    [Fact]
    public void GuardaELeTexto()
    {
        var state = new GameState();

        Assert.Equal("", state.GetText("Nome"));
        Assert.False(state.HasText("Nome"));

        state.SetText("Nome", "Ana");

        Assert.Equal("Ana", state.GetText("Nome"));
        Assert.True(state.HasText("Nome"));
    }

    /// <summary>Nomes de variável já ignoram maiúscula nos números e switches; texto seguindo
    /// outra regra seria uma pegadinha silenciosa.</summary>
    [Fact]
    public void NomeDaVariavelIgnoraMaiuscula()
    {
        var state = new GameState();
        state.SetText("NomeJogador", "Ana");

        Assert.Equal("Ana", state.GetText("nomejogador"));
    }

    /// <summary>
    /// Campo apagado guarda "" em vez de sumir. A diferença importa: chave ausente faria o
    /// {Token} do UiText cair no lado numérico e desenhar "0" onde devia aparecer nada.
    /// </summary>
    [Fact]
    public void TextoVazioContinuaExistindo()
    {
        var state = new GameState();
        state.SetText("Nome", "Ana");
        state.SetText("Nome", "");

        Assert.True(state.HasText("Nome"));
        Assert.Equal("", state.GetText("Nome"));
    }

    [Fact]
    public void TextoNaoSeMisturaComNumero()
    {
        var state = new GameState();
        state.SetVariable("Ouro", 50f);
        state.SetText("Nome", "Ana");

        Assert.Equal(0f, state.GetVariable("Nome"));
        Assert.Equal("", state.GetText("Ouro"));
    }

    [Fact]
    public void SobreviveAoJson()
    {
        var state = new GameState();
        state.SetText("Nome", "Ana");
        state.SetVariable("Ouro", 50f);
        state.SetSwitch("Porta", true);

        var carregado = new GameState();
        carregado.LoadJson(state.ToJson());

        Assert.Equal("Ana", carregado.GetText("Nome"));
        Assert.Equal(50f, carregado.GetVariable("Ouro"));
        Assert.True(carregado.GetSwitch("Porta"));
    }

    /// <summary>Save gravado antes desta feature não tem a seção de textos — tem que carregar
    /// vazio, não estourar.</summary>
    [Fact]
    public void JsonAntigoSemTextos_CarregaSemQuebrar()
    {
        const string antigo = """
            { "Variables": { "Ouro": 50 }, "Switches": { "Porta": true } }
            """;

        var state = new GameState();
        state.LoadJson(antigo);

        Assert.Equal(50f, state.GetVariable("Ouro"));
        Assert.True(state.GetSwitch("Porta"));
        Assert.Equal("", state.GetText("Nome"));
    }

    [Fact]
    public void ClearApagaTextos()
    {
        var state = new GameState();
        state.SetText("Nome", "Ana");

        state.Clear();

        Assert.False(state.HasText("Nome"));
    }

    /// <summary>Carregar substitui, não mistura: o nome da partida anterior não pode vazar pro
    /// save recém-carregado.</summary>
    [Fact]
    public void CarregarSubstituiOsTextosAnteriores()
    {
        var state = new GameState();
        state.SetText("Nome", "Ana");
        state.SetText("Cidade", "Vila");

        var outro = new GameState();
        outro.SetText("Nome", "Beto");
        state.LoadJson(outro.ToJson());

        Assert.Equal("Beto", state.GetText("Nome"));
        Assert.False(state.HasText("Cidade"));
    }

    [Fact]
    public void MudancaDeTextoAvisaOsOuvintes()
    {
        var state = new GameState();
        int avisos = 0;
        state.Changed += () => avisos++;

        state.SetText("Nome", "Ana");

        Assert.Equal(1, avisos);
    }
}
