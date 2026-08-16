namespace Aurora.Editor.ViewModels;

/// <summary>Um arquivo .cs encontrado na pasta Scripts do projeto — item do painel SCRIPTS.</summary>
public sealed class ScriptFileViewModel
{
    /// <summary>Caminho absoluto do arquivo em disco.</summary>
    public string FullPath { get; }

    /// <summary>Caminho relativo à pasta Scripts, com '/'.</summary>
    public string RelativePath { get; }

    public string Name => System.IO.Path.GetFileNameWithoutExtension(RelativePath);

    public ScriptFileViewModel(string fullPath, string relativePath)
    {
        FullPath = fullPath;
        RelativePath = relativePath;
    }
}
