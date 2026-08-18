using System.Numerics;
using Aurora.Runtime.Net;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Interpolação de entidade remota — o pedaço que transforma 20 pacotes por segundo em
/// movimento contínuo a 60 FPS. Testado direto, sem rede: é lógica pura de tempo e posição.
/// </summary>
public class NetInterpolatorTests
{
    private const float Tolerance = 0.001f;

    [Fact]
    public void SemAmostraNaoTemOQueMostrar()
    {
        var interp = new NetInterpolator();

        Assert.False(interp.HasData);
        Assert.False(interp.Sample(0f, out _, out _));
    }

    [Fact]
    public void UmaAmostraSoDevolveElaMesma()
    {
        var interp = new NetInterpolator();
        interp.Push(1f, new Vector2(10f, 20f), 0.5f);

        Assert.True(interp.Sample(1f, out var position, out float rotation));
        Assert.Equal(10f, position.X, Tolerance);
        Assert.Equal(20f, position.Y, Tolerance);
        Assert.Equal(0.5f, rotation, Tolerance);
    }

    [Fact]
    public void MeioDoCaminhoEAMediaDasDuasAmostras()
    {
        var interp = new NetInterpolator();
        interp.Push(1f, new Vector2(0f, 0f), 0f);
        interp.Push(2f, new Vector2(100f, 50f), 0f);

        Assert.True(interp.Sample(1.5f, out var position, out _));
        Assert.Equal(50f, position.X, Tolerance);
        Assert.Equal(25f, position.Y, Tolerance);
    }

    [Fact]
    public void TempoAntesDaPrimeiraAmostraSeguraAPrimeira()
    {
        var interp = new NetInterpolator();
        interp.Push(5f, new Vector2(10f, 10f), 0f);
        interp.Push(6f, new Vector2(20f, 20f), 0f);

        Assert.True(interp.Sample(0f, out var position, out _));
        Assert.Equal(10f, position.X, Tolerance);
    }

    [Fact]
    public void TempoDepoisDaUltimaAmostraCongelaEmVezDeChutar()
    {
        var interp = new NetInterpolator();
        interp.Push(1f, new Vector2(0f, 0f), 0f);
        interp.Push(2f, new Vector2(100f, 0f), 0f);

        // Rede atrasou: o certo é segurar os 100, não extrapolar pra 300 e ter que corrigir
        // com um teleporte quando o pacote chegar.
        Assert.True(interp.Sample(4f, out var position, out _));
        Assert.Equal(100f, position.X, Tolerance);
    }

    [Fact]
    public void AmostraForaDeOrdemEDescartada()
    {
        var interp = new NetInterpolator();
        interp.Push(2f, new Vector2(100f, 0f), 0f);
        interp.Push(1f, new Vector2(0f, 0f), 0f);

        Assert.True(interp.Sample(2f, out var position, out _));
        Assert.Equal(100f, position.X, Tolerance);
    }

    [Fact]
    public void AnguloInterpolaPeloCaminhoCurto()
    {
        var interp = new NetInterpolator();

        // De -10° a +10°, atravessando o zero. Pelo caminho longo daria uma volta quase
        // completa e o sprite giraria pro lado errado.
        float from = -MathF.PI / 18f;
        float to = MathF.PI / 18f;

        interp.Push(0f, Vector2.Zero, from);
        interp.Push(1f, Vector2.Zero, to);

        Assert.True(interp.Sample(0.5f, out _, out float rotation));
        Assert.Equal(0f, rotation, Tolerance);
    }

    [Fact]
    public void HistoricoCheioDescartaAsAmostrasMaisVelhas()
    {
        var interp = new NetInterpolator();

        // 12 amostras num histórico de 8: as 4 primeiras têm que sair.
        for (int i = 0; i < 12; i++)
            interp.Push(i, new Vector2(i * 10f, 0f), 0f);

        Assert.True(interp.Sample(11f, out var newest, out _));
        Assert.Equal(110f, newest.X, Tolerance);

        // Pedindo um tempo já descartado, sobra a mais antiga ainda guardada (t=4).
        Assert.True(interp.Sample(0f, out var oldest, out _));
        Assert.Equal(40f, oldest.X, Tolerance);
    }

    [Fact]
    public void ClearZeraOHistorico()
    {
        var interp = new NetInterpolator();
        interp.Push(1f, Vector2.One, 0f);
        interp.Clear();

        Assert.False(interp.HasData);
        Assert.False(interp.Sample(1f, out _, out _));
    }
}
