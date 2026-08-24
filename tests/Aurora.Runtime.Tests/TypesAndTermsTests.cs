using Aurora.Runtime.Database;
using Aurora.Runtime.UI;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Listas de categorias (Types) e textos de interface (Terms). Os dois existem pra tirar decisão
/// do código: qual categoria de item existe, e que palavra a engine escreve na tela. Os dois são
/// opcionais — sem arquivo, o campo aceita qualquer texto e a palavra é a padrão.
/// </summary>
public class TypesAndTermsTests
{
    // ---------- Types ----------

    private static TypeDatabase Types(string json)
    {
        var database = new TypeDatabase();
        database.Load(json);
        return database;
    }

    [Fact]
    public void ListaCadastradaDevolveOsValoresNaOrdem()
    {
        var types = Types("""
        { "Types": [ { "Id": "ItemTypes", "Values": ["Consumivel", "Arma", "Chave"] } ] }
        """);

        Assert.Equal(["Consumivel", "Arma", "Chave"], types.Get("ItemTypes"));
    }

    [Fact]
    public void ContainsIgnoraMaiusculasEAceitaVazio()
    {
        var types = Types("""
        { "Types": [ { "Id": "ItemTypes", "Values": ["Consumivel"] } ] }
        """);

        Assert.True(types.Contains("ItemTypes", "consumivel"));
        Assert.True(types.Contains("ItemTypes", ""));
        Assert.False(types.Contains("ItemTypes", "Consumível"));
    }

    [Fact]
    public void ListaInexistenteOuVaziaAceitaQualquerCoisa()
    {
        // "Não cadastrei" significa "não quero controle aqui" — e não "nada vale".
        var types = Types("""{ "Types": [ { "Id": "ItemTypes", "Values": [] } ] }""");

        Assert.True(types.Contains("ItemTypes", "QualquerCoisa"));
        Assert.True(types.Contains("ListaQueNaoExiste", "QualquerCoisa"));
        Assert.Empty(types.Get("ListaQueNaoExiste"));
    }

    [Fact]
    public void ListaDoJogoConviveComADaEngine()
    {
        // Types não é só pra item: um jogo de corrida cadastra o que quiser e lê pelo id.
        var types = Types("""
        {
          "Types": [
            { "Id": "ItemTypes", "Values": ["Peca"] },
            { "Id": "CategoriasDePista", "Values": ["Terra", "Asfalto"] }
          ]
        }
        """);

        Assert.Equal(2, types.Count);
        Assert.Equal(["Terra", "Asfalto"], types.Get("CategoriasDePista"));
    }

    // ---------- Terms ----------

    private static TermDatabase Terms(string json)
    {
        var database = new TermDatabase();
        database.Load(json);
        return database;
    }

    [Fact]
    public void TermoCadastradoSubstituiOPadrao()
    {
        var terms = Terms("""{ "Terms": { "shop.buy": "Requisitar" } }""");

        Assert.Equal("Requisitar", terms.Get("shop.buy", "Comprar"));
        Assert.Equal("Sair", terms.Get("shop.exit", "Sair"));
    }

    [Fact]
    public void TermoConhecidoCaiNoPadraoDaEngineSemCadastro()
    {
        var terms = new TermDatabase();

        Assert.Equal("Comprar", terms.Get("shop.buy"));
        // Chave desconhecida devolve a própria chave: aparece torto na tela, que é o jeito mais
        // rápido de descobrir o erro de digitação.
        Assert.Equal("meujogo.inexistente", terms.Get("meujogo.inexistente"));
    }

    [Fact]
    public void AceitaTantoObjetoQuantoLista()
    {
        var objeto = Terms("""{ "Terms": { "a": "um" } }""");
        var lista = Terms("""{ "Terms": [ { "Key": "a", "Text": "um" } ] }""");

        Assert.Equal("um", objeto.Get("a", ""));
        Assert.Equal("um", lista.Get("a", ""));
    }

    [Fact]
    public void LojaUsaOsTermosCadastrados()
    {
        var items = new ItemDatabase();
        items.Load("""{ "Items": [ { "Id": "pocao", "Name": "Poção", "Price": 50 } ] }""");

        var dialogue = new DialogueSystem();
        var state = new GameState();
        var terms = Terms("""{ "Terms": { "shop.exit": "Tchau", "shop.cantAfford": "Volte com grana." } }""");
        var shop = new ShopSystem(dialogue, new InventoryManager(), items, state, terms);

        shop.Open(["pocao"], "Ouro", "Buy", 0f);
        dialogue.Update();

        var choice = Assert.IsType<DialogueChoice>(dialogue.Current);
        Assert.Equal("Tchau", choice.Options[^1]);

        // Sem dinheiro nenhum: a mensagem de recusa também vem do cadastro.
        dialogue.Advance();
        dialogue.Update();
        var message = Assert.IsType<DialogueMessage>(dialogue.Current);
        Assert.Equal("Volte com grana.", message.Text);
    }

    [Fact]
    public void LojaSemBancoDeTermosUsaOsPadroes()
    {
        var items = new ItemDatabase();
        items.Load("""{ "Items": [ { "Id": "pocao", "Price": 50 } ] }""");

        var dialogue = new DialogueSystem();
        var shop = new ShopSystem(dialogue, new InventoryManager(), items, new GameState());

        shop.Open(["pocao"], "Ouro", "Buy", 0f);
        dialogue.Update();

        var choice = Assert.IsType<DialogueChoice>(dialogue.Current);
        Assert.Equal("Sair", choice.Options[^1]);
    }
}
