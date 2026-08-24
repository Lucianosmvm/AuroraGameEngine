using System.Numerics;
using Aurora.Runtime.Database;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Events;
using Aurora.Runtime.UI;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Bancos que existem pra um jogo não precisar de código: eventos comuns (uma sequência
/// cadastrada e chamada de vários lugares), status (veneno/lentidão), teto de pilha do item e
/// loja. Nenhum deles é obrigatório — os testes de "sem banco" prendem justamente isso: sem
/// arquivo, o jogo roda igual e ninguém estoura.
/// </summary>
public class DatabaseFeaturesTests
{
    private const float Tolerance = 0.01f;

    private static (World World, EventSystem Events, GameState State) Build()
    {
        var world = new World();
        var state = new GameState();
        return (world, new EventSystem(world, state), state);
    }

    private static void Fire(World world, EventSystem events, params EventAction[] actions)
    {
        var source = world.CreateEntity("Gatilho");
        source.Add(new Transform(Vector2.Zero));
        source.Add(new EventTrigger { Trigger = "SceneStart", Once = true, Actions = [.. actions] });
        events.Update(0.016f);
    }

    // ---------- Eventos comuns ----------

    [Fact]
    public void CallEventRodaASequenciaCadastrada()
    {
        var (world, events, state) = Build();
        var database = new CommonEventDatabase();
        database.Load("""
        {
          "Events": [
            { "Id": "abrir_bau", "Actions": [
                { "Action": "SetSwitch", "Name": "BauAberto", "On": true },
                { "Action": "AddItem", "Name": "pocao", "Value": 2 }
            ]}
          ]
        }
        """);
        events.CommonEvents = database;
        events.Inventory = new InventoryManager();

        Fire(world, events, new EventAction { Type = "CallEvent", Name = "abrir_bau" });

        Assert.True(state.GetSwitch("BauAberto"));
        Assert.Equal(2, events.Inventory.GetCount("pocao"));
    }

    [Fact]
    public void EventoComumRecebeQuemChamouComoSelf()
    {
        // É o que permite UMA sequência servir a quarenta baús: as ações miram "Self".
        var (world, events, _) = Build();
        var database = new CommonEventDatabase();
        database.Load("""
        { "Events": [ { "Id": "sumir", "Actions": [ { "Action": "Destroy" } ] } ] }
        """);
        events.CommonEvents = database;

        var bau = world.CreateEntity("Bau");
        bau.Add(new Transform(Vector2.Zero));
        bau.Add(new EventTrigger
        {
            Trigger = "SceneStart",
            Actions = [new EventAction { Type = "CallEvent", Name = "sumir" }],
        });

        events.Update(0.016f);

        Assert.False(world.IsAlive(bau.Id));
    }

    [Fact]
    public void EventoComumQueChamaASiMesmoNaoTravaOJogo()
    {
        var (world, events, state) = Build();
        var database = new CommonEventDatabase();
        database.Load("""
        {
          "Events": [
            { "Id": "laco", "Actions": [
                { "Action": "SetSwitch", "Name": "Rodou", "On": true },
                { "Action": "CallEvent", "Name": "laco" }
            ]}
          ]
        }
        """);
        events.CommonEvents = database;

        Fire(world, events, new EventAction { Type = "CallEvent", Name = "laco" });

        // Rodou uma vez e a recursão foi cortada — sem StackOverflow.
        Assert.True(state.GetSwitch("Rodou"));
    }

    [Fact]
    public void EventoAutomaticoDisparaNaBordaDoSwitchENaoTodoFrame()
    {
        var (world, events, state) = Build();
        var database = new CommonEventDatabase();
        database.Load("""
        {
          "Events": [
            { "Id": "conta", "Trigger": "OnSwitchOn", "Switch": "Ligado",
              "Actions": [ { "Action": "SetVariable", "Name": "Contador", "Op": "Add", "Value": 1 } ] }
          ]
        }
        """);
        events.CommonEvents = database;

        events.Update(0.016f);
        Assert.Equal(0f, state.GetVariable("Contador"), Tolerance);

        state.SetSwitch("Ligado", true);
        events.Update(0.016f);
        events.Update(0.016f);
        events.Update(0.016f);
        Assert.Equal(1f, state.GetVariable("Contador"), Tolerance);

        // Desligar e religar é uma borda nova.
        state.SetSwitch("Ligado", false);
        events.Update(0.016f);
        state.SetSwitch("Ligado", true);
        events.Update(0.016f);
        Assert.Equal(2f, state.GetVariable("Contador"), Tolerance);
    }

    [Fact]
    public void EventoAutomaticoSemSwitchNuncaRoda()
    {
        var (_, events, state) = Build();
        var database = new CommonEventDatabase();
        database.Load("""
        {
          "Events": [
            { "Id": "solto", "Trigger": "WhileSwitchOn",
              "Actions": [ { "Action": "SetSwitch", "Name": "Rodou", "On": true } ] }
          ]
        }
        """);
        events.CommonEvents = database;

        events.Update(0.016f);

        Assert.False(state.GetSwitch("Rodou"));
    }

    // ---------- Status ----------

    private static StatusDatabase StatusBank(string json)
    {
        var database = new StatusDatabase();
        database.Load(json);
        return database;
    }

    [Fact]
    public void VenenoTiraVidaAoLongoDoTempoESaiSozinho()
    {
        var (world, events, _) = Build();
        var status = StatusBank("""
        { "Status": [ { "Id": "veneno", "Duration": 3, "DamagePerSecond": 10 } ] }
        """);
        world.StatusDatabase = status;
        events.Status = status;

        var alvo = world.CreateEntity("slime");
        alvo.Add(new Transform(Vector2.Zero));
        alvo.Add(new Health { Max = 100f, Current = 100f });

        Fire(world, events, new EventAction { Type = "AddStatus", Name = "veneno", Text = "slime" });

        Assert.True(alvo.Get<Status>()!.Has("veneno"));

        // 2 segundos de veneno a 10/s.
        for (int i = 0; i < 20; i++)
            world.Update(0.1f);

        Assert.Equal(80f, alvo.Get<Health>()!.Current, 1f);
        Assert.True(alvo.Get<Status>()!.Has("veneno"));

        // Passando dos 3 segundos, sai sozinho e para de machucar.
        for (int i = 0; i < 20; i++)
            world.Update(0.1f);

        Assert.False(alvo.Get<Status>()!.Has("veneno"));
        float depois = alvo.Get<Health>()!.Current;

        for (int i = 0; i < 10; i++)
            world.Update(0.1f);

        Assert.Equal(depois, alvo.Get<Health>()!.Current, Tolerance);
    }

    [Fact]
    public void VenenoNaoConcedeInvulnerabilidadeContraGolpeDeVerdade()
    {
        // O tique do veneno passa pelo World.Damage. Se ele renovasse os i-frames, o alvo ficaria
        // imune a tudo enquanto estivesse envenenado — o oposto do que a ficha promete.
        var world = new World();
        var status = StatusBank("""
        { "Status": [ { "Id": "veneno", "Duration": 10, "DamagePerSecond": 20 } ] }
        """);
        world.StatusDatabase = status;

        var alvo = world.CreateEntity("Player");
        alvo.Add(new Transform(Vector2.Zero));
        alvo.Add(new Health { Max = 100f, Current = 100f, InvulnerabilityAfterHit = 1f });
        alvo.Add(new Status()).Apply("veneno");

        world.Update(0.1f);
        float aposVeneno = alvo.Get<Health>()!.Current;

        Assert.True(world.Damage(alvo, 30f), "o golpe tinha que passar mesmo com veneno ativo");
        Assert.Equal(aposVeneno - 30f, alvo.Get<Health>()!.Current, Tolerance);
    }

    [Fact]
    public void MultiplicadoresDeStatusValemNaVelocidadeENoDanoRecebido()
    {
        var world = new World();
        world.StatusDatabase = StatusBank("""
        {
          "Status": [
            { "Id": "lento", "Duration": 0, "SpeedMultiplier": 0.5 },
            { "Id": "vulneravel", "Duration": 0, "DamageTakenMultiplier": 2 },
            { "Id": "imune", "Duration": 0, "DamageTakenMultiplier": 0 }
          ]
        }
        """);

        var alvo = world.CreateEntity("slime");
        alvo.Add(new Transform(Vector2.Zero));
        alvo.Add(new Health { Max = 100f, Current = 100f });
        var status = alvo.Add(new Status());

        status.Apply("lento");
        Assert.Equal(0.5f, status.SpeedMultiplier, Tolerance);

        status.Apply("vulneravel");
        world.Damage(alvo, 10f);
        Assert.Equal(80f, alvo.Get<Health>()!.Current, Tolerance);

        status.Remove("vulneravel");
        status.Apply("imune");
        Assert.False(world.Damage(alvo, 50f));
        Assert.Equal(80f, alvo.Get<Health>()!.Current, Tolerance);
    }

    [Fact]
    public void AddStatusCriaOComponenteEmQuemNaoTem()
    {
        var (world, events, _) = Build();
        var status = StatusBank("""{ "Status": [ { "Id": "lento", "Duration": 5 } ] }""");
        world.StatusDatabase = status;
        events.Status = status;

        var alvo = world.CreateEntity("slime");
        alvo.Add(new Transform(Vector2.Zero));
        Assert.Null(alvo.Get<Status>());

        Fire(world, events, new EventAction { Type = "AddStatus", Name = "lento", Text = "slime" });

        Assert.NotNull(alvo.Get<Status>());
        Assert.True(alvo.Get<Status>()!.Has("lento"));
    }

    [Fact]
    public void StatusPegaOGrupoPorEtiqueta()
    {
        var (world, events, _) = Build();
        var status = StatusBank("""{ "Status": [ { "Id": "lento", "Duration": 5 } ] }""");
        world.StatusDatabase = status;
        events.Status = status;

        var slime = world.CreateEntity("slimeazul");
        slime.Add(new Transform(new Vector2(10f, 0f)));
        slime.Add(new Tags { Value = "inimigo" });

        var player = world.CreateEntity("Player");
        player.Add(new Transform(new Vector2(20f, 0f)));

        Fire(world, events, new EventAction { Type = "AddStatus", Name = "lento", Text = "#inimigo" });

        Assert.True(slime.Get<Status>()!.Has("lento"));
        Assert.Null(player.Get<Status>());
    }

    // ---------- Teto de pilha ----------

    [Fact]
    public void MaxStackLimitaOQueEntraNoInventario()
    {
        var items = new ItemDatabase();
        items.Load("""{ "Items": [ { "Id": "pocao", "MaxStack": 3 } ] }""");

        var inventory = new InventoryManager { Database = items };

        Assert.Equal(2, inventory.Add("pocao", 2));
        Assert.Equal(1, inventory.Add("pocao", 5));
        Assert.Equal(3, inventory.GetCount("pocao"));
        Assert.Equal(0, inventory.Add("pocao", 1));
    }

    [Fact]
    public void SemBancoOInventarioNaoTemTeto()
    {
        var inventory = new InventoryManager();
        inventory.Add("pedra", 999);
        Assert.Equal(999, inventory.GetCount("pedra"));
    }

    // ---------- Loja ----------

    private static (ShopSystem Shop, DialogueSystem Dialogue, InventoryManager Inventory, GameState State) Shop()
    {
        var items = new ItemDatabase();
        items.Load("""
        {
          "Items": [
            { "Id": "pocao", "Name": "Poção", "Price": 50 },
            { "Id": "espada", "Name": "Espada", "Price": 300 }
          ]
        }
        """);

        var dialogue = new DialogueSystem();
        var inventory = new InventoryManager { Database = items };
        var state = new GameState();
        return (new ShopSystem(dialogue, inventory, items, state), dialogue, inventory, state);
    }

    /// <summary>Escolhe a opção de índice dado na escolha aberta agora.</summary>
    private static void Choose(DialogueSystem dialogue, int index)
    {
        dialogue.Update();
        for (int i = 0; i < index; i++)
            dialogue.SelectNext();
        dialogue.Advance();
    }

    [Fact]
    public void ComprarTiraDinheiroEDaOItem()
    {
        var (shop, dialogue, inventory, state) = Shop();
        state.SetVariable("Ouro", 100f);

        shop.Open(["pocao", "espada"], "Ouro", "Buy", 0f);
        Choose(dialogue, 0);

        Assert.Equal(1, inventory.GetCount("pocao"));
        Assert.Equal(50f, state.GetVariable("Ouro"), Tolerance);
    }

    [Fact]
    public void SemDinheiroNaoCompra()
    {
        var (shop, dialogue, inventory, state) = Shop();
        state.SetVariable("Ouro", 10f);

        shop.Open(["pocao"], "Ouro", "Buy", 0f);
        Choose(dialogue, 0);

        Assert.Equal(0, inventory.GetCount("pocao"));
        Assert.Equal(10f, state.GetVariable("Ouro"), Tolerance);
    }

    [Fact]
    public void VenderPagaAFracaoDoPreco()
    {
        var (shop, dialogue, inventory, state) = Shop();
        inventory.Add("espada", 1);

        shop.Open(["pocao"], "Ouro", "Sell", 0f);
        Choose(dialogue, 0);

        Assert.Equal(0, inventory.GetCount("espada"));
        Assert.Equal(150f, state.GetVariable("Ouro"), Tolerance);
    }

    [Fact]
    public void LojaReabreDepoisDeCadaCompraEFechaNoSair()
    {
        var (shop, dialogue, inventory, state) = Shop();
        state.SetVariable("Ouro", 200f);

        shop.Open(["pocao"], "Ouro", "Buy", 0f);
        Choose(dialogue, 0);
        Choose(dialogue, 0);

        Assert.Equal(2, inventory.GetCount("pocao"));
        Assert.Equal(100f, state.GetVariable("Ouro"), Tolerance);

        // Índice 1 = "Sair" (uma mercadoria + sair).
        Choose(dialogue, 1);
        dialogue.Update();
        Assert.False(dialogue.IsActive);
    }
}
