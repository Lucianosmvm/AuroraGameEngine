using Aurora.Editor.ViewModels;

namespace Aurora.Editor.Tests;

/// <summary>
/// Hierarquia no editor. O que se prende aqui é o viewport contar a mesma história que o jogo:
/// arrastar o pai leva os filhos pelo mesmo deslocamento (World.UpdateHierarchy faz igual no
/// runtime). Se divergir, a pessoa monta no editor e descobre no Play que está tudo torto.
/// </summary>
public class HierarchyEditingTests : IDisposable
{
    private const float Tolerance = 0.001f;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aurora-hier-" + Guid.NewGuid().ToString("N"));
    private readonly MainViewModel _vm = new();

    public HierarchyEditingTests()
    {
        Directory.CreateDirectory(_dir);
        string scene = Path.Combine(_dir, "fase.json");
        File.WriteAllText(scene, """
            {
              "Scene": "fase",
              "Objects": [
                { "Name": "Player",  "Components": [{ "Type": "Transform", "X": 100, "Y": 100 }] },
                { "Name": "Arma",    "Components": [{ "Type": "Transform", "X": 120, "Y": 100, "Parent": "Player" }] },
                { "Name": "Mira",    "Components": [{ "Type": "Transform", "X": 140, "Y": 100, "Parent": "Arma" }] },
                { "Name": "Pedra",   "Components": [{ "Type": "Transform", "X": 500, "Y": 500 }] }
              ]
            }
            """);
        _vm.OpenScene(scene);
    }

    private EntityViewModel Entity(string name) => _vm.Entities.Single(e => e.Name == name);

    private (float X, float Y) PositionOf(string name)
    {
        var transform = Entity(name).Transform!;
        return (transform.GetFloat("X", 0f), transform.GetFloat("Y", 0f));
    }

    [Fact]
    public void ParentName_IsReadFromTheTransform()
    {
        Assert.Equal("Player", Entity("Arma").ParentName);
        Assert.True(Entity("Arma").HasParent);
        Assert.Null(Entity("Pedra").ParentName);
        Assert.False(Entity("Pedra").HasParent);
    }

    [Fact]
    public void DraggingTheParent_CarriesTheChild_ByTheSameDelta()
    {
        _vm.MoveEntityWithChildren(Entity("Player"), 200, 150);

        Assert.Equal((200f, 150f), PositionOf("Player"));

        // Offset (20, 0) preservado — não reposicionado em cima do pai.
        var arma = PositionOf("Arma");
        Assert.Equal(220, arma.X, Tolerance);
        Assert.Equal(150, arma.Y, Tolerance);
    }

    [Fact]
    public void DraggingTheParent_ReachesGrandchildren()
    {
        _vm.MoveEntityWithChildren(Entity("Player"), 200, 100);

        var mira = PositionOf("Mira");
        Assert.Equal(240, mira.X, Tolerance);
    }

    [Fact]
    public void DraggingTheParent_LeavesUnrelatedEntitiesAlone()
    {
        _vm.MoveEntityWithChildren(Entity("Player"), 999, 999);

        Assert.Equal((500f, 500f), PositionOf("Pedra"));
    }

    [Fact]
    public void DraggingAChild_DoesNotMoveTheParent()
    {
        _vm.MoveEntityWithChildren(Entity("Arma"), 130, 100);

        Assert.Equal((100f, 100f), PositionOf("Player"));
        Assert.Equal(130, PositionOf("Arma").X, Tolerance);
    }

    [Fact]
    public void DraggingAChild_StillCarriesItsOwnChildren()
    {
        _vm.MoveEntityWithChildren(Entity("Arma"), 130, 100);

        Assert.Equal(150, PositionOf("Mira").X, Tolerance);
    }

    [Fact]
    public void TheWholeDrag_IsASingleUndoStep()
    {
        // Cada filho move com a tag do PAI. Com a tag de cada um, as edições se alternariam e
        // um arrasto viraria dezenas de passos de undo.
        _vm.MoveEntityWithChildren(Entity("Player"), 200, 150);
        _vm.Undo();

        Assert.Equal((100f, 100f), PositionOf("Player"));
        Assert.Equal(120, PositionOf("Arma").X, Tolerance);
        Assert.Equal(140, PositionOf("Mira").X, Tolerance);
    }

    [Fact]
    public void CircularParenting_DoesNotHangTheEditor()
    {
        string scene = Path.Combine(_dir, "ciclo.json");
        File.WriteAllText(scene, """
            {
              "Scene": "ciclo",
              "Objects": [
                { "Name": "A", "Components": [{ "Type": "Transform", "X": 0, "Y": 0, "Parent": "B" }] },
                { "Name": "B", "Components": [{ "Type": "Transform", "X": 10, "Y": 0, "Parent": "A" }] }
              ]
            }
            """);
        _vm.OpenScene(scene);

        _vm.MoveEntityWithChildren(_vm.Entities.Single(e => e.Name == "A"), 50, 0);

        // O que se garante é que retorna. Descendants precisa parar no nó já visitado.
        Assert.Equal(2, _vm.Entities.Count);
    }

    [Fact]
    public void Descendants_DoesNotIncludeTheRootItself()
    {
        var descendants = _vm.Descendants(Entity("Player")).Select(e => e.Name).ToList();

        Assert.Equal(["Arma", "Mira"], descendants);
    }

    [Fact]
    public void DuplicatingAChild_KeepsPointingToTheSameParent()
    {
        _vm.SelectedEntity = Entity("Arma");
        _vm.DuplicateSelectedEntity();

        Assert.Equal("Player", _vm.SelectedEntity!.ParentName);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* melhor esforço */ }
    }
}
