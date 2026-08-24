using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Pai/filho: o filho é levado junto quando o pai anda ou gira. Transform.Position continua
/// sendo MUNDO (a engine inteira lê assim), então o que se prende aqui é o comportamento do
/// empurrão — e principalmente as saídas sujas, que é onde hierarquia costuma teleportar
/// objeto pra longe: primeiro frame, pai que morre, corrente de três, ciclo.
/// </summary>
public class HierarchyTests
{
    private const float Tolerance = 0.001f;

    private static (World World, Transform Parent, Transform Child) BuildPair(
        Vector2 parentAt, Vector2 childAt, bool inheritRotation = true)
    {
        var world = new World();

        var parent = new Transform(parentAt);
        world.CreateEntity("Pai").Add(parent);

        var child = new Transform(childAt) { Parent = "Pai", InheritRotation = inheritRotation };
        world.CreateEntity("Filho").Add(child);

        return (world, parent, child);
    }

    private static void Advance(World world, int frames = 1)
    {
        for (int i = 0; i < frames; i++)
            world.Update(1f / 60f);
    }

    [Fact]
    public void FirstFrame_DoesNotYankTheChildOntoTheParent()
    {
        // O filho tem que ficar onde a cena o colocou. Se o primeiro frame "resolvesse" a
        // posição a partir do pai, todo filho pularia pra cima dele ao abrir a fase.
        var (world, _, child) = BuildPair(parentAt: new Vector2(100, 0), childAt: new Vector2(140, 10));

        Advance(world);

        Assert.Equal(new Vector2(140, 10), child.Position);
    }

    [Fact]
    public void MovingTheParent_CarriesTheChild_KeepingTheOffset()
    {
        var (world, parent, child) = BuildPair(new Vector2(100, 0), new Vector2(140, 10));
        Advance(world);

        parent.Position = new Vector2(200, 50);
        Advance(world);

        // Offset original era (40, 10).
        Assert.Equal(240, child.Position.X, Tolerance);
        Assert.Equal(60, child.Position.Y, Tolerance);
    }

    [Fact]
    public void MovingTheChildDirectly_IsNotOverwritten_AndBecomesTheNewOffset()
    {
        // Empurrão de colisão, tween, script: quem mexeu no filho por fora tem razão. Recalcular
        // a posição a partir do pai desfaria isso todo frame.
        var (world, parent, child) = BuildPair(new Vector2(0, 0), new Vector2(10, 0));
        Advance(world);

        child.Position = new Vector2(30, 0);
        Advance(world);
        Assert.Equal(30, child.Position.X, Tolerance);

        parent.Position = new Vector2(5, 0);
        Advance(world);

        Assert.Equal(35, child.Position.X, Tolerance);
    }

    [Fact]
    public void RotatingTheParent_OrbitsTheChildAroundIt()
    {
        // Arma na mão: girar o dono tem que levar a arma pro outro lado, não só girá-la no lugar.
        var (world, parent, child) = BuildPair(new Vector2(0, 0), new Vector2(10, 0));
        Advance(world);

        parent.Rotation = MathF.PI / 2f;   // 90°
        Advance(world);

        Assert.Equal(0, child.Position.X, 0.001);
        Assert.Equal(10, child.Position.Y, 0.001);
        Assert.Equal(MathF.PI / 2f, child.Rotation, Tolerance);
    }

    [Fact]
    public void InheritRotationOff_KeepsTheChildUpright_AndInPlace()
    {
        // Barra de vida sobre o inimigo: acompanha a posição, nunca deita.
        var (world, parent, child) = BuildPair(new Vector2(0, 0), new Vector2(10, 0), inheritRotation: false);
        Advance(world);

        parent.Rotation = MathF.PI / 2f;
        Advance(world);

        Assert.Equal(10, child.Position.X, Tolerance);
        Assert.Equal(0, child.Position.Y, Tolerance);
        Assert.Equal(0, child.Rotation, Tolerance);
    }

    [Fact]
    public void ThreeLevelChain_MovesInOneFrame_NotOneLinkPerFrame()
    {
        var world = new World();

        var avo = new Transform(new Vector2(0, 0));
        world.CreateEntity("Avo").Add(avo);
        var pai = new Transform(new Vector2(10, 0)) { Parent = "Avo" };
        world.CreateEntity("Pai").Add(pai);
        var neto = new Transform(new Vector2(20, 0)) { Parent = "Pai" };
        world.CreateEntity("Neto").Add(neto);

        Advance(world);
        avo.Position = new Vector2(100, 0);
        Advance(world);

        // Sem ordenar por profundidade, o neto só se moveria no frame seguinte.
        Assert.Equal(110, pai.Position.X, Tolerance);
        Assert.Equal(120, neto.Position.X, Tolerance);
    }

