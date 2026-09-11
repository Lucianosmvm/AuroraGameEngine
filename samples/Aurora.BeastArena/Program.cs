using BeastArena;

// Balanceamento sem janela: dotnet run --project samples/Aurora.BeastArena -- --simular 200
int simular = Array.IndexOf(args, "--simular");
if (simular >= 0)
{
    Simulador.Rodar(simular + 1 < args.Length && int.TryParse(args[simular + 1], out int n) ? n : 100);
    return;
}

// 720x1280 = retrato de celular. A janela do desktop abre menor só pra caber no monitor; o
// enquadramento de verdade é o DesignResolution do BeastArenaGame.
using var game = new BeastArenaGame();
game.ParseArgs(args);
game.Run("Beast Arena", 540, 960);
