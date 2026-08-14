using System.Numerics;
using Aurora.Runtime.AI;
using Aurora.Runtime.Ecs.Components;

namespace Aurora.Runtime.Tests;

/// <summary>A* e a grade de navegação derivada do Tilemap.</summary>
public class PathfindingTests
{
    private const float Tolerance = 0.001f;

    /// <summary>Grade quadrada de tiles de 16px na origem. Cada par em <paramref name="solidos"/>
    /// vira uma célula bloqueada (tile índice 1, registrado em SolidTiles).</summary>
    private static NavGrid Grade(int lado, params (int X, int Y)[] solidos)
    {
        var map = new Tilemap { TileWidth = 16, TileHeight = 16, Width = lado, Height = lado };
        map.EnsureSize();
        map.SolidTiles.Add(1);

        foreach (var (x, y) in solidos)
            map.SetTile(x, y, 1);

        return NavGrid.FromTilemap(new Transform(Vector2.Zero), map);
    }

    private static Vector2 Centro(int x, int y) => new(x * 16f + 8f, y * 16f + 8f);

    [Fact]
    public void GradeVaziaTemCaminhoDiretoDeCantoACanto()
    {
        var grade = Grade(5);

        var caminho = AStarPathfinder.FindPath(grade, Centro(0, 0), Centro(4, 4));

        Assert.NotNull(caminho);
        Assert.Equal(Centro(0, 0), caminho[0]);
        Assert.Equal(Centro(4, 4), caminho[^1]);
        // Diagonal livre: 5 células, incluindo as duas pontas.
        Assert.Equal(5, caminho.Count);
    }

    [Fact]
    public void SemDiagonalOCaminhoUsaSoPassosOrtogonais()
    {
        var grade = Grade(5);

        var caminho = AStarPathfinder.FindPath(grade, Centro(0, 0), Centro(2, 2), allowDiagonal: false);

        Assert.NotNull(caminho);
        for (int i = 1; i < caminho.Count; i++)
        {
            var passo = caminho[i] - caminho[i - 1];
            Assert.True(MathF.Abs(passo.X) < Tolerance || MathF.Abs(passo.Y) < Tolerance,
                $"Passo {i} andou nos dois eixos ao mesmo tempo: {passo}");
        }
    }

    [Fact]
    public void OrigemIgualAoDestinoRetornaUmPontoSo()
    {
        var grade = Grade(5);

        var caminho = AStarPathfinder.FindPath(grade, Centro(2, 2), Centro(2, 2));

        Assert.NotNull(caminho);
        Assert.Equal(Centro(2, 2), Assert.Single(caminho));
    }

    [Fact]
    public void DestinoBloqueadoRetornaNull()
    {
        var grade = Grade(5, (4, 4));

        Assert.Null(AStarPathfinder.FindPath(grade, Centro(0, 0), Centro(4, 4)));
    }

    [Fact]
    public void DestinoForaDaGradeRetornaNull()
    {
        var grade = Grade(5);

        // IsBlocked trata fora dos limites como bloqueado — destino inalcançável.
        Assert.Null(AStarPathfinder.FindPath(grade, Centro(0, 0), Centro(99, 99)));
    }

    [Fact]
    public void ParedeCompletaTornaODestinoInalcancavel()
    {
        // Coluna x=2 inteira bloqueada: não há como cruzar de um lado ao outro.
        var grade = Grade(5, (2, 0), (2, 1), (2, 2), (2, 3), (2, 4));

        Assert.Null(AStarPathfinder.FindPath(grade, Centro(0, 2), Centro(4, 2)));
    }

    [Fact]
    public void CaminhoContornaParedeComPassagem()
    {
        // Mesma coluna, mas a célula (2,4) fica livre — o caminho tem que descer e passar por lá.
        var grade = Grade(5, (2, 0), (2, 1), (2, 2), (2, 3));

        var caminho = AStarPathfinder.FindPath(grade, Centro(0, 2), Centro(4, 2));

        Assert.NotNull(caminho);
        Assert.Equal(Centro(0, 2), caminho[0]);
        Assert.Equal(Centro(4, 2), caminho[^1]);
        Assert.Contains(Centro(2, 4), caminho);
    }

