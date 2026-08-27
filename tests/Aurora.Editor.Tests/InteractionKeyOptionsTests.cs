using Aurora.Editor.ViewModels;
using Aurora.Runtime.Input;

namespace Aurora.Editor.Tests;

/// <summary>
/// A lista de controles que o editor oferece no campo do gatilho, conferida contra o que a
/// engine realmente entende.
///
/// Roda no projeto do editor de propósito, cruzando os dois assemblies: oferecer no dropdown um
/// nome que o <c>InputBinding</c> não resolve seria dar ao autor uma escolha que não funciona —
/// e o sintoma seria "apertei a tecla e não aconteceu nada", sem erro nenhum pra investigar.
/// </summary>
public class InteractionKeyOptionsTests
{
    [Fact]
    public void EveryOfferedControl_IsUnderstoodByTheEngine()
    {
        foreach (string name in MainViewModel.KeyNames)
        {
            var binding = InputBinding.Parse(name);
            Assert.True(binding.Kind != InputKind.None,
                $"O editor oferece '{name}', que o InputBinding não resolve.");
        }
    }

    [Fact]
    public void TheListOffersMouseAndGamepad_NotOnlyKeyboard()
    {
        // O pedido que originou isto: poder escolher espaço, clique do mouse ou o que for. Se um
        // dia a lista voltar a ser só teclado, é aqui que aparece.
        var kinds = MainViewModel.KeyNames.Select(InputBinding.Parse).Select(b => b.Kind).ToHashSet();

        Assert.Contains(InputKind.Key, kinds);
        Assert.Contains(InputKind.Mouse, kinds);
        Assert.Contains(InputKind.Gamepad, kinds);
    }

    [Fact]
    public void MouseLeftIsOffered_BecauseItIsAlsoTouchOnAndroid()
    {
        // No Android o toque entra como clique esquerdo. É a única opção da lista que serve
        // desktop e celular ao mesmo tempo, então não pode sumir dela.
        Assert.Contains("MouseLeft", MainViewModel.KeyNames);
    }

    [Fact]
    public void TheTriggerListOffersPlayerInteract()
    {
        var trigger = new EventTriggerViewModel(new System.Text.Json.Nodes.JsonObject
        {
            ["Type"] = "EventTrigger",
        });

        Assert.Contains("PlayerInteract", trigger.TriggerTypes);
    }

    [Theory]
    [InlineData("PlayerInteract", true, true)]   // precisa dos dois: raio e controle
    [InlineData("PlayerTouch", true, false)]     // só raio
    [InlineData("KeyPress", false, true)]        // só controle
    [InlineData("Timer", false, false)]
    public void TheInspectorShowsTheFieldsEachTriggerActuallyUses(
        string triggerType, bool expectRadius, bool expectKey)
    {
        var trigger = new EventTriggerViewModel(new System.Text.Json.Nodes.JsonObject
        {
            ["Type"] = "EventTrigger",
            ["Trigger"] = triggerType,
        });

        Assert.Equal(expectRadius, trigger.ShowRadius);
        Assert.Equal(expectKey, trigger.ShowKey);
    }
}
