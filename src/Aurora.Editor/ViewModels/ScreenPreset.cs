namespace Aurora.Editor.ViewModels;

/// <summary>
/// Um tamanho de tela com nome. Serve pros dois campos que hoje são só um par de números: a
/// resolução de referência do jogo e o aparelho da comparação no viewport.
/// </summary>
/// <param name="Label">Como aparece na lista.</param>
/// <param name="Width">Largura em pixels. 0 = "nenhum" (ver <see cref="None"/>).</param>
/// <param name="Height">Altura em pixels.</param>
public sealed record ScreenPreset(string Label, int Width, int Height)
{
    /// <summary>Item "sem comparação" da lista de aparelhos. Existe como valor em vez de null
    /// porque um ComboBox com null selecionado aparece vazio, e vazio não diz que dá pra desligar.</summary>
    public static readonly ScreenPreset None = new("— sem comparação —", 0, 0);

    /// <summary>É o que o ComboBox mostra: sem isto, a lista viria com o nome do tipo.</summary>
    public override string ToString() => Label;
}