    [Fact]
    public void CaminhoNuncaAtravessaCelulaBloqueada()
    {
        var grade = Grade(5, (2, 0), (2, 1), (2, 2), (2, 3));

        var caminho = AStarPathfinder.FindPath(grade, Centro(0, 2), Centro(4, 2));

        Assert.NotNull(caminho);
        foreach (var ponto in caminho)
            Assert.False(grade.IsBlocked(grade.WorldToCell(ponto)), $"Caminho passou por {ponto}, que está bloqueado.");
    }

    [Fact]
    public void DiagonalNaoCortaQuinaEntreDoisBlocos()
    {
        // (1,0) e (0,1) bloqueados: ir de (0,0) até (1,1) exigiria passar "pelo vão" entre os
        // dois cantos. A regra de não cortar quina proíbe isso, e não sobra rota alternativa.
        var grade = Grade(3, (1, 0), (0, 1));

        Assert.Null(AStarPathfinder.FindPath(grade, Centro(0, 0), Centro(1, 1)));
    }

    [Fact]
    public void CadaPassoDoCaminhoEParaUmaCelulaVizinha()
    {
        var grade = Grade(6, (3, 0), (3, 1), (3, 2));

        var caminho = AStarPathfinder.FindPath(grade, Centro(0, 0), Centro(5, 5));

        Assert.NotNull(caminho);
        for (int i = 1; i < caminho.Count; i++)
        {
            var anterior = grade.WorldToCell(caminho[i - 1]);
            var atual = grade.WorldToCell(caminho[i]);
            Assert.True(Math.Abs(atual.X - anterior.X) <= 1 && Math.Abs(atual.Y - anterior.Y) <= 1,
                $"Salto de {anterior} para {atual} não é um passo de célula vizinha.");
        }
    }

    // ---- NavGrid ----

    [Fact]
    public void CelulaSolidaVemDoSolidTilesDoTilemap()
    {
        var grade = Grade(3, (1, 1));

        Assert.True(grade.IsBlocked(new GridPos(1, 1)));
        Assert.False(grade.IsBlocked(new GridPos(0, 0)));
    }

    [Fact]
    public void ForaDosLimitesContaComoBloqueado()
    {
        var grade = Grade(3);

        Assert.True(grade.IsBlocked(new GridPos(-1, 0)));
        Assert.True(grade.IsBlocked(new GridPos(0, -1)));
        Assert.True(grade.IsBlocked(new GridPos(3, 0)));
        Assert.True(grade.IsBlocked(new GridPos(0, 3)));
    }

    [Fact]
    public void ConversaoCelulaMundoRespeitaOrigemEEscala()
    {
        var map = new Tilemap { TileWidth = 16, TileHeight = 16, Width = 4, Height = 4 };
        map.EnsureSize();
        var transform = new Transform(new Vector2(100f, 50f)) { Scale = new Vector2(2f, 2f) };

        var grade = NavGrid.FromTilemap(transform, map);

        Assert.Equal(32f, grade.CellWidth, Tolerance);
        Assert.Equal(32f, grade.CellHeight, Tolerance);
        Assert.Equal(new GridPos(0, 0), grade.WorldToCell(new Vector2(100f, 50f)));
        Assert.Equal(new GridPos(1, 1), grade.WorldToCell(new Vector2(140f, 90f)));
        Assert.Equal(new Vector2(116f, 66f), grade.CellToWorld(new GridPos(0, 0)));
    }

    [Fact]
    public void CentroDaCelulaVoltaParaAMesmaCelula()
    {
        var grade = Grade(4);

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                var cell = new GridPos(x, y);
                Assert.Equal(cell, grade.WorldToCell(grade.CellToWorld(cell)));
            }
        }
    }
}
