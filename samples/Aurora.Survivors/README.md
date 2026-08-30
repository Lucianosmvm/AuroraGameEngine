# Aurora Survivors — base de um jogo estilo Vampire Survivors

Projeto jogável do zero em cima da Aurora Engine: menu, loja permanente, partida com ondas de
inimigos, arma automática, level up com escolha de melhoria, pausa e tela de derrota.
A ideia é ser **base** — cada sistema é pequeno, comentado e feito pra você acrescentar em cima.

## Rodar

```bash
dotnet run --project samples/Aurora.Survivors
```

Cai no menu. Pra entrar direto numa partida (útil no dia a dia):

```bash
dotnet run --project samples/Aurora.Survivors -- --scene scenes/arena.json
```

No editor: **Arquivo → Abrir Projeto…** e escolha esta pasta (o `aurora.project.json` já aponta
pro `.csproj`; se você mover o projeto, corrija o campo `gameProject`). Com `arena.json` aberta,
o **▶ Play** entra direto na partida.

## Controles

| | |
|---|---|
| Andar | WASD / setas / analógico esquerdo / joystick de toque (canto inferior esquerdo) |
| Atacar | automático — a arma mira sozinha no inimigo mais próximo |
| Pausar | `ESC` |
| Menus | mouse ou toque |

## O que já está montado

- **Menu** (`MainMenu`) → Jogar / Loja / Sair.
- **Loja permanente**: 4 melhorias compradas com `Moeda`, que valem em TODAS as partidas. O
  progresso (moedas + níveis comprados) fica no save do slot 0, gravado ao comprar, ao morrer e
  ao voltar pro menu.
- **Partida**: arena de 2048×2048 com o jogador no centro, câmera seguindo, inimigos nascendo em
  volta fora da tela, cada vez mais rápido e mais forte.
- **Armas**: tiro automático que mira no inimigo mais próximo, e lâminas orbitais (desbloqueadas
  por melhoria de level up).
- **Level up**: matar inimigo larga gema de XP; encheu a barra, o jogo congela e você escolhe uma
  entre 3 melhorias sorteadas.
- **HUD**: vida, XP, nível, tempo, mortes e moedas.
- **Derrota**: resumo da partida (tempo, nível, mortes, moedas ganhas) e jogar de novo / menu.

## Mapa dos arquivos

```
SurvivorsGame.cs         fluxo de telas: menu → loja → partida → level up → pausa → derrota
Game/RunManager.cs       estado de UMA partida: tempo, nível, curva de XP, sorteio de melhoria
Game/UpgradeCatalog.cs   catálogo das melhorias de level up  ← acrescente aqui
Game/MetaShop.cs         itens da loja permanente e seus bônus  ← acrescente aqui
Game/Alvos.cs            "qual é o inimigo mais próximo" (usado pelas armas)

Scripts/PlayerStats.cs      ficha de atributos do jogador — a fonte da verdade de tudo
Scripts/PlayerRunner.cs     aplica a ficha nos componentes, regenera, HUD, limite da arena
Scripts/WeaponAutoShoot.cs  arma automática — o molde pra uma arma nova
Scripts/OrbitBlade.cs       arma orbital (lâminas girando)
Scripts/EnemyChaser.cs      inimigo que persegue e larga loot ao morrer
Scripts/EnemySpawner.cs     ritmo de spawn e curva de dificuldade
Scripts/Pickup.cs           gema de XP e moeda (ímã + coleta)

Assets/scenes/arena.json    a fase: chão, jogador, diretor de spawn, câmera
Assets/scenes/menu.json     cena de fundo do menu
Assets/scenes/*.json        telas de UI (MainMenu, Loja, Hud, LevelUp, GameOver, Pausa)
Assets/prefabs/*.json       morcego, tiro, lâmina, gema, moeda
Assets/sprites/*.png        arte placeholder gerada em código — troque à vontade
```

## Como acrescentar

### Um inimigo novo

