using Aurora.Runtime.Database;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Events;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Banco de itens (a "aba banco de dados") e ramificação condicional dentro de um evento — as
/// duas peças que faltavam pra autorar regra de jogo sem escrever código.
/// </summary>
public class ItemDatabaseAndBranchTests
{
    private const float Tolerance = 0.01f;

    private const string ItemsJson = """
        {
          "Items": [
            {
              "Id": "pocao",
              "Name": "Poção Pequena",
              "Icon": "sprites/pocao.png",
              "Description": "Cura 50 de vida.",
              "Type": "Consumivel",
              "MaxStack": 99,
              "Price": 25,
              "Effect": [ { "Action": "Heal", "Value": 50 } ]
            },
            {
              "Id": "chave_mestra",
              "Name": "Chave Mestra",
              "Type": "Chave",
              "Consumable": false,
              "Effect": [ { "Action": "SetSwitch", "Name": "PortaAberta", "On": true } ]
            }
          ]
        }
        """;

    private static (World World, EventSystem Events, GameState State, InventoryManager Inventory, ItemDatabase Items) Build()
    {
        var world = new World();
        var state = new GameState();
        var inventory = new InventoryManager();
        var items = new ItemDatabase();
        items.Load(ItemsJson);

        var events = new EventSystem(world, state) { Inventory = inventory, Items = items };
        return (world, events, state, inventory, items);
    }

    /// <summary>Roda uma sequência de ações como se fosse um evento de cena, com dono.</summary>
    private static void Run(World world, EventSystem events, Entity owner, params EventAction[] actions)
    {
        owner.Add(new EventTrigger { Trigger = "SceneStart", Actions = [.. actions] });
        events.Update(1f / 60f);
    }

    // ---------- Banco de itens ----------

    [Fact]
    public void BancoLeAsFichasDosItens()
    {
        var items = new ItemDatabase();
        items.Load(ItemsJson);

        var potion = items.Get("pocao")!;
        Assert.Equal("Poção Pequena", potion.Name);
        Assert.Equal("sprites/pocao.png", potion.Icon);
        Assert.Equal(25, potion.Price);
        Assert.Equal(99, potion.MaxStack);
        Assert.True(potion.Consumable);
        Assert.Single(potion.Effect);
    }

    [Fact]
    public void IdDesconhecidoNaoEErro()
    {
        // Jogo pode usar o inventário sem banco nenhum, só com contagem. Nome de exibição cai no
        // próprio id pra HUD nunca mostrar vazio.
        var items = new ItemDatabase();
        items.Load(ItemsJson);

        Assert.Null(items.Get("espada_lendaria"));
        Assert.Equal("espada_lendaria", items.DisplayName("espada_lendaria"));
    }

    [Fact]
    public void ItemSemIdEIgnoradoSemDerrubarOBanco()
    {
        var items = new ItemDatabase();

        items.Load("""{ "Items": [ { "Name": "Sem chave" }, { "Id": "ok" } ] }""");

        Assert.Equal(1, items.Count);
        Assert.NotNull(items.Get("ok"));
    }

    [Fact]
    public void UsarPocaoCuraQuemUsouEConsomeUmaUnidade()
    {
        var (world, events, _, inventory, _) = Build();
        inventory.Add("pocao", 3);

        var player = world.CreateEntity("Player");
        player.Add(new Transform());
        player.Add(new Health { Max = 100f, Current = 20f });

        var trigger = world.CreateEntity("Gatilho");
        trigger.Add(new Transform());
        Run(world, events, trigger, new EventAction { Type = "UseItem", Name = "pocao" });

        Assert.Equal(70f, player.Get<Health>()!.Current, Tolerance);
        Assert.Equal(2, inventory.GetCount("pocao"));
    }

    [Fact]
    public void ItemNaoConsumivelNaoSaiDoInventario()
    {
        var (world, events, state, inventory, _) = Build();
        inventory.Add("chave_mestra", 1);

        var trigger = world.CreateEntity("Porta");
        trigger.Add(new Transform());
        Run(world, events, trigger, new EventAction { Type = "UseItem", Name = "chave_mestra" });

        Assert.True(state.GetSwitch("PortaAberta"));
        Assert.Equal(1, inventory.GetCount("chave_mestra"));
    }

    [Fact]
    public void UsarItemQueNaoSeTemNaoFazNada()
    {
        var (world, events, _, _, _) = Build();

        var player = world.CreateEntity("Player");
        player.Add(new Transform());
        player.Add(new Health { Max = 100f, Current = 20f });

        var trigger = world.CreateEntity("Gatilho");
        trigger.Add(new Transform());
        Run(world, events, trigger, new EventAction { Type = "UseItem", Name = "pocao" });

        Assert.Equal(20f, player.Get<Health>()!.Current, Tolerance);
    }

    // ---------- Ramificação condicional ----------

    [Fact]
    public void RamoVerdadeiroRodaSoOCorpoDoIf()
    {
        var (world, events, state, inventory, _) = Build();
        inventory.Add("chave_mestra", 1);

        var door = world.CreateEntity("Porta");
        door.Add(new Transform());
        Run(world, events, door,
            new EventAction { Type = "If", Text = "Item", Name = "chave_mestra", Op = ">=", Value = 1f },
            new EventAction { Type = "SetVariable", Name = "Abriu", Value = 1f },
            new EventAction { Type = "Else" },
            new EventAction { Type = "SetVariable", Name = "Trancada", Value = 1f },
            new EventAction { Type = "EndIf" });

        Assert.Equal(1f, state.GetVariable("Abriu"), Tolerance);
        Assert.Equal(0f, state.GetVariable("Trancada"), Tolerance);
    }

