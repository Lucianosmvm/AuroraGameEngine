using System.Text.Json.Nodes;
using Aurora.Editor.ViewModels;

namespace Aurora.Editor.Tests;

/// <summary>
/// Duplicar/copiar/colar entidade. Montar fase sem isto era criar entidade, reanexar
/// componente e reconfigurar campo um por um. O que estes testes travam é o que costuma
/// quebrar numa cópia: nome repetido (o runtime acha entidade por nome), cópia rasa (mexer
/// na cópia mexia no original) e o passo de undo.
/// </summary>
public class EntityClipboardTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aurora-clip-" + Guid.NewGuid().ToString("N"));
    private readonly MainViewModel _vm = new();

    public EntityClipboardTests()
    {
        Directory.CreateDirectory(_dir);
        string scene = Path.Combine(_dir, "fase.json");
        File.WriteAllText(scene, """
            {
              "Scene": "fase",
              "Objects": [
                {
                  "Name": "Plataforma3",
                  "Components": [
                    { "Type": "Transform", "X": 100, "Y": 200 },
                    { "Type": "SpriteRenderer", "Texture": "sprites/chao.png" }
                  ]
                }
              ]
            }
            """);
        _vm.OpenScene(scene);
    }

    private EntityViewModel Original => _vm.Entities.Single(e => e.Name == "Plataforma3");

    [Fact]
    public void Duplicate_NamesTheCopyByIncrementingTheTrailingNumber()
    {
        _vm.SelectedEntity = Original;
        _vm.DuplicateSelectedEntity();

        // "Plataforma3" -> "Plataforma4", não "Plataforma31".
        Assert.Equal(2, _vm.Entities.Count);
        Assert.Contains(_vm.Entities, e => e.Name == "Plataforma4");
    }

    [Fact]
    public void Duplicate_SelectsTheCopy_SoDragAndDropMovesTheNewOne()
    {
        _vm.SelectedEntity = Original;
        _vm.DuplicateSelectedEntity();

        Assert.Equal("Plataforma4", _vm.SelectedEntity!.Name);
    }

    [Fact]
    public void Duplicate_IsDeep_SoEditingTheCopyLeavesTheOriginalAlone()
    {
        _vm.SelectedEntity = Original;
        _vm.DuplicateSelectedEntity();

        var copy = _vm.SelectedEntity!.Node;
        copy["Components"]!.AsArray()[0]!["X"] = 999;

        Assert.Equal(100, Original.Node["Components"]!.AsArray()[0]!["X"]!.GetValue<int>());
    }

    [Fact]
    public void Duplicate_KeepsComponents()
    {
        _vm.SelectedEntity = Original;
        _vm.DuplicateSelectedEntity();

        var components = _vm.SelectedEntity!.Node["Components"]!.AsArray();
        Assert.Equal(2, components.Count);
        Assert.Equal("sprites/chao.png", components[1]!["Texture"]!.GetValue<string>());
    }

    [Fact]
    public void Duplicate_IsUndoable_AsASingleStep()
    {
        _vm.SelectedEntity = Original;
        _vm.DuplicateSelectedEntity();
        Assert.Equal(2, _vm.Entities.Count);

        _vm.Undo();

        Assert.Single(_vm.Entities);
    }

    [Fact]
    public void Copy_ThenPaste_AddsOneCopy()
    {
        _vm.SelectedEntity = Original;
        _vm.CopySelectedEntity();
        _vm.PasteEntity();

        Assert.Equal(2, _vm.Entities.Count);
        Assert.Equal("Plataforma4", _vm.SelectedEntity!.Name);
    }

    [Fact]
    public void Paste_CanRepeat_AndEveryNameStaysUnique()
    {
        _vm.SelectedEntity = Original;
        _vm.CopySelectedEntity();
        _vm.PasteEntity();
        _vm.PasteEntity();
        _vm.PasteEntity();

        var names = _vm.Entities.Select(e => e.Name).ToList();
        Assert.Equal(4, names.Count);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Paste_SurvivesSceneChange_SoAnObjectCanMoveBetweenLevels()
    {
        _vm.SelectedEntity = Original;
        _vm.CopySelectedEntity();

        string other = Path.Combine(_dir, "fase2.json");
        File.WriteAllText(other, """{ "Scene": "fase2", "Objects": [] }""");
        _vm.OpenScene(other);

        Assert.True(_vm.CanPaste);
        _vm.PasteEntity();

        // Cena vazia: o nome original está livre, então nada de sufixo.
        Assert.Equal("Plataforma3", Assert.Single(_vm.Entities).Name);
    }

    [Fact]
    public void CopiedEntity_IsASnapshot_NotALiveReference()
    {
        _vm.SelectedEntity = Original;
        _vm.CopySelectedEntity();

        Original.Node["Components"]!.AsArray()[0]!["X"] = 777;
        _vm.PasteEntity();

        Assert.Equal(100, _vm.SelectedEntity!.Node["Components"]!.AsArray()[0]!["X"]!.GetValue<int>());
    }

    [Fact]
    public void NothingSelected_DuplicateAndCopyAreNoOps()
    {
        _vm.SelectedEntity = null;

        _vm.DuplicateSelectedEntity();
        _vm.CopySelectedEntity();

        Assert.Single(_vm.Entities);
        Assert.False(_vm.CanPaste);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* melhor esforço */ }
    }
}
