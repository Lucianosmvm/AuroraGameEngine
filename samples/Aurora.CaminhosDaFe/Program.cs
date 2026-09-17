using CaminhosDaFe;

using var game = new CaminhosGame();
game.ParseArgs(args);

// --toque: controles de celular no PC, com o mouse fazendo o papel do dedo.
if (args.Contains("--toque"))
    game.ModoToque = true;

game.Run("Caminhos da Fé — Davi", 1280, 720);
