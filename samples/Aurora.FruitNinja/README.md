# Aurora Ninja

Jogo mobile em retrato (720x1280) no estilo *Fruit Ninja*, feito só com a Aurora Engine.
Frutas sobem pela borda de baixo, você arrasta o dedo para cortar, bomba encerra a partida e
fruta que escapa custa uma vida.

```bash
dotnet run --project samples/Aurora.FruitNinja
```

Mouse no PC, toque no Android — o mesmo código: a engine entrega o mouse como um toque
sintético, e o corte é multi-toque de verdade (cada dedo tem o próprio traço e o próprio combo).

## O que foi copiado do original

| Regra | Como está aqui |
|---|---|
| Fruta lançada em arco pela borda de baixo | balística de verdade: escolhe-se a altura do ápice e a velocidade sai de `v = √(2·g·h)` |
| Corte pelo traço do dedo | teste de **segmento** (posição do frame passado → posição de agora) contra o círculo da fruta |
| Fruta parte em duas metades que voam | duas entidades novas, giradas para o talho ficar em cima da direção do golpe |
| Combo | 3+ frutas no mesmo traço dão bônus, pago quando o dedo levanta |
| Bomba | cortar encerra a partida na hora; deixar passar não custa nada |
| Três vidas (os "X") | cada fruta que sai por baixo sem ser cortada acende um X |
| Bananas de poder | Congelar (câmera lenta), Frenesi (chuva de fruta, sem bomba) e Pontos em Dobro |
| Dificuldade que acelera sozinha | virou **nível**: sobe a cada 30 pontos e é o único número que alimenta a curva |

O que o original tem e aqui **não** tem: modos Arcade/Zen com regras próprias, placar online,
som (a engine tem áudio, mas o projeto não traz WAV/OGG) e a fruta gigante de vários golpes.

## APK (Android)

O APK sai do exportador da engine (Inspector → *Exportar Android*, ou chamando
`Aurora.Editor.Models.AndroidExporter.Export` direto). Ele gera um segundo projeto —
`samples/Aurora.FruitNinja.Android/` — que compila **os mesmos .cs** deste aqui, trocando o
`Program.cs` por uma `MainActivity` e a pasta `Assets/` por `AndroidAsset` dentro do pacote:

```
Export(gameCsproj: "samples/Aurora.FruitNinja/Aurora.FruitNinja.csproj",
       androidProjectDir: "samples/Aurora.FruitNinja.Android",
       applicationId: "com.aurora.ninja",
       displayName: "Aurora Ninja",
       orientation: "Portrait")
```

```bash
dotnet build samples/Aurora.FruitNinja.Android -c Release
```

Sai `bin/Release/net10.0-android/com.aurora.ninja-Signed.apk` (~15 MB, arm64-v8a + x86_64,
minSdk 24, assinado com a chave de debug — serve pra sideload, não pra Play Store). Instale com
`adb install -r com.aurora.ninja-Signed.apk`, ou copie pro aparelho e abra permitindo
"fontes desconhecidas".

Precisa da workload (`dotnet workload install android`), de um JDK 17+ e do Android SDK com a
plataforma 36. A pasta `Aurora.FruitNinja.Android/` é **gerada**: dá pra apagar e exportar de
novo a qualquer momento, e é por isso que ela não entra na solução (quem não tem a workload
não conseguiria compilar a solução inteira).

Uma armadilha que este projeto já contorna: o SDK Android injeta `using Android.App;`, que tem
uma `Android.App.GameState` própria. Todo arquivo que fala de `GameState` usa o apelido
`using GameState = Aurora.Runtime.GameState;` — sem ele o desktop compila e o APK não (CS0104).

## Como acrescentar uma fruta

1. **A arte.** Abra `tools/gerar_sprites.py`, some uma entrada em `FRUTAS` (formato + cores) e
   rode `python tools/gerar_sprites.py`. Saem dois PNGs: a fruta inteira e a **metade
   esquerda** — a metade da direita é essa mesma imagem espelhada pelo jogo em tempo de
   execução. Se preferir desenhar à mão, só respeite esses dois arquivos de 128x128.
2. **A ficha.** Some uma entrada em `Assets/database/frutas.json` apontando para os dois PNGs.

Pronto. Nenhuma linha de C# muda: nenhum script cita fruta pelo nome, o lançador pergunta ao
catálogo. Campos que valem a pena conhecer:

- `Peso` — chance relativa no sorteio (0 tira do jogo sem apagar a ficha).
- `NivelMinimo` / `NivelMaximo` — a fruta estreia (e se aposenta) sozinha conforme o nível.
- `RaioCorte` — raio de acerto como fração do `Tamanho`; 0.5 é exatamente o círculo do sprite.
- `Tipo` — `Fruta`, `Bomba` ou `Poder` (com `Efeito`: `Congelar`, `Frenesi`, `Dobro`).
- `VidasAoCortar` — já existe: uma ficha com `"VidasAoCortar": 1` vira uma fruta-coração.

## Como acrescentar uma arma

Some uma entrada em `Assets/database/laminas.json`. A lâmina muda o **rastro** (cor, espessura,
duração) e o **corte**:

- `Alcance` — multiplica o raio de acerto: é a alavanca de "corta de raspão".
- `VelocidadeMinima` — px/s que o dedo precisa fazer pra o traço cortar; alto = arma pesada.
- `MultiplicadorPontos` — multiplica os pontos de cada fruta.
- `Preco` — em moedas; a primeira da lista deve ser 0 (é a que o jogador já tem).

A loja mostra até quatro (as quatro linhas de `Assets/scenes/Laminas.json`); para uma quinta,
duplique um par `Lamina4` + `BtnLamina4` ali e suba o limite dos laços em `NinjaGame.cs`.

Compra e escolha ficam no `GameState` como switches (`Lamina_katana`, `Equipada_katana`), então
entram no save de graça e continuam certas mesmo se você reordenar o JSON.

## Onde mexer em cada coisa

| Quero mudar | Arquivo |
|---|---|
| Quantas frutas por leva, ritmo, chance de bomba | `Game/CurvaNivel.cs` |
| Pontos por nível, teto de nível, bônus de combo | `Game/CurvaNivel.cs` |
| Vidas, o que acontece ao cortar/escapar, poderes | `Game/Partida.cs` |
| Tamanho do campo, gravidade, altura de lançamento | `Game/Arena.cs` |
| Arco do arremesso, espalhamento, fila da leva | `Scripts/Lancador.cs` |
| Detecção do corte e desenho do rastro | `Scripts/Lamina.cs` |
| Voo da fruta e o que o corte cria | `Scripts/Fruta.cs`, `Scripts/Metade.cs`, `Scripts/Espirro.cs` |
| Telas, HUD, fluxo entre menus | `NinjaGame.cs` + `Assets/scenes/*.json` |

## Detalhes de implementação que valem uma linha

- **Sem `Collider`.** O corte é segmento contra círculo; o passo de colisão da engine testa
  sobreposição na posição já atualizada e deixaria passar todo gesto rápido.
- **`World.Paused` não serve pro Congelar** (é tudo ou nada): o poder é uma escala de tempo que
  fruta, metade e lançador aplicam no próprio `deltaTime`. O rastro do dedo fica em tempo real.
- **O rastro é desenhado pelo `NinjaGame`**, não pelo Behavior: `Behavior` não tem gancho de
  render, e o traço precisa da mesma projeção das frutas.
- **HUD dividida de propósito:** pontos/nível/recorde são `UiText` com token (`{Pontos}`) e se
  atualizam sozinhos; os X das vidas e os avisos flutuantes são desenhados em código, porque
  trocam de textura e animam.
