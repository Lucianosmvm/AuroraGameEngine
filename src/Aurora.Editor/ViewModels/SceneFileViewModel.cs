namespace Aurora.Editor.ViewModels;

/// <summary>Uma cena .json encontrada na pasta de assets do projeto — item do painel CENAS.</summary>
public sealed class SceneFileViewModel : ViewModelBase
{
    public string FullPath { get; }
    public string Name { get; }

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (Set(ref _isCurrent, value))
                Raise(nameof(DisplayName));
        }
    }

    /// <summary>Marca a cena aberta no momento sem precisar de converter/estilo extra na view.</summary>
    public string DisplayName => IsCurrent ? $"▶ {Name}" : Name;

    public SceneFileViewModel(string fullPath, string name)
    {
        FullPath = fullPath;
        Name = name;
    }
}
