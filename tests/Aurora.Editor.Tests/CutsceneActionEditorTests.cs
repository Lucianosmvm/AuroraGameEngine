using System.Text.Json.Nodes;
using Aurora.Editor.ViewModels;

namespace Aurora.Editor.Tests;

/// <summary>
/// Campos do editor pras duas peças de cutscene: a ação MoveTo (anda até X,Y contornando parede)
/// e o retrato de ShowMessage. O que se prende é a VISIBILIDADE de campo por ActionType — errar
/// isso mostra "Velocidade" onde devia estar "Segundos", ou esconde o Retrato de ShowMessage.
/// </summary>
public class CutsceneActionEditorTests
{
    private static EventActionViewModel Build(string actionType)
    {
        var node = new JsonObject { ["Action"] = actionType };
        return new EventActionViewModel(node, () => { }, _ => { });
    }

    [Fact]
    public void MoveToApareceNaListaDeAcoes()
    {
        var vm = Build("Wait");
        Assert.Contains("MoveTo", vm.ActionTypes);
    }

    [Fact]
    public void MoveToMostraEntidadeEXYEVelocidadeENaoOutrosCampos()
    {
        var vm = Build("MoveTo");

        Assert.True(vm.ShowNameText);
        Assert.True(vm.ShowXY);
        Assert.True(vm.ShowValue);
        Assert.Equal("Entidade", vm.NameLabel);
        Assert.Equal("Velocidade (0 = atual)", vm.ValueLabel);

        // Campos de outras ações não devem vazar pra MoveTo.
        Assert.False(vm.ShowSeconds);
        Assert.False(vm.ShowText);
        Assert.False(vm.ShowPortrait);
        Assert.False(vm.ShowOp);
        Assert.False(vm.ShowOn);
        Assert.False(vm.ShowSpawnPoint);
    }

    [Fact]
    public void MoveToNaoOfereceEtiquetaComoAlvo()
    {
        // O bloqueio da cutscene ("pausa até chegar") é de UMA entidade só — "#grupo" não tem
        // como "chegar", então não deve aparecer na sugestão do campo Entidade.
        var vm = Build("MoveTo");
        Assert.DoesNotContain(vm.NameSuggestions, s => s.StartsWith('#'));
    }

    [Fact]
    public void ValorDeMoveToLeEGravaComoNumero()
    {
        var vm = Build("MoveTo");
        vm.ValueText = "150";
        Assert.Equal(150f, vm.ValueFloat, 0.01f);
    }

    [Fact]
    public void XYDeMoveToLeEGravaComoNumero()
    {
        var vm = Build("MoveTo");
        vm.XText = "64";
        vm.YText = "32";
        Assert.Equal(64f, vm.X, 0.01f);
        Assert.Equal(32f, vm.Y, 0.01f);
    }

    [Fact]
    public void ShowMessageMostraORetrato()
    {
        var vm = Build("ShowMessage");
        Assert.True(vm.ShowPortrait);
        Assert.True(vm.ShowText);
    }

    [Fact]
    public void OutrasAcoesNaoMostramORetrato()
    {
        foreach (string type in new[] { "ShowChoice", "PlayAnimation", "Wait", "MoveTo", "Teleport" })
            Assert.False(Build(type).ShowPortrait, $"{type} não devia mostrar Retrato");
    }

    [Fact]
    public void PortraitLeEGravaNoNoEVoltaVazioQuandoLimpo()
    {
        var vm = Build("ShowMessage");
        Assert.Equal("", vm.Portrait);

        vm.Portrait = "sprites/retratos/ferreiro.png";
        Assert.Equal("sprites/retratos/ferreiro.png", vm.Portrait);

        // String vazia remove a chave do JSON — não grava lixo pra um retrato que o autor tirou.
        vm.Portrait = "";
        var node = new JsonObject { ["Action"] = "ShowMessage" };
        var vm2 = new EventActionViewModel(node, () => { }, _ => { });
        vm2.Portrait = "x.png";
        vm2.Portrait = "";
        Assert.False(node.ContainsKey("Portrait"));
    }

    [Fact]
    public void TrocarDeTipoAtualizaAVisibilidadeDoRetrato()
    {
        var vm = Build("Wait");
        Assert.False(vm.ShowPortrait);

        vm.ActionType = "ShowMessage";
        Assert.True(vm.ShowPortrait);

        vm.ActionType = "Teleport";
        Assert.False(vm.ShowPortrait);
    }

    [Fact]
    public void DescricaoDeMoveToMencionaOBloqueioDaSequencia()
    {
        var vm = Build("MoveTo");
        Assert.Contains("pausa", vm.ActionDescription, StringComparison.OrdinalIgnoreCase);
    }
}