1. Copie `Assets/prefabs/morcego.json` pra `Assets/prefabs/seu_bicho.json`, troque sprite, vida,
   dano e velocidade. **Mantenha o componente `Tags` com `"inimigo"`**: é por essa etiqueta que
   as armas miram e o spawner conta — sem ela o bicho é invencível e invisível pro resto do jogo.
2. Pra ele nascer, duplique a entidade `Diretor` em `arena.json` com outro `Prefab` e um
   `StartAfterSeconds` maior (ex.: só aparece depois de 2 minutos).
3. Se ele precisar de comportamento diferente (atirar, fugir, explodir ao morrer), copie
   `Scripts/EnemyChaser.cs` e mude o `Update`. Todo script marcado `[SceneScript]` aparece
   sozinho no "+Add Componente" do editor.

Alternativa sem duplicar spawner: cadastre uma **tabela de spawn** em
`Assets/database/spawns.json` e ponha o id dela no campo `Prefab` do spawner — a engine sorteia
por peso e condição sozinha.

### Uma melhoria de level up

Um item novo em `Game/UpgradeCatalog.cs`. O sorteio, a tela e a contagem de nível já funcionam:

```csharp
new()
{
    Id = "critico", Nome = "Golpe Crítico", Descricao = "+10% de dano crítico",
    MaxNivel = 5, Aplicar = s => s.CritChance += 0.10f,
},
```

Se o efeito precisar de um atributo que ainda não existe, crie o campo em
`Scripts/PlayerStats.cs` e leia ele onde importa (na arma, no `PlayerRunner`, no inimigo).

### Uma arma nova

Copie `Scripts/WeaponAutoShoot.cs`, mude a mira/o prefab, e adicione o componente novo na
entidade `Player` de `arena.json`. Pra ela começar desligada e ser desbloqueada por melhoria, use
o padrão do `OrbitBlade`: um campo em `PlayerStats` (ex.: `OrbitBlades`) que o script consulta —
zero significa arma dormindo.

### Um item na loja

Um `MetaItem` novo em `Game/MetaShop.Itens` e o efeito dele em `MetaShop.AplicarEm`. O `Id` é o
nome da variável do `GameState` que guarda o nível comprado — como o save da engine grava o
GameState inteiro, o progresso persiste sem escrever nenhum código de save. A tela mostra 4
linhas; pra mais, acrescente `Item4`/`BtnComprar4` em `Assets/scenes/Loja.json`.

### Regular a dificuldade

Tudo no componente `EnemySpawner` da entidade `Diretor` (editável no Inspector):
`StartInterval`/`MinInterval`/`IntervalHalfLife` (ritmo), `HealthPerMinute`/`SpeedPerMinute`
(o quanto o bicho engrossa), `HordeEvery`/`HordeAmount` (as ondas em anel), `MaxAlive` (teto de
inimigos vivos — é o que segura o FPS na partida longa).

A curva de XP fica em `RunManager.XpParaNivel`.

## Três regras deste projeto (poupam depuração)

1. **Ficha manda, componente obedece.** Todo número de balanceamento do jogador vive em
   `PlayerStats`; upgrades e loja só escrevem lá. Quem copia pros componentes nativos é o
   `PlayerRunner`.
2. **Mundo pausado não roda script.** `World.Paused` congela Behaviors, colisão e vida — por isso
   level up, pausa e derrota moram no `OnUpdate` do `SurvivorsGame` (que roda sempre) e não num
   script de cena, que ficaria congelado junto e nunca leria o botão que descongela.
3. **Tela de UI não é cena.** `LoadScene` troca o mundo, mas as telas continuam desenhadas por
   cima até alguém escondê-las — é o que `MostrarSomente(...)` resolve num lugar só.

## O que ficou de fora de propósito

Som e música (`World?.Audio?.Play`), animação de sprite (componente `Animator`), tipos de inimigo
além do morcego, chefe, elite, evolução de arma, cofre/relíquia, seleção de personagem e telas de
configuração. Nada disso precisa de mudança estrutural: os ganchos já estão nos lugares marcados
acima.