    [Fact]
    public void RamoFalsoPulaProElse()
    {
        var (world, events, state, _, _) = Build();

        var door = world.CreateEntity("Porta");
        door.Add(new Transform());
        Run(world, events, door,
            new EventAction { Type = "If", Text = "Item", Name = "chave_mestra", Op = ">=", Value = 1f },
            new EventAction { Type = "SetVariable", Name = "Abriu", Value = 1f },
            new EventAction { Type = "Else" },
            new EventAction { Type = "SetVariable", Name = "Trancada", Value = 1f },
            new EventAction { Type = "EndIf" });

        Assert.Equal(0f, state.GetVariable("Abriu"), Tolerance);
        Assert.Equal(1f, state.GetVariable("Trancada"), Tolerance);
    }

    [Fact]
    public void AcoesDepoisDoEndIfRodamNosDoisCaminhos()
    {
        var (world, events, state, _, _) = Build();

        var door = world.CreateEntity("Porta");
        door.Add(new Transform());
        Run(world, events, door,
            new EventAction { Type = "If", Text = "Switch", Name = "NuncaLigado", On = true },
            new EventAction { Type = "SetVariable", Name = "Dentro", Value = 1f },
            new EventAction { Type = "EndIf" },
            new EventAction { Type = "SetVariable", Name = "Depois", Value = 1f });

        Assert.Equal(0f, state.GetVariable("Dentro"), Tolerance);
        Assert.Equal(1f, state.GetVariable("Depois"), Tolerance);
    }

    [Fact]
    public void IfDentroDeIfAchaOProprioElse()
    {
        // Contagem de profundidade: sem ela, o If de dentro roubaria o Else do de fora e a
        // sequência executaria os dois lados.
        var (world, events, state, _, _) = Build();
        state.SetSwitch("Externo", true);

        var npc = world.CreateEntity("Npc");
        npc.Add(new Transform());
        Run(world, events, npc,
            new EventAction { Type = "If", Text = "Switch", Name = "Externo", On = true },
                new EventAction { Type = "If", Text = "Switch", Name = "Interno", On = true },
                    new EventAction { Type = "SetVariable", Name = "InternoSim", Value = 1f },
                new EventAction { Type = "Else" },
                    new EventAction { Type = "SetVariable", Name = "InternoNao", Value = 1f },
                new EventAction { Type = "EndIf" },
            new EventAction { Type = "Else" },
                new EventAction { Type = "SetVariable", Name = "ExternoNao", Value = 1f },
            new EventAction { Type = "EndIf" });

        Assert.Equal(1f, state.GetVariable("InternoNao"), Tolerance);
        Assert.Equal(0f, state.GetVariable("InternoSim"), Tolerance);
        Assert.Equal(0f, state.GetVariable("ExternoNao"), Tolerance);
    }

    [Fact]
    public void IfComparaVariavelPeloOperador()
    {
        var (world, events, state, _, _) = Build();
        state.SetVariable("Ouro", 30f);

        var shop = world.CreateEntity("Loja");
        shop.Add(new Transform());
        Run(world, events, shop,
            new EventAction { Type = "If", Name = "Ouro", Op = ">=", Value = 25f },
            new EventAction { Type = "SetVariable", Name = "Comprou", Value = 1f },
            new EventAction { Type = "EndIf" });

        Assert.Equal(1f, state.GetVariable("Comprou"), Tolerance);
    }

    [Fact]
    public void BlocoSemEndIfTerminaASequenciaEmVezDeEstourar()
    {
        // JSON escrito à mão erra; um If sem fechamento não pode virar exceção de índice.
        var (world, events, state, _, _) = Build();

        var npc = world.CreateEntity("Npc");
        npc.Add(new Transform());
        Run(world, events, npc,
            new EventAction { Type = "If", Text = "Switch", Name = "NuncaLigado", On = true },
            new EventAction { Type = "SetVariable", Name = "Dentro", Value = 1f });

        Assert.Equal(0f, state.GetVariable("Dentro"), Tolerance);
    }

    [Fact]
    public void CondicaoTambemValeEmBotaoDeUi()
    {
        // RunActions é o caminho do UiButton.OnClick — condicionar lá é o que permite um botão de
        // HUD que só age se o jogador tiver o recurso.
        var (_, events, state, inventory, _) = Build();
        inventory.Add("pocao", 1);

        events.RunActions(
        [
            new EventAction { Type = "If", Text = "Item", Name = "pocao", Op = ">=", Value = 1f },
            new EventAction { Type = "SetVariable", Name = "Tem", Value = 1f },
            new EventAction { Type = "Else" },
            new EventAction { Type = "SetVariable", Name = "NaoTem", Value = 1f },
            new EventAction { Type = "EndIf" },
        ]);

        Assert.Equal(1f, state.GetVariable("Tem"), Tolerance);
        Assert.Equal(0f, state.GetVariable("NaoTem"), Tolerance);
    }
}
