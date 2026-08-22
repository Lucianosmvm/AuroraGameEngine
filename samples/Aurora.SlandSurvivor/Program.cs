using System.Globalization;
using Aurora.SlandSurvivor;

// Argumentos:
//   --smoke              roteiro automatizado de verificação (usado pelo CI), sem teclado
//   --seed <n>           gera um mundo específico (mesma seed = mesmo mundo)
//   --time <hora>        hora inicial do relógio, 0–23 (padrão 8)
//   --depth <tiles>      começa em um poço aberto até essa profundidade
//   --zoom <n>           zoom da câmera (2 = padrão)
//   --shot <arquivo>     salva um PNG da tela e fecha
//   --shot-delay <seg>   espera antes da captura (padrão 1,5 s)
bool smokeTest = args.Contains("--smoke");

int seed = Environment.TickCount;
float startClock = 8f;
int startDepth = 0;
float zoom = 2f;
float shotDelay = 1.5f;
string? shot = null;

for (int i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--seed" when int.TryParse(args[i + 1], out int parsedSeed):
            seed = parsedSeed;
            break;
        case "--time" when float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedTime):
            startClock = parsedTime;
            break;
        case "--depth" when int.TryParse(args[i + 1], out int parsedDepth):
            startDepth = parsedDepth;
            break;
        case "--shot-delay" when float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedDelay):
            shotDelay = parsedDelay;
            break;
        case "--zoom" when float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedZoom):
            zoom = parsedZoom;
            break;
        case "--shot":
            shot = args[i + 1];
            break;
    }
}

using var game = new SurvivorGame(seed, smokeTest)
{
    StartClock = startClock,
    StartDepth = startDepth,
    Zoom = zoom,
    ScreenshotPath = shot,
    ScreenshotDelay = shotDelay,
};

game.ParseArgs(args);                       // --scene <caminho>, usado pelo editor
game.Run("Sland Survivor — Aurora", 1280, 720);