    [Fact]
    public void ParentThatDies_LeavesTheChildWhereItIs()
    {
        var (world, parent, child) = BuildPair(new Vector2(0, 0), new Vector2(10, 0));
        Advance(world);

        parent.Position = new Vector2(50, 0);
        Advance(world);
        Assert.Equal(60, child.Position.X, Tolerance);

        world.TryFind("Pai", out var parentEntity);
        world.Destroy(parentEntity);
        Advance(world, 5);

        Assert.Equal(60, child.Position.X, Tolerance);
    }

    [Fact]
    public void ParentAppearingLater_DoesNotTeleportTheChild()
    {
        // O filho existe antes do pai (ordem da cena, ou pai criado por spawner). Quando o pai
        // aparece, o filho não pode receber de uma vez todo o caminho que o pai já andou.
        var world = new World();
        var child = new Transform(new Vector2(10, 0)) { Parent = "Pai" };
        world.CreateEntity("Filho").Add(child);

        Advance(world, 3);
        Assert.Equal(10, child.Position.X, Tolerance);

        var parent = new Transform(new Vector2(500, 0));
        world.CreateEntity("Pai").Add(parent);
        Advance(world);

        Assert.Equal(10, child.Position.X, Tolerance);

        parent.Position = new Vector2(510, 0);
        Advance(world);
        Assert.Equal(20, child.Position.X, Tolerance);
    }

    [Fact]
    public void MissingParentName_IsIgnoredInsteadOfCrashing()
    {
        var world = new World();
        var child = new Transform(new Vector2(7, 3)) { Parent = "NaoExiste" };
        world.CreateEntity("Filho").Add(child);

        Advance(world, 10);

        Assert.Equal(new Vector2(7, 3), child.Position);
    }

    [Fact]
    public void CircularParenting_DoesNotHangTheGame()
    {
        var world = new World();
        var a = new Transform(new Vector2(0, 0)) { Parent = "B" };
        world.CreateEntity("A").Add(a);
        var b = new Transform(new Vector2(10, 0)) { Parent = "A" };
        world.CreateEntity("B").Add(b);

        // O que se garante é que Update RETORNA. Posição num ciclo não tem resposta certa.
        Advance(world, 5);

        Assert.True(true);
    }

    [Fact]
    public void TeleportWithChildren_CarriesTheWholeChain()
    {
        // O caminho de QUALQUER salto que não seja andar: carregar save, marcador de spawn,
        // ação Teleport. Escrever Position direto move só o alvo e separa a corrente pra sempre.
        var world = new World();
        var pai = new Transform(new Vector2(0, 0));
        var paiEntity = world.CreateEntity("Pai");
        paiEntity.Add(pai);
        var filho = new Transform(new Vector2(10, 0)) { Parent = "Pai" };
        world.CreateEntity("Filho").Add(filho);
        var neto = new Transform(new Vector2(20, 0)) { Parent = "Filho" };
        world.CreateEntity("Neto").Add(neto);

        world.TeleportWithChildren(paiEntity, new Vector2(1000, 0));

        Assert.Equal(1000, pai.Position.X, Tolerance);
        Assert.Equal(1010, filho.Position.X, Tolerance);
        Assert.Equal(1020, neto.Position.X, Tolerance);
    }

    [Fact]
    public void TeleportWithChildren_LeavesUnrelatedEntitiesAlone()
    {
        var world = new World();
        var paiEntity = world.CreateEntity("Pai");
        paiEntity.Add(new Transform(new Vector2(0, 0)));
        var estranho = new Transform(new Vector2(77, 77));
        world.CreateEntity("Estranho").Add(estranho);

        world.TeleportWithChildren(paiEntity, new Vector2(1000, 0));

        Assert.Equal(new Vector2(77, 77), estranho.Position);
    }

    [Fact]
    public void TeleportWithChildren_OnACycle_DoesNotHang()
    {
        var world = new World();
        var a = world.CreateEntity("A");
        a.Add(new Transform(new Vector2(0, 0)) { Parent = "B" });
        world.CreateEntity("B").Add(new Transform(new Vector2(10, 0)) { Parent = "A" });

        world.TeleportWithChildren(a, new Vector2(50, 0));

        Assert.True(true);
    }

    [Fact]
    public void EntityWithoutParent_IsUntouched()
    {
        var world = new World();
        var solto = new Transform(new Vector2(42, 42));
        world.CreateEntity("Solto").Add(solto);

        Advance(world, 10);

        Assert.Equal(new Vector2(42, 42), solto.Position);
    }

    [Fact]
    public void TwoChildrenOfTheSameParent_BothFollow()
    {
        var world = new World();
        var parent = new Transform(new Vector2(0, 0));
        world.CreateEntity("Pai").Add(parent);
        var esquerda = new Transform(new Vector2(-10, 0)) { Parent = "Pai" };
        world.CreateEntity("Esquerda").Add(esquerda);
        var direita = new Transform(new Vector2(10, 0)) { Parent = "Pai" };
        world.CreateEntity("Direita").Add(direita);

        Advance(world);
        parent.Position = new Vector2(0, 100);
        Advance(world);

        Assert.Equal(100, esquerda.Position.Y, Tolerance);
        Assert.Equal(100, direita.Position.Y, Tolerance);
    }
}
