namespace Aurora.Runtime.Tests;

/// <summary>Variáveis/switches globais, inventário e quests — o modelo RPG Maker que os
/// eventos visuais e o save leem e escrevem.</summary>
public class GameStateTests
{
    private const float Tolerance = 0.001f;

    [Fact]
    public void VariavelNaoDefinidaUsaOFallback()
    {
        var state = new GameState();

        Assert.Equal(0f, state.GetVariable("Gold"), Tolerance);
        Assert.Equal(99f, state.GetVariable("Gold", 99f), Tolerance);
    }

    [Fact]
    public void SetEGetVariavelFazemParDireito()
    {
        var state = new GameState();
        state.SetVariable("Gold", 120f);

        Assert.Equal(120f, state.GetVariable("Gold"), Tolerance);
    }

    [Fact]
    public void AddVariableSomaSobreOValorAtual()
    {
        var state = new GameState();
        state.SetVariable("Gold", 10f);
        state.AddVariable("Gold", 5f);
        state.AddVariable("Gold", -3f);

        Assert.Equal(12f, state.GetVariable("Gold"), Tolerance);
    }

    [Fact]
    public void AddVariableEmVariavelNovaComecaDoZero()
    {
        var state = new GameState();
        state.AddVariable("XP", 25f);

        Assert.Equal(25f, state.GetVariable("XP"), Tolerance);
    }

    [Fact]
    public void NomesDeVariavelIgnoramMaiusculas()
    {
        var state = new GameState();
        state.SetVariable("Gold", 7f);

        Assert.Equal(7f, state.GetVariable("GOLD"), Tolerance);
        Assert.Equal(7f, state.GetVariable("gold"), Tolerance);
    }

    [Fact]
    public void SwitchNaoDefinidoEFalso()
    {
        Assert.False(new GameState().GetSwitch("PorteiraAberta"));
    }

    [Fact]
    public void SwitchLigaEDesliga()
    {
        var state = new GameState();
        state.SetSwitch("PorteiraAberta", true);
        Assert.True(state.GetSwitch("PorteiraAberta"));

        state.SetSwitch("PorteiraAberta", false);
        Assert.False(state.GetSwitch("PorteiraAberta"));
    }

    [Fact]
    public void NomesDeSwitchIgnoramMaiusculas()
    {
        var state = new GameState();
        state.SetSwitch("PorteiraAberta", true);

        Assert.True(state.GetSwitch("PORTEIRAABERTA"));
    }

    [Fact]
    public void ChangedDisparaEmCadaEscrita()
    {
        var state = new GameState();
        int disparos = 0;
        state.Changed += () => disparos++;

        state.SetVariable("Gold", 1f);
        state.AddVariable("Gold", 1f);
        state.SetSwitch("A", true);
        state.Clear();

        Assert.Equal(4, disparos);
    }

    [Fact]
    public void ClearZeraVariaveisESwitches()
    {
        var state = new GameState();
        state.SetVariable("Gold", 10f);
        state.SetSwitch("A", true);

        state.Clear();

        Assert.Empty(state.Variables);
        Assert.Empty(state.Switches);
    }

    [Fact]
    public void JsonFazRoundtripDeVariaveisESwitches()
    {
        var origem = new GameState();
        origem.SetVariable("Gold", 123.5f);
        origem.SetVariable("XP", -4f);
        origem.SetSwitch("PorteiraAberta", true);
        origem.SetSwitch("ChefeMorto", false);

        var destino = new GameState();
        destino.LoadJson(origem.ToJson());

        Assert.Equal(123.5f, destino.GetVariable("Gold"), Tolerance);
        Assert.Equal(-4f, destino.GetVariable("XP"), Tolerance);
        Assert.True(destino.GetSwitch("PorteiraAberta"));
        Assert.False(destino.GetSwitch("ChefeMorto"));
    }

    [Fact]
    public void LoadJsonDescartaOEstadoAnterior()
    {
        var destino = new GameState();
        destino.SetVariable("Lixo", 999f);

        destino.LoadJson(new GameState().ToJson());

        Assert.Empty(destino.Variables);
    }

