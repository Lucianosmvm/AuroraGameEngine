using Survivors;

using var game = new SurvivorsGame();
game.ParseArgs(args);
game.Run("Aurora Survivors", 1280, 720);
