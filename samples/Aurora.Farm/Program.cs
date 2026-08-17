using AuroraFarm;

bool smokeTest = args.Contains("--smoke");

using var game = new FarmGame(smokeTest);
game.Run("Aurora Farm", 1280, 720);