    [Fact]
    public void LoadJsonInvalidoLancaErro()
    {
        Assert.Throws<InvalidDataException>(() => new GameState().LoadJson("null"));
    }
}

public class InventoryManagerTests
{
    [Fact]
    public void ItemInexistenteTemContagemZero()
    {
        var inventory = new InventoryManager();

        Assert.Equal(0, inventory.GetCount("Poção"));
        Assert.False(inventory.Has("Poção"));
    }

    [Fact]
    public void AddAcumulaQuantidade()
    {
        var inventory = new InventoryManager();
        inventory.Add("Poção", 2);
        inventory.Add("Poção", 3);

        Assert.Equal(5, inventory.GetCount("Poção"));
    }

    [Fact]
    public void RemoveSubtrai()
    {
        var inventory = new InventoryManager();
        inventory.Add("Poção", 5);
        inventory.Remove("Poção", 2);

        Assert.Equal(3, inventory.GetCount("Poção"));
    }

    [Fact]
    public void QuantidadeNuncaFicaNegativa()
    {
        var inventory = new InventoryManager();
        inventory.Add("Poção", 1);
        inventory.Remove("Poção", 10);

        Assert.Equal(0, inventory.GetCount("Poção"));
    }

    [Fact]
    public void ItemZeradoSaiDoDicionario()
    {
        var inventory = new InventoryManager();
        inventory.Add("Poção", 1);
        inventory.Remove("Poção", 1);

        Assert.DoesNotContain("Poção", inventory.Items.Keys);
    }

    [Fact]
    public void HasRespeitaAQuantidadePedida()
    {
        var inventory = new InventoryManager();
        inventory.Add("Chave", 2);

        Assert.True(inventory.Has("Chave"));
        Assert.True(inventory.Has("Chave", 2));
        Assert.False(inventory.Has("Chave", 3));
    }

    [Fact]
    public void NomesDeItemIgnoramMaiusculas()
    {
        var inventory = new InventoryManager();
        inventory.Add("Poção", 1);

        Assert.Equal(1, inventory.GetCount("POÇÃO"));
    }

    [Fact]
    public void AddComDeltaZeroNaoDisparaChanged()
    {
        var inventory = new InventoryManager();
        int disparos = 0;
        inventory.Changed += () => disparos++;

        inventory.Add("Poção", 0);

        Assert.Equal(0, disparos);
    }
}

public class QuestManagerTests
{
    [Fact]
    public void QuestNaoIniciadaEstaNoEstagioZero()
    {
        Assert.Equal(0, new QuestManager().GetStage("ResgateDoGato"));
    }

    [Fact]
    public void SetStageGravaOEstagio()
    {
        var quests = new QuestManager();
        quests.SetStage("ResgateDoGato", 3);

        Assert.Equal(3, quests.GetStage("ResgateDoGato"));
    }

    [Fact]
    public void AdvanceSomaUmPorPadrao()
    {
        var quests = new QuestManager();
        quests.Advance("ResgateDoGato");
        quests.Advance("ResgateDoGato");

        Assert.Equal(2, quests.GetStage("ResgateDoGato"));
    }

    [Fact]
    public void AdvanceAceitaPassoCustomizado()
    {
        var quests = new QuestManager();
        quests.Advance("ResgateDoGato", 5);

        Assert.Equal(5, quests.GetStage("ResgateDoGato"));
    }

    [Fact]
    public void IsAtLeastComparaComOEstagioAtual()
    {
        var quests = new QuestManager();
        quests.SetStage("ResgateDoGato", 2);

        Assert.True(quests.IsAtLeast("ResgateDoGato", 1));
        Assert.True(quests.IsAtLeast("ResgateDoGato", 2));
        Assert.False(quests.IsAtLeast("ResgateDoGato", 3));
    }

    [Fact]
    public void ClearApagaOProgresso()
    {
        var quests = new QuestManager();
        quests.SetStage("ResgateDoGato", 2);

        quests.Clear();

        Assert.Empty(quests.Stages);
        Assert.Equal(0, quests.GetStage("ResgateDoGato"));
    }
}
