# Guia — Sland Survivor (sandbox 2D com mundo procedural)

Jogo de exploração/construção no estilo Terraria, feito inteiro sobre a Aurora:
`samples/Aurora.SlandSurvivor`. Mundo de **1200x300 tiles** gerado por ruído, mineração e
construção bloco a bloco, iluminação por propagação, ciclo dia/noite, inimigos, fabricação e
save. **Não usa nenhum arquivo de arte** — tileset e personagens são pintados em código.

```bash
dotnet run --project samples/Aurora.SlandSurvivor
```

| Argumento | Efeito |
| --- | --- |
| `--seed 12345` | gera um mundo específico (mesma seed = mesmo mundo, em qualquer máquina) |
| `--time 22` | começa nessa hora do relógio (0–23) |
| `--depth 40` | nasce em um poço já aberto até essa profundidade |
| `--zoom 3` | aproxima a câmera (padrão 2) |
| `--shot arquivo.png` | salva um PNG da tela e fecha |
| `--smoke` | roteiro de verificação automatizado, sem teclado |

## Controles

| Tecla | Ação |
| --- | --- |
| A/D ou setas | mover |
| Espaço/W | pular (na água, nadar para cima) |
| Botão esquerdo | cavar o bloco mirado; sem bloco na mira (ou com espada na mão), golpear |
| Botão direito | colocar o bloco/usar o item selecionado |
| 1–0, Q | escolher o item da barra rápida |
| Tab ou E | mochila |
| C | fabricação |
| M | minimapa |
| H | esconder a ajuda |
| F5 / F9 | salvar / carregar |
| F12 | captura de tela (vai para Imagens/SlandSurvivor) |
| ESC | sair |

## Como o mundo é gerado

`Worlds/WorldGen.cs`. Tudo sai da seed por hash das coordenadas (`Worlds/Noise.cs`) — não
existe `Random` no gerador, então dá para amostrar qualquer ponto fora de ordem e o mundo é
reproduzível.

1. **Relevo e bioma por coluna** — uma onda larga de colinas (ruído 1D de baixa frequência)
   mais um detalhe fino. Um segundo ruído, ainda mais lento, decide floresta/deserto/tundra.
2. **Preenchimento vertical** — superfície (grama/areia/neve), camada de terra de espessura
   variável, pedra, rocha profunda a partir da linha 175 e rocha-mãe irregular no fundo.
3. **Paredes de fundo** — a partir de 3 tiles abaixo do terreno. É o que faz caverna parecer
   caverna em vez de buraco no vazio; a boca da caverna continua mostrando céu.
4. **Cavernas** — ruído de *crista* (`Noise.Ridge`) alongado no eixo X vira túnel comprido;
   um segundo campo abre salões grandes só na parte funda. Perto da superfície a escavação
   entra suavizada, senão o chão vira queijo.
5. **Argila e minérios** — cada veio tem faixa de profundidade, densidade e raio próprios:
   carvão raso, ferro médio, ouro fundo, cristal só na rocha profunda.
6. **Líquidos** — lagos nas depressões do relevo e poças em caverna, preenchidos linha a
   linha com teste de vazamento (a linha só é aceita se tiver piso e parede dos dois lados).
7. **Vegetação e ruínas** — árvores na floresta, cactos no deserto, salas de tijolo com tocha
   no subsolo.
8. **Ponto de nascimento** — procura uma coluna com chão firme e sem lago, abre uma clareira
   (senão uma árvore nasceria em cima do jogador) e devolve a linha do spawn.

Mudar o tamanho do mundo é `WorldGen.Generate(seed, largura, altura)`.

## Estrutura

```
Program.cs                 argumentos de linha de comando
SurvivorGame.cs            relógio dia/noite, iluminação, spawn, partículas, save, HUD
Worlds/Noise.cs            ruído de valor determinístico (hash das coordenadas)
Worlds/WorldGen.cs         geração procedural (as 8 etapas acima)
Worlds/TileDb.cs           tabela de tiles + tileset pintado em código
Worlds/TileWorld.cs        acesso ao mapa: converter pixel↔tile, quebrar, colocar, cache do céu
Worlds/LightMap.cs         propagação de luz por tile (céu + tochas)
Gameplay/PlayerController.cs  andar, pular, nadar, cavar, construir, golpear
Gameplay/EnemyBehavior.cs     gosma (pulos), zumbi (anda), morcego (voa)
Gameplay/EnemySpawner.cs      onde e quando nasce bicho
Gameplay/ItemDropBehavior.cs  item caído: cai, é atraído, é coletado
Gameplay/PixelArt.cs          sprites em mapa de caracteres + paleta
Items/                     itens, mochila e receitas
UI/Hud.cs                  vida, barra rápida, mochila, fabricação, minimapa, avisos
Saves/WorldSave.cs         save binário com compressão por repetição (RLE)
Tools/PngWriter.cs         escritor de PNG usado pelo F12
```

O mundo inteiro são **dois `Tilemap` da engine** (frente sólida + parede de fundo). A engine
já recorta o desenho pela câmera e resolve colisão contra tiles sólidos, então o custo por
frame depende do tamanho da tela, não do tamanho do mundo.

## Iluminação

`Worlds/LightMap.cs`. A cada frame, só a janela visível (mais uma margem) é recalculada: uma
busca em largura por níveis, de 15 até 1, a partir de duas fontes — o céu (apenas nas colunas
abertas, atenuado pela hora) e os tiles que emitem luz (tocha, lava). Andar para dentro da
rocha custa 3 níveis, para dentro de área com parede de fundo custa 2, no ar aberto custa 1.

O desenho é feito por cima de tudo: cada tile vira 4 quadrantes com o alpha interpolado entre
os cantos (`LightMap.Corner`), o que dá um degradê suave sem shader. Por isso jogador,
inimigos e itens também escurecem no fundo da caverna — e por isso a tocha vale alguma coisa.

O jogador carrega um brilho fraco (nível 7, ~4 tiles) para não descer completamente às cegas.

## Verificação automatizada

```bash
dotnet run --project samples/Aurora.SlandSurvivor -- --smoke --seed 777
```

Confere, sem ninguém no teclado: mundo com terreno plausível em toda coluna, rocha-mãe no
fundo, cavernas e minério em quantidade razoável, spawn fora da rocha, jogador pousando no
chão, mineração com progresso (uma picaretada racha, não quebra), coleta, construção,
fabricação, luz do céu e da tocha, dano em inimigo, nascimento de bicho à noite e ida e volta
do save. Qualquer falha imprime a razão e encerra com código 1.
