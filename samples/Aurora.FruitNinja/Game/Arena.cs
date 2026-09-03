namespace FruitNinja;

/// <summary>
/// As medidas do campo de jogo, num lugar só.
///
/// <para>A câmera do Aurora fica parada em (0,0) com zoom 1 neste jogo — não há
/// <c>CameraController</c> na cena —, então o mundo visível é exatamente a resolução de
/// referência centrada na origem: X de -360 a 360, Y de -640 a 640, com Y CRESCENDO PARA
/// BAIXO (a projeção da engine é de tela, canto superior esquerdo na origem). Por isso a
/// gravidade aqui é positiva e a fruta é lançada com velocidade Y negativa.</para>
/// </summary>
public static class Arena
{
    /// <summary>Resolução de referência. Mudar aqui muda o enquadramento do jogo inteiro,
    /// inclusive no Android — o resto do código lê estas medidas, não números soltos.</summary>
    public const int Largura = 720;
    public const int Altura = 1280;

    public static float Esquerda => -Largura / 2f;
    public static float Direita => Largura / 2f;
    public static float Topo => -Altura / 2f;
    public static float Base => Altura / 2f;

    /// <summary>Altura em que a fruta nasce: abaixo da borda de baixo, fora de vista, pra ela
    /// entrar em cena já subindo em vez de "aparecer do nada" no meio da tela.</summary>
    public const float AlturaDeLancamento = 780f;

    /// <summary>Passou disto pra baixo, a fruta saiu de vez — é o que conta como escapada.
    /// Precisa de folga sobre <see cref="Base"/>: uma fruta cortada rente à borda de baixo
    /// ainda tem que poder cair fora da tela antes de sumir.</summary>
    public const float LimiteDeSaida = 900f;

    /// <summary>Aceleração da gravidade em pixels/s². Sobe junto com a altura da tela: é o par
    /// dela que decide quanto tempo a fruta fica no ar.</summary>
    public const float Gravidade = 1100f;
}
