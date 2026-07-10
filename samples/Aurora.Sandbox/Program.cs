using Aurora.Sandbox;

bool smokeTest = args.Contains("--smoke");

using var game = new SandboxGame(smokeTest);
game.Run("Aurora Sandbox", 1280, 720);
