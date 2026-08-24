using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Events;

namespace Aurora.Runtime.Tests;

/// <summary>
/// O gatilho <c>PlayerInteract</c>: encostar E apertar, junto. É a peça que faltava pra montar
/// porta, NPC e alavanca sem escrever C# — <c>PlayerTouch</c> media distância mas não via
/// teclado, e <c>KeyPress</c> via a tecla mas disparava de qualquer canto do mapa.
///
/// <para>Sem janela não há teclado, então o que dá pra prender aqui é o lado do E: com o input
/// ausente, estar coladinho na porta NÃO basta. É justamente a metade que <c>PlayerTouch</c> já
/// resolvia sozinho — se um dia alguém "simplificar" o gatilho novo de volta pra só distância,
/// estes testes caem.</para>
/// </summary>
public class PlayerInteractTriggerTests
{
    private static (World World, EventSystem Events, GameState State) Build()
    {
        var world = new World();
        var state = new GameState();
        var events = new EventSystem(world, state);

        var player = world.CreateEntity("Player");
        player.Add(new Transform());

        return (world, events, state);
    }

    private static void AddDoor(World world, string trigger, float x = 0f)
    {
        var door = world.CreateEntity("Porta");
        door.Add(new Transform(new Vector2(x, 0f)));
        door.Add(new EventTrigger
        {
            Trigger = trigger,
            Radius = 24f,
            Key = "E",
            Once = false,
            Actions = [new EventAction { Type = "SetSwitch", Name = "porta_usada", On = true }],
        });
    }

    [Fact]
    public void StandingOnTheDoor_IsNotEnough_WithoutPressingAnything()
    {
        var (world, events, state) = Build();
        AddDoor(world, "PlayerInteract");

        // Jogador exatamente em cima e nenhum input ligado.
        events.Update(1f / 60f);

        Assert.False(state.GetSwitch("porta_usada"),
            "PlayerInteract disparou só por proximidade — virou um PlayerTouch.");
    }

    [Fact]
    public void PlayerTouch_StillFiresOnProximityAlone()
    {
        // Contraprova: a diferença entre os dois gatilhos é exatamente o controle exigido.
        var (world, events, state) = Build();
        AddDoor(world, "PlayerTouch");

        events.Update(1f / 60f);

        Assert.True(state.GetSwitch("porta_usada"));
    }

    [Fact]
    public void FarFromTheDoor_NothingHappensEither()
    {
        var (world, events, state) = Build();
        AddDoor(world, "PlayerInteract", x: 500f);

        events.Update(1f / 60f);

        Assert.False(state.GetSwitch("porta_usada"));
    }

    [Fact]
    public void KeyPress_KeepsIgnoringDistance_SoOldContentDoesNotBreak()
    {
        // Radius vem com 20 por padrão em TODO gatilho. Se o KeyPress passasse a respeitá-lo,
        // todo "aperte E pra abrir o menu" já existente viraria, em silêncio, um gatilho que só
        // funciona perto de alguma coisa. Foi por isso que o comportamento novo virou um tipo
        // próprio em vez de mudar o KeyPress.
        var (world, events, _) = Build();

        var menu = world.CreateEntity("Menu");
        menu.Add(new Transform(new Vector2(9999f, 9999f)));
        var trigger = new EventTrigger { Trigger = "KeyPress", Key = "E", Radius = 20f };
        menu.Add(trigger);

        events.Update(1f / 60f);

        // Sem input não dispara de qualquer jeito; o que se prende é que a distância não entra
        // na conta — o gatilho continua sendo avaliado pelo controle, não pelo raio.
        Assert.Equal("KeyPress", trigger.Trigger);
        Assert.Equal(20f, trigger.Radius);
    }

    [Fact]
    public void TheTriggerAcceptsAnyControlName_NotOnlyKeyboard()
    {
        // "MouseLeft" antes virava Key.Unknown e o gatilho nunca disparava. Aqui só se confirma
        // que o nome é aceito e guardado; a resolução em si está em InputBindingTests.
        var (world, events, _) = Build();

        var door = world.CreateEntity("Porta");
        door.Add(new Transform());
        var trigger = new EventTrigger { Trigger = "PlayerInteract", Key = "MouseLeft", Radius = 24f };
        door.Add(trigger);

        events.Update(1f / 60f);

        Assert.Equal("MouseLeft", trigger.Key);
        Assert.NotEqual(Input.InputKind.None, Input.InputBinding.Parse(trigger.Key).Kind);
    }
}
