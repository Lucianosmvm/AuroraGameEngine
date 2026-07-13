namespace Aurora.Editor.ViewModels;

/// <summary>Um arquivo de prefab (.json com Name+Components) encontrado no projeto — item do painel PREFABS.</summary>
public sealed class PrefabFileViewModel
{
    /// <summary>Caminho absoluto do arquivo em disco.</summary>
    public string FullPath { get; }

    /// <summary>Caminho relativo à raiz de assets, com '/' — o formato salvo no campo "Prefab" da cena.</summary>
    public string RelativePath { get; }

    public string Name => System.IO.Path.GetFileNameWithoutExtension(RelativePath);

    public PrefabFileViewModel(string fullPath, string relativePath)
    {
        FullPath = fullPath;
        RelativePath = relativePath;
    }
}
