# Água, lava e sangue — tiles de líquido animados

Guia da camada de líquido da Aurora: como montar uma lagoa, um rio de lava ou uma poça de
sangue num jogo 2D de cima (RPG), usando `LiquidTileset` + `Tilemap`.

Nada aqui precisa de arte externa nem de shader: o tileset é pintado em código, pixel a
pixel, e a animação é feita trocando a linha do atlas.

---

## Como funciona

O tileset de um líquido é um atlas de **16 colunas × N linhas**:

```
        máscara →   0    1    2    3   ...  15
frame 0           [   ][   ][   ][   ] ... [   ]
frame 1           [   ][   ][   ][   ] ... [   ]
frame 2           [   ][   ][   ][   ] ... [   ]
frame 3           [   ][   ][   ][   ] ... [   ]
```

* **A coluna é a máscara de vizinhança** — `N=1`, `L=2`, `S=4`, `O=8`. Bit ligado = o
  vizinho daquele lado também é líquido, ou seja, **aquele lado não tem margem**. Máscara
  `15` é o miolo do lago; máscara `0` é uma poça solta, com espuma nos quatro lados e os
  cantos arredondados (o que sobra fica transparente e deixa ver o chão embaixo).
* **A linha é o frame da animação.** O tile `N` da primeira linha vira `N + 16*frame`.

Duas peças da engine casam com esse layout:

| Peça | O que faz |
|------|-----------|
| `Tilemap.Autotile()` | Olha os 4 vizinhos de cada célula não-vazia e escreve a máscara como índice. |
| `Tilemap.AnimationFrames` / `AnimationFrameDuration` / `AnimationColumns` | Fazem o desenho pular de linha ao longo do tempo. `World.Update` cuida do relógio. |

Você pinta "onde tem água"; margem, canto e animação saem de graça.

---

## Receita em código

```csharp
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;

const int Cell = 32;

var pond = World.CreateEntity("Lagoa");
pond.Add(new Transform(new Vector2(400f, -160f)));

var water = pond.Add(new Tilemap
{
    Tileset = Assets.LoadTexture("tilesets/water.png"),
    TileWidth = Cell,
    TileHeight = Cell,
    Width = 12,
    Height = 12,
    Layer = 1,                                   // acima do terreno, abaixo dos personagens
    AnimationFrames = 4,
    AnimationFrameDuration = 0.18f,
    AnimationColumns = LiquidTileset.Columns,    // 16
});

// 1) Marque as células molhadas com qualquer índice >= 0.
water.Fill(2, 2, 8, 8, 0);

// 2) O Autotile reescreve tudo como máscara.
water.Autotile(outsideIsFilled: false);

// 3) Opcional: as 16 máscaras são parede, o jogador contorna em vez de atravessar.
for (int mask = 0; mask <= LiquidTileset.Center; mask++)
    water.SolidTiles.Add(mask);
```

`outsideIsFilled`:

* `false` → a borda da grade conta como terra. Use em lagoa/poça que acaba dentro do mapa.
* `true` (padrão) → fora da grade conta como líquido. Use em oceano/lava que encosta na
  borda do mapa e não deveria ganhar espuma ali.

Exemplo rodando: `BuildPond` em [samples/Aurora.Farm/FarmGame.cs](../samples/Aurora.Farm/FarmGame.cs).

---

## Receita no editor

1. Copie os PNGs de `samples/Aurora.Farm/Assets/tilesets/` (`water.png`, `lava.png`,
   `blood.png`, `swamp.png`, `water_shallow.png`) pra pasta `Assets/tilesets/` do seu jogo.
   Crie um **Tilemap** e aponte `Texture` pra ele.
2. `TileWidth`/`TileHeight` = **32** (o tamanho com que os PNGs foram gerados).
3. Preencha `AnimationFrames` = **4**, `AnimationFrameDuration` = **0.18**,
   `AnimationColumns` = **16**. Sem isso a água fica parada no frame 0.
4. Pinte com a paleta de tiles. A paleta mostra o atlas inteiro (64 células num tileset de
   4 frames), mas **só as 16 primeiras interessam** — são as máscaras; as de baixo são os
   frames delas. Pinte tudo com a coluna 15 (miolo) e ajuste as bordas na mão, ou monte a
   lagoa em código com `Autotile()`, que é bem mais rápido.
5. `SolidTiles` = `0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15` se a água tiver
   que bloquear o jogador.

Água sempre numa **camada própria**, com `Layer` acima do terreno. Assim os cantos
transparentes deixam o chão aparecer e a lagoa lê como mancha, não como quadrado.

---

## Gerando seus próprios tilesets

Os cinco PNGs vêm de presets. Pra gerar variações (água tropical, veneno roxo, lodo):

```csharp
var style = LiquidStyle.Water();
style.TileSize = 32;
style.Frames = 6;
style.Deep = Color.FromBytes(10, 70, 90);
style.Shallow = Color.FromBytes(30, 150, 160);
style.Crest = Color.FromBytes(180, 245, 240);
style.Edge = Color.FromBytes(240, 252, 250);

LiquidTileset.SavePng("Assets/tilesets/agua_tropical.png", style);
```

Ou, sem passar por arquivo nenhum (o tileset nasce direto na GPU):

```csharp
var texture = LiquidTileset.CreateTexture(Gl, style);
```

Presets prontos: `LiquidStyle.Water()`, `ShallowWater()`, `Lava()`, `Blood()`, `Swamp()`.

Campos que mais mudam o visual:

