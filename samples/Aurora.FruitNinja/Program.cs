using FruitNinja;

// 720x1280 = retrato de celular. A janela do desktop abre nesse formato só pra caber na tela;
// quem manda no enquadramento é o DesignResolution de NinjaGame, então o jogo mostra
// exatamente a mesma coisa no PC e no aparelho.
using var game = new NinjaGame();
game.ParseArgs(args);
game.Run("Aurora Ninja", 540, 960);
