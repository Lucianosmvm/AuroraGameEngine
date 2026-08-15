using Aurora.Platformer;

bool smokeTest = args.Contains("--smoke");

using var game = new PlatformerGame(smokeTest);
game.ParseArgs(args);                       // --scene <caminho>, usado pelo editor
game.Run("Aurora Platformer", 1280, 720);
