extern alias runtime;

using System.Text.Json;
using Aurora.Editor.ViewModels;

namespace Aurora.Editor.Tests;

/// <summary>
/// Banco de dados de itens do editor. O arquivo que ele grava é lido pelo runtime sem
/// intermediário, então o que se prende aqui é o formato — e as duas formas de estragar um
/// banco em silêncio: id repetido e arquivo malformado sobrescrito.
/// </summary>
public sealed class ItemDatabaseViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aurora-itemdb-" + Guid.NewGuid().ToString("N"));

    private string ItemsPath => Path.Combine(_dir, "database", "items.json");

    public ItemDatabaseViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private void WriteItems(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ItemsPath)!);
        File.WriteAllText(ItemsPath, json);
    }

    [Fact]
    public void BancoInexistenteAbreVazioEmVezDeFalhar()
    {
        // Projeto novo não tem database/items.json — abrir a janela não pode explodir.
        var vm = new ItemDatabaseViewModel(ItemsPath);

        Assert.Empty(vm.Items);
    }

    [Fact]
    public void LeOsItensDoArquivo()
    {
        WriteItems("""
            { "Items": [ { "Id": "pocao", "Name": "Poção" }, { "Id": "chave" } ] }
            """);

        var vm = new ItemDatabaseViewModel(ItemsPath);

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Poção", vm.Items[0].Name);
    }

    [Fact]
    public void ItemNovoNasceComIdUnico()
    {
        // Id repetido faz o runtime enxergar só um dos dois, sem erro. Dois cliques em "+" não
        // podem produzir isso.
        var vm = new ItemDatabaseViewModel(ItemsPath);

        vm.AddCommand.Execute(null);
        vm.AddCommand.Execute(null);
        vm.AddCommand.Execute(null);

        Assert.Equal(3, vm.Items.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void SalvarGravaOFormatoQueORuntimeLe()
    {
        var vm = new ItemDatabaseViewModel(ItemsPath);
        vm.AddCommand.Execute(null);
        vm.Items[0].Id = "pocao";
        vm.Items[0].Name = "Poção Pequena";
        vm.Items[0].Consumable = true;

        vm.Save();

        // Lido de volta pelo próprio runtime: é o teste que pega qualquer divergência de nome
        // de campo entre editor e engine.
        var database = new runtime::Aurora.Runtime.Database.ItemDatabase();
        database.Load(File.ReadAllText(ItemsPath));

        Assert.Equal("Poção Pequena", database.Get("pocao")!.Name);
    }

    [Fact]
    public void SalvarRecusaIdRepetido()
    {
        var vm = new ItemDatabaseViewModel(ItemsPath);
        vm.AddCommand.Execute(null);
        vm.AddCommand.Execute(null);
        vm.Items[0].Id = "igual";
        vm.Items[1].Id = "igual";

        vm.Save();

        Assert.False(File.Exists(ItemsPath), "Gravou um banco com id repetido.");
        Assert.Contains("repetido", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BancoMalformadoNaoESobrescritoAoAbrir()
    {
        // Perder o banco inteiro por causa de uma vírgula sobrando seria irreversível.
        WriteItems("{ isso não é json }");

        var vm = new ItemDatabaseViewModel(ItemsPath);

        Assert.Empty(vm.Items);
        Assert.Contains("inválido", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{ isso não é json }", File.ReadAllText(ItemsPath));
    }

    [Fact]
    public void EfeitoDoItemUsaAMesmaListaDeAcoesDosEventos()
    {
        var vm = new ItemDatabaseViewModel(ItemsPath);
        vm.AddCommand.Execute(null);
        vm.Items[0].Id = "pocao";
        vm.Selected = vm.Items[0];

        vm.AddEffectCommand.Execute(null);
        vm.Effects[0].ActionType = "Heal";
        vm.Effects[0].ValueFloat = 50f;
        vm.Save();

        var database = new runtime::Aurora.Runtime.Database.ItemDatabase();
        database.Load(File.ReadAllText(ItemsPath));

        var effect = Assert.Single(database.Get("pocao")!.Effect);
        Assert.Equal("Heal", effect.Type);
        Assert.Equal(50f, effect.Value, 0.01f);
    }

    [Fact]
    public void RemoverTiraOItemDoArquivo()
    {
        WriteItems("""{ "Items": [ { "Id": "a" }, { "Id": "b" } ] }""");

        var vm = new ItemDatabaseViewModel(ItemsPath);
        vm.Selected = vm.Items.First(i => i.Id == "a");
        vm.RemoveCommand.Execute(null);
        vm.Save();

        using var doc = JsonDocument.Parse(File.ReadAllText(ItemsPath));
        var ids = doc.RootElement.GetProperty("Items").EnumerateArray()
            .Select(i => i.GetProperty("Id").GetString())
            .ToList();

        Assert.Equal(["b"], ids);
    }
}
