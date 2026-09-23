using Bichinhos;

// 720x1280 = retrato de celular. A janela do desktop abre menor só pra caber no monitor; quem
// manda no enquadramento é o DesignResolution, então PC e aparelho mostram a mesma coisa.
//
//   --rapido             relógio do bicho 60x mais rápido (fome e cocô sem esperar)
//   --save <pasta>       save em outra pasta (testar sem mexer no bicho de verdade)
//   --demo <tela>        abre direto em casa/batalha/brincar/evolucao/escolha com um bicho pronto
//   --foto <arquivo.png> grava a tela depois de ~1,5 s e fecha
using var game = new BichinhosGame();
game.LerArgumentos(args);
game.Run("Bichinhos", 540, 960);
