using Aurora.Runtime.UI;

namespace Aurora.Editor.Tests;

/// <summary>
/// A regra de Anchor. Vive em Aurora.Runtime e entra no Aurora.Editor por link no .csproj — este
/// teste roda no projeto do editor de propósito: se alguém quebrar o link e o editor voltar a ter
/// a própria cópia da regra, é aqui que aparece. Foi exatamente essa duplicação que fez o menu
/// montado no editor cair em outro lugar no jogo.
/// </summary>
public sealed class UiAnchorTests
{
    private const float ScreenWidth = 1280f;
    private const float ElementWidth = 200f;

    [Theory]
    [InlineData("Left")]
    [InlineData("Top")]
    [InlineData("")]
    [InlineData("qualquer-coisa-desconhecida")]
    public void SemAncora_XEhPixelAbsoluto(string anchor)
        => Assert.Equal(326f, UiAnchor.Resolve(anchor, 326f, ScreenWidth, ElementWidth));

    [Fact]
    public void Center_CentralizaOElementoNaTela()
    {
        // X=0 + Center = elemento centrado, não borda esquerda no centro.
        Assert.Equal((ScreenWidth - ElementWidth) / 2f, UiAnchor.Resolve("Center", 0f, ScreenWidth, ElementWidth));

        // O X vira deslocamento a partir do centro.
        Assert.Equal((ScreenWidth - ElementWidth) / 2f + 30f,
            UiAnchor.Resolve("Center", 30f, ScreenWidth, ElementWidth));
    }

    [Theory]
    [InlineData("Right")]
    [InlineData("Bottom")]
    public void RightBottom_MedemDaBordaOposta(string anchor)
    {
        // X=0 encosta na borda direita/inferior; X cresce indo pra dentro da tela.
        Assert.Equal(ScreenWidth - ElementWidth, UiAnchor.Resolve(anchor, 0f, ScreenWidth, ElementWidth));
        Assert.Equal(ScreenWidth - ElementWidth - 40f, UiAnchor.Resolve(anchor, 40f, ScreenWidth, ElementWidth));
    }

    [Theory]
    [InlineData("Left")]
    [InlineData("Center")]
    [InlineData("Right")]
    [InlineData("Bottom")]
    public void Unresolve_DesfazResolve(string anchor)
    {
        const float x = 137f;

        float edge = UiAnchor.Resolve(anchor, x, ScreenWidth, ElementWidth);
        float roundTrip = UiAnchor.Unresolve(anchor, edge, ScreenWidth, ElementWidth);

        // Sem essa volta exata, arrastar um elemento no editor o deslocaria a cada movimento do
        // mouse (e pra Right/Bottom, no sentido contrário).
        Assert.Equal(x, roundTrip, 3);
    }

    [Fact]
    public void Center_AcompanhaALarguraDaTela_LeftNao()
    {
        // O bug original em uma linha: numa tela mais estreita o elemento Center anda e o Left
        // fica parado. É por isso que o editor precisa usar a resolução do jogo, não o tamanho
        // do painel dele.
        float centerLargo = UiAnchor.Resolve("Center", 0f, 1280f, ElementWidth);
        float centerEstreito = UiAnchor.Resolve("Center", 0f, 777f, ElementWidth);
        float leftLargo = UiAnchor.Resolve("Left", 326f, 1280f, ElementWidth);
        float leftEstreito = UiAnchor.Resolve("Left", 326f, 777f, ElementWidth);

        Assert.NotEqual(centerLargo, centerEstreito);
        Assert.Equal(leftLargo, leftEstreito);
    }
}
