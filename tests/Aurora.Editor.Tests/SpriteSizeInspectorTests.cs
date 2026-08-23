using System.Text.Json.Nodes;
using Aurora.Editor.ViewModels;

namespace Aurora.Editor.Tests;

/// <summary>
/// SizeX/SizeY do SpriteRenderer no inspector. O risco aqui é o inverso do normal: campo
/// canônico aparece mesmo ausente do JSON, e se só de abrir o inspector ele fosse gravado com
/// o default 0, toda cena existente ganharia um sprite de lado zero — invisível no jogo.
/// </summary>
public sealed class SpriteSizeInspectorTests
{
    private static JsonObject SpriteNode() => new()
    {
        ["Type"] = "SpriteRenderer",
        ["Texture"] = "sprites/slime.png",
    };

    [Fact]
    public void SizeApareceNoInspectorMesmoAusenteDoJson()
    {
        var component = new ComponentViewModel(SpriteNode());

        Assert.Equal(0f, component.Number("SizeX")?.Value);
        Assert.Equal(0f, component.Number("SizeY")?.Value);
    }

    [Fact]
    public void AbrirOInspectorNaoGravaSizeNoJson()
    {
        var node = SpriteNode();

        _ = new ComponentViewModel(node);

        Assert.False(node.ContainsKey("SizeX"));
        Assert.False(node.ContainsKey("SizeY"));
    }

    [Fact]
    public void EditarSizeGravaNoJson()
    {
        var node = SpriteNode();
        var component = new ComponentViewModel(node);

        component.Number("SizeX")!.Value = 28f;
        component.Number("SizeY")!.Value = 28f;

        Assert.Equal(28f, node["SizeX"]!.GetValue<float>());
        Assert.Equal(28f, node["SizeY"]!.GetValue<float>());
    }
}
