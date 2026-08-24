namespace Aurora.Editor.ViewModels;

/// <summary>
/// A janela "Banco de Dados" inteira: uma aba por catálogo. Cada aba tem o seu arquivo e o seu
/// próprio estado de alteração — Salvar grava as duas, e cada uma relata o que fez.
///
/// <para>Só existem catálogos que o runtime carrega sozinho no boot. Inimigo e objeto de cena
/// continuam sendo prefab, e as tabelas de spawn é que agrupam prefabs por id — o banco não
/// duplica a lista de prefabs, ele aponta pra ela.</para>
/// </summary>
public sealed class DatabaseViewModel : ViewModelBase
{
    public ItemDatabaseViewModel Items { get; }
    public SpawnTableEditorViewModel Spawns { get; }
    public CommonEventEditorViewModel CommonEvents { get; }
    public StatusEditorViewModel Status { get; }

    public DatabaseViewModel(MainViewModel owner)
    {
        Items = new ItemDatabaseViewModel(owner.ItemDatabasePath, owner);
        Spawns = new SpawnTableEditorViewModel(owner.SpawnTablePath, owner);
        CommonEvents = new CommonEventEditorViewModel(owner.CommonEventPath, owner);
        Status = new StatusEditorViewModel(owner.StatusPath);
    }

    /// <summary>Grava as duas abas. Se uma recusar (id repetido, por exemplo), a outra ainda é
    /// gravada — cada uma diz no próprio status o que aconteceu.</summary>
    public void Save()
    {
        Items.Save();
        Spawns.Save();
        CommonEvents.Save();
        Status.Save();
    }
}
