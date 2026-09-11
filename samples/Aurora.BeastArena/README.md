# Beast Arena (protótipo)

Batalha 1x1 em tempo real, em retrato (720x1280), feita com a Aurora Engine. Você solta
criaturas e feitiços numa arena contra uma IA e disputa **três santuários**: cada santuário
dominado dá pontos e mana. Chegou a 200 pontos, ganhou; senão, ganha quem tiver mais quando os
3 minutos acabam.

O gancho: **criaturas evoluem durante a partida**. Quem mata ganha XP (barra dourada debaixo da
vida) e muda de forma — Lobo → Lobo Alfa → Senhor dos Lobos. E matar uma criatura evoluída dá
**mana de recompensa** pra quem matou. Toda luta vira a pergunta "mato agora ou deixo crescer?".

```bash
dotnet run --project samples/Aurora.BeastArena
```

Mouse no PC, toque no Android. Arraste a carta até a arena, ou toque na carta e depois na arena.
`Esc` volta ao menu.

## APK (Android)

O projeto `samples/Aurora.BeastArena.Android/` é **gerado** pelo exportador da engine e compila
os mesmos `.cs` deste (sem o `Program.cs`), com `Assets/` empacotado no APK:

```
AndroidExporter.Export(gameCsprojPath: "samples/Aurora.BeastArena/Aurora.BeastArena.csproj",
                       androidProjectDir: "samples/Aurora.BeastArena.Android",
                       applicationId: "com.aurora.beastarena",
                       displayName: "Beast Arena",
                       orientation: "Portrait")
```

```bash
dotnet build samples/Aurora.BeastArena.Android -c Release
```

Sai `bin/Release/net10.0-android/com.aurora.beastarena-Signed.apk` (~15 MB, assinado com chave
de debug: serve pra sideload, não pra Play Store). Mesmos pré-requisitos do Aurora Ninja:
workload `android`, JDK 17+ e Android SDK com a plataforma 36.

## O que tem

| Regra | Como está aqui |
|---|---|
| Arena | 18 x 24 tiles; um ninho por lado; 3 santuários em diagonal e pedras que tropa de chão contorna. Simetria de rotação: cada lado tem um santuário "de casa", um fica no meio |
| Captura | tropa de chão de uma equipe só dentro do santuário puxa a influência (4 s de neutro a dominado; mais tropas aceleram até 2x); das duas equipes, trava. Virar o do oponente = neutralizar + dominar |
| Pontos | 1 por segundo por santuário dominado; 200 ganha na hora; no fim dos 3 min, mais pontos vence |
| Ninho | não é alvo e não cai; aura que cura quem é da casa e queima invasor |
| Mana | 0,3/s + 0,06/s por santuário dominado; abate devolve 40% do custo da vítima; máximo 12 |
| Mão de 4 + fila | a carta jogada vai pro fim da fila (dá pra contar o ciclo do oponente) |
| Área de implantação | em volta do próprio ninho e de cada santuário dominado — fecha enquanto tem inimigo dentro dele |
| Segundo de implantação | a criatura aparece meio transparente antes de agir |
| Dono dos 3 | criaturas sem nada pra tomar marcham pro ninho inimigo, e a aura cobra — o exército de quem lidera não cresce parado |
| 8 cartas | Lobo, Rinoceronte, Vespas, Serpente, Ouriço, Xamã-Coruja, Lamaçal, Queimada |
| Status | veneno (Serpente) e lentidão (Lamaçal) |
| Voadoras | não contam pra captura; passam por cima de pedra |
| **Evolução** | por abate (XP = custo da vítima × (1 + estágio dela)) ou por captura (Rinoceronte) |
| **Recompensa** | matar criatura evoluída dá mana ao lado que matou |
| IA | mesmas regras do jogador; defende santuário invadido, reforça tanque, ataca em onda o santuário que vale mais |

Não tem, de propósito (ver "Próximos passos"): arte, som, progressão, coleção, online.

## Arquitetura

```
Sim/      a batalha como lógica pura — NÃO referencia a Aurora
Render/   desenha a batalha e lê o toque — só lê a Sim e manda comandos
BeastArenaGame.cs   fluxo de telas + relógio de passo fixo
Simulador.cs        IA x IA sem janela, pra balancear
Assets/database/cartas.json   todas as cartas
```

Três decisões que valem mais que o resto:

- **Passo fixo.** `Batalha.Avancar()` anda sempre 1/30 s; o jogo acumula o tempo real e chama
  quantas vezes couber, e o desenho interpola entre dois passos. Mesma semente + mesmos
  comandos = mesma partida (tem teste disso).
