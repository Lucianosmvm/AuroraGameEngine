using System.Text.Json.Nodes;
using Aurora.Editor.Models;
using Aurora.Editor.ViewModels;
using Avalonia.Media;

namespace Aurora.Editor.Tests;

/// <summary>
/// Seletor de cores do inspector. O ponto delicado é a ORDEM DOS CANAIS: o engine grava
/// <c>#RRGGBBAA</c> e o Avalonia lê <c>#AARRGGBB</c> — trocar os dois pinta a cena de uma cor
/// no editor e de outra no jogo, e o erro passa despercebido enquanto o alpha for FF. Estes
/// testes prendem a conversão, a escolha na paleta e o controle de opacidade.
/// </summary>
public sealed class ColorPropertyTests
{
    [Fact]
    public void Parse_LeAlphaNoFim()
    {
        var color = EngineColor.Parse("#20A0FF80", Colors.White);

        Assert.Equal(0x20, color.R);
        Assert.Equal(0xA0, color.G);
        Assert.Equal(0xFF, color.B);
        Assert.Equal(0x80, color.A);
    }

    [Fact]
    public void Parse_SemAlphaAssumeOpaco()
        => Assert.Equal(255, EngineColor.Parse("#20A0FF", Colors.Black).A);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("azul")]
    [InlineData("#12345")]
    public void Parse_ValorInvalidoCaiNoFallback(string? hex)
        => Assert.Equal(Colors.Magenta, EngineColor.Parse(hex, Colors.Magenta));

    [Fact]
    public void ToHex_EscreveSempreOitoDigitosNaOrdemDoEngine()
        => Assert.Equal("#20A0FF80", EngineColor.ToHex(Color.FromArgb(0x80, 0x20, 0xA0, 0xFF)));

    [Fact]
    public void EscolherCorDaPaleta_TrocaORgbEMantemAOpacidade()
    {
        var property = ColorProperty("#FFFFFF40");
        var vermelho = property.Choices.Single(c => c.Swatch.Name == "Vermelho");

        vermelho.Command.Execute(null);

        // #40 = 25% — quem já tinha deixado o campo translúcido não perde isso ao trocar a cor.
        Assert.Equal("#E23B3B40", property.Value);
        Assert.Equal("Vermelho", property.ColorName);
    }

    [Fact]
    public void EscolherCorComOpacidadePropria_AplicaAOpacidadeDoSwatch()
    {
        var property = ColorProperty("#FFFFFFFF");
        var sombra = property.Choices.Single(c => c.Swatch.Name == "Sombra");

        sombra.Command.Execute(null);

        Assert.Equal("#00000080", property.Value);
    }

    [Fact]
    public void Opacidade_EscreveOAlphaEDevolveEmPorcentagem()
    {
        var property = ColorProperty("#2D6CDFFF");
        Assert.Equal(100, property.Opacity);

        property.Opacity = 50;

        Assert.Equal("#2D6CDF80", property.Value);
        Assert.Equal("50%", property.OpacityText);
    }

    [Fact]
    public void CorForaDaPaleta_MostraOProprioHex()
        => Assert.Equal("#123456FF", ColorProperty("#123456FF").ColorName);

    [Fact]
    public void Edicao_AvisaOInspectorParaRedesenharEDesfazer()
    {
        var property = ColorProperty("#FFFFFFFF");
        string? tag = null;
        property.Edited += name => tag = name;

        property.Choices.Single(c => c.Swatch.Name == "Verde").Command.Execute(null);

        Assert.Equal("Color", tag);
    }

    [Fact]
    public void EscolherACorQueJaEstava_NaoGeraPassoDeDesfazer()
    {
        var property = ColorProperty("#FFFFFFFF");
        bool avisou = false;
        property.Edited += _ => avisou = true;

        property.Choices.Single(c => c.Swatch.Name == "Branco").Command.Execute(null);

        Assert.False(avisou);
    }

    [Fact]
    public void CampoComHexNoJson_ViraSeletorDeCor()
    {
        var node = new JsonObject
        {
            ["Type"] = "MeuScript",
            ["Cor"] = "#FF0000FF",       // campo de script custom, sem "Color" no nome
            ["Alvo"] = "Player",
        };

        var component = new ComponentViewModel(node);

        Assert.IsType<ColorPropertyViewModel>(component.Properties.Single(p => p.Name == "Cor"));
        Assert.IsType<TextPropertyViewModel>(component.Properties.Single(p => p.Name == "Alvo"));
    }

    [Fact]
    public void CampoDeCorDeComponenteNativo_ViraSeletorMesmoAusenteDoJson()
    {
        // O JSON omite valores default; o esquema do SpriteRenderer é que traz o "#FFFFFFFF".
        var component = new ComponentViewModel(new JsonObject { ["Type"] = "SpriteRenderer" });

        var color = Assert.IsType<ColorPropertyViewModel>(component.Properties.Single(p => p.Name == "Color"));
        Assert.Equal("#FFFFFFFF", color.Value);
    }

    private static ColorPropertyViewModel ColorProperty(string hex)
        => new(new JsonObject { ["Type"] = "SpriteRenderer", ["Color"] = hex }, "Color", "#FFFFFFFF");
}
