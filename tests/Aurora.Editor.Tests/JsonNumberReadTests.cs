using System.Text.Json.Nodes;
using Aurora.Editor.Models;
using Aurora.Editor.ViewModels;

namespace Aurora.Editor.Tests;

/// <summary>
/// Leitura de número vinda de nó JSON. O editor cria nó em memória (<c>["Value"] = 50</c>, um
/// Int32 boxeado) e também lê nó vindo de arquivo (JsonElement). <c>GetValue&lt;float&gt;()</c>
/// só aguenta o segundo caso — no primeiro estoura InvalidOperationException e derruba o
/// inspector inteiro na hora de desenhar o campo. Estes testes prendem os dois caminhos.
/// </summary>
public sealed class JsonNumberReadTests
{
    [Fact]
    public void AsFloat_LeInteiroCriadoEmMemoria()
    {
        var node = new JsonObject { ["Value"] = 50 };
        Assert.Equal(50f, node["Value"].AsFloat(0f));
    }

    [Fact]
    public void AsFloat_LeNumeroVindoDeArquivo()
    {
        var node = JsonNode.Parse("""{ "Value": 50, "Other": 1.5 }""")!.AsObject();
        Assert.Equal(50f, node["Value"].AsFloat(0f));
        Assert.Equal(1.5f, node["Other"].AsFloat(0f));
    }

    [Fact]
    public void AsFloat_DevolveFallbackQuandoNaoENumero()
    {
        var node = new JsonObject { ["Value"] = "texto" };
        Assert.Equal(7f, node["Value"].AsFloat(7f));
        Assert.Equal(7f, node["Ausente"].AsFloat(7f));
    }

    [Fact]
    public void AsInt_LeFloatCriadoEmMemoriaTruncando()
    {
        var node = new JsonObject { ["MaxStack"] = 3.9f };
        Assert.Equal(3, node["MaxStack"].AsInt(0));
    }

    [Fact]
    public void ValueFloat_NaoEstouraComInteiroBoxeado()
    {
        // Reproduz o efeito de item criado pelo banco de dados: {"Action":"Heal","Value":50}.
        var node = new JsonObject { ["Action"] = "Heal", ["Value"] = 50 };
        var vm = new EventActionViewModel(node, () => { }, _ => { });

        Assert.Equal(50f, vm.ValueFloat);
        Assert.Equal("50", vm.ValueText);
    }

    [Fact]
    public void CamposDeAcao_LeemInteirosBoxeados()
    {
        var node = new JsonObject
        {
            ["Action"] = "Teleport",
            ["X"] = 10,
            ["Y"] = 20,
            ["Seconds"] = 2,
            ["Chance"] = 1,
        };
        var vm = new EventActionViewModel(node, () => { }, _ => { });

        Assert.Equal(10f, vm.X);
        Assert.Equal(20f, vm.Y);
        Assert.Equal(2f, vm.Seconds);
        Assert.Equal(1f, vm.ChanceFloat);
    }
}
