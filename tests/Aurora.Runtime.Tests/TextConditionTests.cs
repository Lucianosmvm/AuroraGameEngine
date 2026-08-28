using Aurora.Runtime.Ecs;
using Aurora.Runtime.Events;

namespace Aurora.Runtime.Tests;

/// <summary>
/// A ação <c>SetText</c> e a condição <c>If</c> sobre texto — sem elas o nome ficaria guardado
/// e não haveria como reagir a ele.
/// </summary>
public sealed class TextConditionTests
{
    private static (EventSystem Events, GameState State) Build()
    {
        var state = new GameState();
        return (new EventSystem(new World(), state), state);
    }

    private static EventAction TextIs(string variable, string expected, string op = "==")
        => new() { Name = variable, Text = "Text", Op = op, TextValue = expected };

    [Fact]
    public void SetText_GravaNaVariavel()
    {
        var (events, state) = Build();

        events.RunActions([new EventAction { Type = "SetText", Name = "Nome", TextValue = "Ana" }]);

        Assert.Equal("Ana", state.GetText("Nome"));
    }

    [Fact]
    public void SetTextSemValor_LimpaAVariavel()
    {
        var (events, state) = Build();
        state.SetText("Nome", "Ana");

        events.RunActions([new EventAction { Type = "SetText", Name = "Nome" }]);

        Assert.Equal("", state.GetText("Nome"));
    }

    [Fact]
    public void CondicaoDeIgualdade()
    {
        var (events, state) = Build();
        state.SetText("Nome", "Ana");

        Assert.True(events.TestCondition(TextIs("Nome", "Ana")));
        Assert.False(events.TestCondition(TextIs("Nome", "Beto")));
    }

    [Fact]
    public void CondicaoDeDiferenca()
    {
        var (events, state) = Build();
        state.SetText("Nome", "Ana");

        Assert.True(events.TestCondition(TextIs("Nome", "Beto", "!=")));
        Assert.False(events.TestCondition(TextIs("Nome", "Ana", "!=")));
    }

    /// <summary>Quem digitou "ana" espera passar num teste escrito "Ana" — a alternativa é um
    /// evento que nunca dispara e nada explicando por quê.</summary>
    [Fact]
    public void ComparacaoIgnoraMaiuscula()
    {
        var (events, state) = Build();
        state.SetText("Nome", "ana");

        Assert.True(events.TestCondition(TextIs("Nome", "ANA")));
    }

    /// <summary>Variável nunca preenchida compara como vazia, não estoura.</summary>
    [Fact]
    public void VariavelInexistente_ComparaComoVazio()
    {
        var (events, _) = Build();

        Assert.True(events.TestCondition(TextIs("NaoExiste", "")));
        Assert.False(events.TestCondition(TextIs("NaoExiste", "Ana")));
    }

    /// <summary>Sem Op explícito o padrão é igualdade — o único que faz sentido pra texto.
    /// (Nas condições numéricas o padrão é ">=", e herdar aquele aqui daria sempre falso.)</summary>
    [Fact]
    public void SemOperador_AssumeIgualdade()
    {
        var (events, state) = Build();
        state.SetText("Nome", "Ana");

        Assert.True(events.TestCondition(new EventAction { Name = "Nome", Text = "Text", TextValue = "Ana" }));
    }

    /// <summary>As condições que já existiam não podem mudar de comportamento.</summary>
    [Fact]
    public void CondicaoNumericaContinuaIgual()
    {
        var (events, state) = Build();
        state.SetVariable("Ouro", 50f);

        Assert.True(events.TestCondition(new EventAction { Name = "Ouro", Value = 30f }));
        Assert.False(events.TestCondition(new EventAction { Name = "Ouro", Value = 80f }));
    }
}
