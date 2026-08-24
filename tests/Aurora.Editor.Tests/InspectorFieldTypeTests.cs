using System.Text.Json.Nodes;
using Aurora.Editor.ViewModels;

namespace Aurora.Editor.Tests;

/// <summary>
/// Que controle cada campo do inspector vira. Campo de conjunto fechado (tipo de clima, forma
/// do colisor) tem que ser LISTA — digitar "Chuva" onde a engine espera "Rain" dá uma cena que
/// carrega sem erro e não faz nada, que é o pior tipo de bug pra quem monta fase. Campo que
/// aponta pra algo do projeto tem que aceitar texto livre COM sugestão, porque o alvo pode ainda
/// não existir.
/// </summary>
public sealed class InspectorFieldTypeTests
{
    private static ComponentViewModel Build(string type, params (string Key, string Value)[] fields)
    {
        var node = new JsonObject { ["Type"] = type };
        foreach (var (key, value) in fields)
            node[key] = value;

        return new ComponentViewModel(node);
    }

    private static PropertyViewModel? Field(ComponentViewModel component, string name)
        => component.Properties.FirstOrDefault(p => p.Name == name);

    [Theory]
    [InlineData("Weather", "Kind")]
    [InlineData("Collider", "Shape")]
    [InlineData("AttackSpawner", "AimMode")]
    [InlineData("UiText", "AnchorX")]
    [InlineData("UiText", "AnchorY")]
    public void CampoDeConjuntoFechadoViraLista(string component, string field)
        => Assert.IsType<EnumPropertyViewModel>(Field(Build(component), field));

    [Theory]
    [InlineData("NavAgent", "Follow")]
    [InlineData("FollowTarget", "TargetName")]
    [InlineData("Spawner", "Prefab")]
    [InlineData("AttackSpawner", "TriggerKey")]
    [InlineData("Weather", "ThunderSound")]
    [InlineData("TopDownController", "JoystickScreen")]
    public void CampoQueApontaProProjetoAceitaTextoLivreComSugestao(string component, string field)
        => Assert.IsType<SuggestPropertyViewModel>(Field(Build(component), field));

    [Fact]
    public void AListaDeClimaMostraPortuguesEGravaIngles()
    {
        // O formato de cena é em inglês; quem monta a fase não deveria precisar saber disso.
        var weather = (EnumPropertyViewModel)Field(Build("Weather"), "Kind")!;

        var chuva = Assert.Single(weather.Options, o => o.Label == "Chuva");
        Assert.Equal("Rain", chuva.Value);

        Assert.Contains(weather.Options, o => o.Label == "Tempestade de areia" && o.Value == "Sandstorm");
        Assert.Contains(weather.Options, o => o.Label == "Neblina" && o.Value == "Fog");
        Assert.Contains(weather.Options, o => o.Label == "Vento" && o.Value == "Wind");
    }

    [Fact]
    public void EscolherNaListaGravaOValorInterno()
    {
        var node = new JsonObject { ["Type"] = "Weather" };
        var weather = (EnumPropertyViewModel)new ComponentViewModel(node).Properties
            .First(p => p.Name == "Kind");

        weather.Selected = weather.Options.First(o => o.Label == "Neve");

        Assert.Equal("Snow", node["Kind"]!.GetValue<string>());
    }

    [Fact]
    public void ValorForaDaListaApareceVazioEmVezDeMentir()
    {
        // Cena com JSON editado à mão, ou campo renomeado numa versão nova. Cair na primeira
        // opção esconderia o problema e gravaria por cima do que o autor escreveu.
        var weather = (EnumPropertyViewModel)Field(
            Build("Weather", ("Kind", "furacao_de_sapos")), "Kind")!;

        Assert.Null(weather.Selected);
        Assert.Equal("furacao_de_sapos", weather.Value);
    }

    [Fact]
    public void KindSoViraListaNoWeather()
    {
        // A chave é Componente.Campo: sem isso, um "Kind" em qualquer componente futuro herdaria
        // a lista de climas.
        var node = new JsonObject { ["Type"] = "MeuScriptCustom", ["Kind"] = "qualquer coisa" };

        var field = Field(new ComponentViewModel(node), "Kind");

        Assert.IsType<TextPropertyViewModel>(field);
    }

    [Fact]
    public void CampoComSugestaoAceitaValorQueNaoEstaNaLista()
    {
        // A entidade alvo pode nascer só em jogo (spawn de prefab): travar nas opções impediria
        // de apontar pra ela.
        var node = new JsonObject { ["Type"] = "FollowTarget" };
        var target = (SuggestPropertyViewModel)new ComponentViewModel(node).Properties
            .First(p => p.Name == "TargetName");

        target.Value = "InimigoQueAindaNaoExiste";

        Assert.Equal("InimigoQueAindaNaoExiste", node["TargetName"]!.GetValue<string>());
    }

    [Fact]
    public void OSwitchDoSpawnerApareceNoInspector()
    {
        // O campo existia no runtime e faltava no schema do editor: quem monta a cena não tinha
        // como ligar "só nasce de noite" sem editar JSON na mão.
        var spawner = Build("Spawner");

        Assert.NotNull(Field(spawner, "RequiredSwitch"));
        Assert.NotNull(Field(spawner, "RequiredSwitchOn"));
    }

    [Fact]
    public void AbrirOInspectorNaoGravaNadaNoJson()
    {
        // Vale pros controles novos também: campo canônico aparece mesmo ausente, mas só de
        // olhar não pode marcar a cena como alterada.
        var node = new JsonObject { ["Type"] = "Weather" };

        _ = new ComponentViewModel(node);

        Assert.False(node.ContainsKey("Kind"));
        Assert.False(node.ContainsKey("ThunderSound"));
    }
}
