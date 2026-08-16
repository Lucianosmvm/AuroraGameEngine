namespace Aurora.Runtime.UI;

/// <summary>
/// A regra de <c>AnchorX</c>/<c>AnchorY</c>: converte o X/Y guardado no JSON pra borda do elemento
/// na tela, e de volta.
///
/// <para>Fica num arquivo próprio, sem depender de nada, porque o editor precisa da MESMA regra pra
/// desenhar o preview — e ele não referencia o Aurora.Runtime de propósito (trabalha só em cima do
/// JSON da cena, sem contexto de GL). O Aurora.Editor.csproj inclui este arquivo por
/// <c>&lt;Compile Include=... Link=...&gt;</c>, então é literalmente o mesmo código nos dois lados.
/// Enquanto era regra copiada, editor e runtime divergiram sem ninguém perceber.</para>
/// </summary>
public static class UiAnchor
{
    /// <summary>X/Y do JSON → coordenada da borda (esquerda ou topo) em pixel de tela.
    /// <c>Center</c>/<c>Right</c>/<c>Bottom</c> tornam a posição independente da resolução;
    /// <c>Left</c>/<c>Top</c> (padrão) são pixel absoluto.</summary>
    public static float Resolve(string anchor, float coordinate, float screenSize, float elementSize)
        => anchor switch
        {
            "Center" => screenSize / 2f + coordinate - elementSize / 2f,
            "Right" or "Bottom" => screenSize - coordinate - elementSize,
            _ => coordinate, // "Left"/"Top"
        };

    /// <summary>Inverso de <see cref="Resolve"/>: borda na tela → X/Y pra gravar no JSON. É o que
    /// o editor usa ao arrastar um elemento — com <c>Right</c>/<c>Bottom</c> o X cresce para o
    /// lado contrário, então somar o movimento do mouse direto no X moveria ao contrário.</summary>
    public static float Unresolve(string anchor, float edge, float screenSize, float elementSize)
        => anchor switch
        {
            "Center" => edge - screenSize / 2f + elementSize / 2f,
            "Right" or "Bottom" => screenSize - edge - elementSize,
            _ => edge,
        };
}
