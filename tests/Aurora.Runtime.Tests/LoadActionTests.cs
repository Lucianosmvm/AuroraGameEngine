using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Events;

namespace Aurora.Runtime.Tests;

/// <summary>
/// A ação Load — o par da Save, que faltava. É ela que permite montar um botão "Continuar" no
/// menu sem escrever C#.
///
/// O ponto delicado não é carregar: é NÃO carregar na hora errada. Carregar troca a cena, e
/// trocar a cena esvazia o World no meio da varredura de gatilhos que disparou a ação — as ações
/// seguintes rodariam contra entidades que já não existem. Por isso a ação só ANUNCIA o pedido
/// (mesmo desenho do ChangeScene) e quem executa é o Game, no começo do frame seguinte.
/// </summary>
public class LoadActionTests
{
    private static (World World, EventSystem Events, GameState State) Build()
    {
        var world = new World();
        var state = new GameState();
        var events = new EventSystem(world, state);
        return (world, events, state);
    }

    /// <summary>Dispara um gatilho de Timer com as ações dadas e roda um frame.</summary>
    private static void Fire(World world, EventSystem events, params EventAction[] actions)
    {
        var entity = world.CreateEntity("Gatilho");
        entity.Add(new Transform());
        entity.Add(new EventTrigger { Trigger = "Timer", Interval = 0f, Actions = [.. actions] });

        events.Update(1f / 60f);
    }

    [Fact]
    public void LoadAction_AnnouncesTheRequest_WithTheSlot()
    {
        var (world, events, _) = Build();
        int? pedido = null;
        events.LoadRequested += slot => pedido = slot;

        Fire(world, events, new EventAction { Type = "Load", Value = 2f });

        Assert.Equal(2, pedido);
    }

    [Fact]
    public void NegativeSlot_MeansAutoSave()
    {
        var (world, events, _) = Build();
        int? pedido = null;
        events.LoadRequested += slot => pedido = slot;

        Fire(world, events, new EventAction { Type = "Load", Value = -1f });

        Assert.True(pedido is < 0);
    }

    [Fact]
    public void LoadAction_DoesNotSwapTheSceneDuringTheTriggerScan()
    {
        // O que garante que as ações seguintes não rodem num mundo já trocado: a ação não carrega
        // nada por conta própria, só avisa. Se um dia alguém chamar SaveManager.Load aqui dentro,
        // este teste continua passando — mas o mundo teria sido limpo no meio da varredura, então
        // o que se prende é que a entidade do gatilho sobrevive ao frame.
        var (world, events, _) = Build();
        events.LoadRequested += _ => { };

        Fire(world, events,
            new EventAction { Type = "Load", Value = 0f },
            new EventAction { Type = "SetSwitch", Name = "depois_do_load", On = true });

        Assert.True(world.TryFind("Gatilho", out _));
    }

    [Fact]
    public void ActionsAfterLoad_StillRunInTheSameSequence()
    {
        // Ninguém assinando LoadRequested (jogo montado à mão, sem SaveManager) não pode
        // interromper a sequência de ações.
        var (world, events, state) = Build();

        Fire(world, events,
            new EventAction { Type = "Load", Value = 0f },
            new EventAction { Type = "SetSwitch", Name = "depois_do_load", On = true });

        Assert.True(state.GetSwitch("depois_do_load"));
    }

    [Fact]
    public void WithoutAnySubscriber_LoadIsAQuietNoOp()
    {
        var (world, events, _) = Build();

        // Sem assinante o evento é null — não pode estourar NullReference.
        Fire(world, events, new EventAction { Type = "Load", Value = 0f });

        Assert.True(true);
    }
}
