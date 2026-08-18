using Aurora.Coop;

// Sem argumentos: abre no menu (HOSPEDAR / PROCURAR PARTIDAS).
//
// Teste rapido de duas janelas na mesma maquina, sem clicar em nada:
//   dotnet run --project samples/Aurora.Coop -- --host
//   dotnet run --project samples/Aurora.Coop -- --join 127.0.0.1
bool autoHost = args.Contains("--host");
bool bot = args.Contains("--bot");
string? autoJoin = null;
float autoExit = 0f;

for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--join") autoJoin = args[i + 1];
    else if (args[i] == "--seconds" && float.TryParse(args[i + 1], out float s)) autoExit = s;
}

using var game = new CoopGame(autoHost, autoJoin, autoExit, bot);
game.Run("Aurora Coop", 1280, 720);