- **Jogada é comando.** `Batalha.Jogar()` só enfileira `(equipe, carta, posição)`; o passo
  seguinte valida de novo e aplica. É exatamente o que um PvP por lockstep ou servidor
  autoritativo manda pela rede — não precisa reescrever a regra pra ficar online.
- **Nada de `World`/entidades pras unidades.** A simulação já é a fonte da verdade; espelhar
  cada unidade numa entidade seria manter dois estados em sincronia à mão.

Limite honesto: a conta é em `float`. Reproduzível na mesma build e CPU; entre ARM e x64 pode
divergir. Pra lockstep entre aparelhos, trocar por ponto fixo (a estrutura não muda).

## Como acrescentar uma carta

Uma entrada em `Assets/database/cartas.json` e, pra ela entrar no jogo, o id no `Baralho`
(sempre 8). Nenhuma linha de C#. O catálogo é conferido no boot: evolução com XP fora de ordem
ou id de baralho inexistente derruba com a mensagem certa, em vez de "a evolução nunca dispara".

Campos que mais mexem no jogo:

- `Alcance` + `VelocidadeProjetil` — 0.5 e 0 é corpo a corpo; 5 e 10 é atirador.
- `IgnoraUnidades` — vai direto pro santuário e só bate em quem disputa aquele santuário (tanque).
- `Voa` — passa por cima de pedra, mas não captura: bom pra caçar, inútil pra segurar.
- `Area` — dano em volta do alvo. Uma evolução pode dar área que a forma base não tem (Hidra).
- `XpAoCapturar` — caminho de evolução pra quem não mata ninguém.
- `Evolucoes[].Recompensa` — o freio da bola de neve. Alto = evoluir é arriscado.

## Balanceamento

```bash
dotnet run --project samples/Aurora.BeastArena -- --simular 200
```

Roda 200 partidas IA x IA em segundos e imprime vitórias por lado, duração média, quantas
terminaram na meta de pontos, santuários dominados, evoluções, mana de recompensa e de abates
por partida, e quanto cada criatura foi jogada. Referência atual (200 partidas): lados
equilibrados (105 x 93, 2 empates), ~131 s por partida, 95% terminam na meta, ~5,5 santuários
dominados, ~12 evoluções e ~13 de mana de recompensa por partida.

Os números da IA não são os de humanos — servem pra pegar regressão ("mudei o rinoceronte e
agora toda partida acaba em 60 s"), não pra decidir o balanceamento final.

## Testes

```bash
dotnet test tests/Aurora.BeastArena.Tests
```

Regras da simulação com cartas sintéticas (não quebram quando o JSON é rebalanceado): ciclo do
baralho, mana, área de implantação (ninho, santuário dominado, fechada sob ataque), captura,
disputa e neutralização, mana por santuário, aura do ninho, contorno de pedra, marcha de quem
domina os três, evolução por abate e por captura, abate e recompensa, lentidão, meta de pontos,
fim por tempo, determinismo e uma partida IA x IA inteira.

## Onde mexer em cada coisa

| Quero mudar | Arquivo |
|---|---|
| Cartas, evoluções, baralho | `Assets/database/cartas.json` |
| Meta de pontos, mana, duração, tempo de captura | `Sim/Batalha.cs` |
| Captura, pontos, aura do ninho | `Sim/Batalha.Santuarios.cs` |
| Tamanho da arena, santuários, pedras, raios de implantação | `Sim/Campo.cs` |
| Mira, objetivo, dano, morte, evolução, recompensa | `Sim/Batalha.Combate.cs` |
| Caminho, desvio, empurrão | `Sim/Batalha.Movimento.cs` |
| Comportamento da IA | `Sim/IaOponente.cs` |
| Visual da arena e das criaturas | `Render/VisaoArena.cs` |
| Mão, mana, placar, arrastar/tocar | `Render/Mao.cs` |
| Telas | `BeastArenaGame.cs` + `Assets/scenes/*.json` |

## Próximos passos (na ordem que eu faria)

1. **Jogar com gente de verdade.** 5–10 pessoas, 10 partidas cada. A pergunta é uma só: pedem
   pra jogar mais uma? Anotar em que momento a evolução gerou reação e se quem estava atrás
   em santuários conseguiu virar.
2. **Arte e som das 8 cartas**, com os 2–3 estágios visivelmente diferentes. Só `Render/` muda.
3. **Mais cartas e 1–2 arenas** com modificador (pântano, fogo) e outro desenho de santuários
   — de novo só JSON + `Campo`.
4. **Meta-game mínimo:** subir de arena, desbloquear carta, montar baralho.
5. **Online:** PvP assíncrono/fantasma primeiro (grava os comandos de partidas reais e a IA
   reproduz o baralho); lockstep em tempo real só com base de jogadores.