| Campo | Efeito |
|-------|--------|
| `Deep` / `Shallow` | Vale e crista da onda — o contraste entre os dois é "quanta onda" se vê. |
| `Crest` | Brilho no topo da crista. É o que faz parecer molhado. |
| `Edge` | Cor da margem: espuma clara na água, crosta escura na lava. |
| `EdgeWidth` | Largura da margem, em fração do tile (`0.14` ≈ 4–5 px num tile de 32). |
| `CornerRadius` | Arredondamento do canto solto. `0` = quadrado. |
| `WavesX` / `WavesY` | Frequência da ondulação. **Só inteiros** — é isso que faz o tile costurar sem emenda no vizinho. |
| `Opacity` | Abaixo de 1 deixa ver a camada de baixo (água rasa sobre areia). |
| `Frames` | Mais frames = animação mais suave e PNG maior. 4 é o padrão, 6–8 pra água mais calma. |

O gerador é determinístico: o mesmo `LiquidStyle` produz exatamente o mesmo PNG em qualquer
máquina, então o asset pode ser versionado sem sujar o diff a cada build.

---

## Lava

Mesmo esquema, outro preset — a "espuma" vira crosta de rocha escura e a crista brilha:

```csharp
var lava = pit.Add(new Tilemap
{
    Tileset = Assets.LoadTexture("tilesets/lava.png"),
    TileWidth = 32, TileHeight = 32, Width = 16, Height = 10, Layer = 1,
    AnimationFrames = 4, AnimationFrameDuration = 0.28f,   // lava escorre mais devagar
    AnimationColumns = LiquidTileset.Columns,
});
```

Pra lava iluminar a caverna, ponha uma `Light2D` no meio da poça (é brilho aditivo, não
sombra — veja "Limites" no fim):

```csharp
pit.Add(new Light2D { Radius = 220f, Color = Color.FromBytes(255, 140, 40), Intensity = 0.8f });
```

Dano por contato: a lava é um `Tilemap` sem `Collider`, então o toque não gera evento de
colisão. O jeito direto é um `Behavior` no jogador consultando a célula:

```csharp
public sealed class LavaDamage : Behavior
{
    public Tilemap Lava = null!;
    public Transform LavaTransform = null!;

    public override void Update(float deltaTime)
    {
        var local = Get<Transform>()!.Position - LavaTransform.Position;
        int x = (int)(local.X / Lava.TileWidth);
        int y = (int)(local.Y / Lava.TileHeight);

        if (Lava.GetTile(x, y) >= 0)
            World?.Damage(Entity, 20f * deltaTime);
    }
}
```

---

## Sangue

Sangue tem dois usos, e cada um pede uma peça diferente:

**1. Poça no chão (decal permanente)** — `LiquidStyle.Blood()`, mesma receita da lagoa, numa
camada acima do terreno. Marque as células e chame `Autotile()`. Como `CornerRadius` do
preset é alto, uma célula solta já lê como uma poça redonda.

**2. Respingo do golpe (efeito)** — isso é partícula, não tile. O `ParticleEmitter` já faz:

```csharp
var splash = World.CreateEntity("Respingo");
splash.Add(new Transform(hitPosition));
splash.Add(new ParticleEmitter
{
    Rate = 120f,
    LifeMin = 0.25f, LifeMax = 0.5f,
    SpeedMin = 60f, SpeedMax = 180f,
    AngleMin = 200f, AngleMax = 340f,          // pra cima (Y cresce pra baixo)
    SizeStart = 4f, SizeEnd = 1f,
    ColorStart = Color.FromBytes(170, 20, 26),
    ColorEnd = Color.FromBytes(90, 8, 12, 0),  // some ao morrer
    Gravity = new Vector2(0f, 420f),
    MaxParticles = 40,
    Layer = 9,
});
```

O emissor emite **continuamente** — não existe "solte 20 partículas e pare". Pra um jato
único, desligue e limpe com um `Behavior` curto:

```csharp
public sealed class OneShot : Behavior
{
    public float Duration = 0.08f;
    private float _elapsed;

    public override void Update(float deltaTime)
    {
        _elapsed += deltaTime;

        if (_elapsed > Duration)
            Get<ParticleEmitter>()!.Emitting = false;

        if (_elapsed > Duration + 1f)     // espera as vivas morrerem antes de sumir
            World?.Destroy(Entity);
    }
}
```

O mesmo padrão serve pra faísca de lava, poeira do passo e folha caindo.

---

## Limites (o que ainda não existe)

Pra não perder tempo procurando:

* **Sem sombra projetada.** `Light2D` é brilho aditivo, não oclusão: a luz não é bloqueada
  por parede nem projeta silhueta. Sombra de personagem em RPG se faz com um sprite de
  elipse escura numa `Layer` logo abaixo do boneco.
* **Sem shader por objeto.** O `SpriteBatch` tem um shader só, então não dá pra fazer
  refração/distorção de água, calor subindo da lava ou ondulação do reflexo. O efeito de
  água aqui é todo por textura + troca de frame.
* **Sem render target.** Não existe superfície persistente pra "pintar sangue por cima" do
  cenário; decal = tile ou sprite de verdade na cena.
* **Sem burst de partícula.** Só emissão contínua (veja o `OneShot` acima).
* **Só a primeira linha do tileset anima.** É de propósito: as linhas de baixo são os frames.
  Se um tileset misturar chão estático e água, separe a água numa camada própria.
* **Escurecer a cena inteira** (noite, caverna, filtro subaquático) é o `GlobalTint`.
