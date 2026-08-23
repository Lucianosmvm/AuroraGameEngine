# Tutorial de scripts — mover o player, atacar no clique e instanciar a animação do golpe

Passo a passo completo de três coisas que todo RPG 2D precisa:

1. **Mover** o personagem em 8 direções.
2. **Atacar no clique do mouse**, com cooldown.
3. **Instanciar uma animação** (o corte) na direção em que o player está olhando, e fazer ela
   sumir sozinha quando acaba.

No fim tem os dois arquivos prontos pra copiar. Tudo aqui roda de verdade no sample da
fazenda — `samples/Aurora.Farm/Scripts/PlayerAttack.cs` e `AttackEffect.cs` são exatamente
estes scripts.

Este documento explica os scripts prontos. Se você prefere **escrever cada linha você mesmo**,
com um teste na tela a cada etapa e o inimigo perseguidor no fim:
[TUTORIAL-ATAQUE-INIMIGO.md](TUTORIAL-ATAQUE-INIMIGO.md).

Referência de API (consulta, não tutorial): [REFERENCIA-SCRIPTS-RPG.md](REFERENCIA-SCRIPTS-RPG.md).
Montar o projeto do zero: [GUIA-JOGO-BASE.md](GUIA-JOGO-BASE.md).

---

## 0. O básico de um script

Um script é uma classe que herda `Behavior` e é **anexada a uma entidade**:

```csharp
[SceneScript]                                  // faz o editor/serializador enxergarem o script
public sealed class MeuScript : Behavior
{
    public float Velocidade = 200f;            // campo público = aparece no Inspector

    public override void Start() { }           // 1x, no primeiro frame ativo
    public override void Update(float dt) { }  // todo frame, dt = segundos desde o anterior
}
```

Duas fontes de dado, e é aqui que todo mundo tropeça no começo:

| Você quer… | Onde busca |
|---|---|
| Componente **da própria entidade** (`Transform`, `SpriteRenderer`, `Animator`, `Health`, outro script seu) | `Get<T>()` |
| Sistema **do jogo** (input, câmera, assets, som, inventário, UI, save) | `World?.X` — `World?.Input`, `World?.Camera`, `World?.Assets`… |

`World` é injetado automaticamente em todo `Behavior`. Nunca precisa passar nada na mão.

> **Regra de ouro do `deltaTime`:** tudo que é "por segundo" (velocidade, cooldown, timer)
> multiplica ou subtrai `deltaTime`. Sem isso o jogo fica mais rápido num PC potente e mais
> lento num fraco.

---

## 1. Mover o player

### 1.1 O que a entidade precisa ter

| Componente | Pra quê |
|---|---|
| `Transform` | posição — é o que o script move |
| `SpriteRenderer` | o desenho (e o `FlipX` pra virar de lado) |
| `Collider` | opcional: bater em parede/tile sólido |
| `Animator` | opcional: trocar entre parado/andando |

### 1.2 O script

```csharp
using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace MeuJogo;

[SceneScript]
public sealed class PlayerMove : Behavior
{
    public float Speed = 200f;   // pixels por segundo

    public override void Update(float deltaTime)
    {
        var input = World?.Input;
        var transform = Get<Transform>();
        if (input is null || transform is null)
            return;

        // AxisX/AxisY já misturam WASD + setas + analógico esquerdo do gamepad.
        // Y positivo = pra BAIXO (convenção de tela, não de matemática).
        var move = new Vector2(input.AxisX, input.AxisY);

        if (move.LengthSquared() > 0f)
        {
            // Normalizar é o que impede a diagonal de ser ~41% mais rápida que a reta.
            move = Vector2.Normalize(move);
            transform.Position += move * Speed * deltaTime;

            var sprite = Get<SpriteRenderer>();
            if (sprite is not null && move.X != 0f)
                sprite.FlipX = move.X < 0f;
        }

        // Se tiver Animator com transições "Speed >= 1 → walk" / "Speed <= 0 → idle".
        Get<Animator>()?.SetFloat("Speed", move.Length() * Speed);
    }
}
```

**Por que `LengthSquared() > 0f` e não `Length() > 0f`?** `LengthSquared` não tira raiz
quadrada. Rodando todo frame pra toda entidade, isso importa. E `Vector2.Normalize` de um
vetor zero devolve `NaN` — o teste também está protegendo disso.

### 1.3 Colisão

Não tem código de colisão aqui de propósito: o `World` resolve sozinho.

* **Parede de tile**: `Tilemap.SolidTiles` com os índices sólidos. Quem tiver `Collider`
  é empurrado pra fora.
* **Objeto solto** (casa, árvore): entidade com `Collider { IsSolid = true, IsKinematic = true }`.
* **Gatilho** (moeda, porta): `Collider { IsSolid = false }` → chega em `OnTriggerEnter`.

---

## 2. Saber pra onde o player está olhando

"A frente do player" é um `Vector2` unitário. Duas maneiras de descobrir, e o jogo geralmente
usa as duas:

### 2.1 Direção pelo movimento

Guarde a última direção andada num campo:

```csharp
private Vector2 _facing = new(0f, 1f);   // começa olhando pra baixo

// dentro do Update, quando move != 0:
_facing = Vector2.Normalize(move);
```

### 2.2 Direção pelo mouse (a que o ataque usa)

O mouse chega em **pixel de tela** (`input.MousePosition`). O player vive em **coordenada de
mundo**. Enquanto a câmera está parada na origem e sem zoom, os dois batem por coincidência —
assim que a câmera segue o player, param de bater. A conversão é a câmera quem faz:

```csharp
var target = World.Camera.ScreenToWorld(input.MousePosition);   // tela → mundo
var direction = Vector2.Normalize(target - transform.Position);
```

> **Erro clássico:** usar `input.MousePosition` direto como se fosse posição de mundo. O
> ataque funciona no começo da fase e começa a sair torto conforme o jogador anda. É sempre
> isso.

`World.Camera` pode ser `null` antes do jogo carregar, então na prática:

```csharp
if (World?.Camera is not { } camera || World.Input is not { } input)
    return null;
```

### 2.3 Ângulo livre ou 8 direções?

Ângulo livre (padrão) aponta exatamente pro cursor. Pra RPG clássico, arredonde pro múltiplo
de 45° mais próximo:

```csharp
private static Vector2 Snap(Vector2 direction)
{
    float step = MathF.PI / 4f;                                       // 45°
    float angle = MathF.Round(MathF.Atan2(direction.Y, direction.X) / step) * step;
    return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
}
```

`MathF.PI / 2f` no lugar de `/ 4f` dá 4 direções (cima/baixo/esquerda/direita).

---

## 3. Atacar no clique

### 3.1 Clique vs botão segurado

| Método | Quando é true |
|---|---|
| `input.WasMouseClicked(MouseButton.Left)` | **só no frame** em que o botão desceu |
| `input.IsMouseDown(MouseButton.Left)` | em todo frame enquanto está segurado |

Pra golpe único é `WasMouseClicked`. `IsMouseDown` serve pra ataque contínuo (metralhadora,
enxada segurada) — e aí o cooldown é obrigatório, senão nascem 60 golpes por segundo.

`MouseButton` vem de `Silk.NET.Input`: `using Silk.NET.Input;`.

### 3.2 Cooldown

```csharp
public float Cooldown = 0.35f;
private float _cooldownTimer;

public override void Update(float deltaTime)
{
    _cooldownTimer -= deltaTime;   // desce sempre, mesmo sem atacar

    if (!input.WasMouseClicked(MouseButton.Left) || _cooldownTimer > 0f)
        return;

    _cooldownTimer = Cooldown;
    // ... ataca
}
```

Deixe o `-= deltaTime` **fora** de qualquer `if` de input: um timer que só anda quando o
jogador clica nunca zera.

---

## 4. Instanciar a animação do golpe

Esta é a parte principal. Nasce uma **entidade nova**, com sprite + animação, na frente do
player, girada pra direção do ataque — e que se destrói quando a animação acaba.

### 4.1 O sprite sheet

O `slash.png` do sample é assim:

```
 ┌────┬────┬────┬────┐
 │ f0 │ f1 │ f2 │ f3 │   192 x 48  →  4 quadros de 48x48, uma linha só
 └────┴────┴────┴────┘
```

Duas regras da arte:

1. **Uma linha, quadros quadrados.** O script descobre o tamanho sozinho:
   `frameSize = texture.Height`, `frames = texture.Width / frameSize`.
2. **Desenhe o efeito apontando pra DIREITA.** Rotação `0` no `Transform` é "pra direita"
   (é a convenção de `MathF.Atan2`/`Cos`/`Sin`). Se a arte apontar pra cima, todo golpe sai
   90° torto — ou você conserta a arte, ou soma `MathF.PI / 2f` na rotação.

Sem arte nenhuma dá pra testar: `SpriteRenderer` sem `Texture` desenha um retângulo colorido.

### 4.2 Carregar a textura de dentro do script

```csharp
var texture = World.Assets?.LoadTexture("sprites/slash.png");
```

`World.Assets` é o mesmo `AssetManager` do jogo e **cacheia por caminho** — chamar isso a cada
golpe não relê o PNG do disco, devolve a textura que já está na GPU. Caminho é relativo à
pasta `Assets/` do projeto.

### 4.3 Montar a entidade

```csharp
var effect = World.CreateEntity("AttackEffect");

// 1) Onde e pra que lado. Rotation é em RADIANOS.
effect.Add(new Transform(origin + _facing * Reach)
{
    Rotation = MathF.Atan2(_facing.Y, _facing.X),
});

// 2) O desenho. Layer acima do player pra o corte aparecer na frente dele.
effect.Add(new SpriteRenderer(texture, EffectLayer)
{
    Size = new Vector2(EffectSize, EffectSize),
});

// 3) A animação.
effect.Add(new Animator
{
    FrameWidth = frameSize,
    FrameHeight = frameSize,
    SheetColumns = frames,
    Clips =
    [
        new AnimationClip
        {
            Name = "attack",
            Frames = Enumerable.Range(0, frames).ToArray(),   // [0,1,2,3]
            FrameDuration = FrameDuration,
            Loop = false,          // ← ESSENCIAL
        },
    ],
});

// 4) Quem apaga isso depois.
effect.Add(new AttackEffect { Owner = Entity, Offset = _facing * Reach });
```

Ponto a ponto:

* **`Reach`** empurra o efeito pra frente. Com `Origin` padrão (0.5, 0.5) a entidade é
  desenhada centrada na posição, então `Reach ≈ EffectSize / 2` deixa o corte encostando no
  player. Aumente pra afastar.
* **`Layer`** decide quem fica na frente. Player na 10 → efeito na 11. Se o corte sumir atrás
  do personagem, é isso.
* **`Loop = false`** é o que faz o `Animator` marcar `IsFinished` no último quadro. Com loop,
  o corte pisca pra sempre e a entidade nunca morre.
* **O `Animator` toca o primeiro clipe da lista sozinho** no `Start` — não precisa chamar
  `Play("attack")`.
* **O `Animator` exige `SpriteRenderer` na mesma entidade** (é o `SourceRect` dele que o
  Animator reescreve a cada quadro).

### 4.4 Fazer o efeito sumir

Sem isso, **cada clique deixa uma entidade parada no mapa pra sempre**. Depois de dez minutos
de jogo são milhares.

```csharp
[SceneScript]
public sealed class AttackEffect : Behavior
{
    public Entity? Owner;          // nullable: um Entity default não aponta pra World nenhum
    public Vector2 Offset;
    public float MaxLife = 1.5f;   // rede de segurança

    private float _age;

    public override void Update(float deltaTime)
    {
        _age += deltaTime;

        var transform = Get<Transform>();
        if (transform is null || World is null)
            return;

        // Gruda no dono: sem isso, andar durante o golpe deixa o corte pra trás.
        if (Owner is { IsAlive: true } owner && owner.Get<Transform>() is { } ownerTransform)
            transform.Position = ownerTransform.Position + Offset;

        bool finished = Get<Animator>() is { IsFinished: true };

        if (finished || _age >= MaxLife)
            World.Destroy(Entity);
    }
}
```

O `MaxLife` existe porque um dia alguém troca o clipe pra `Loop = true` e o `IsFinished`
nunca vem. Timer de segurança custa um `float`.

> **`World.Destroy` é adiado**, não imediato: a entidade sai no fim do frame. É por isso que
> dá pra destruir a si mesmo dentro do próprio `Update` sem quebrar nada.

---

## 5. Dano (opcional)

O efeito é só visual. Pra machucar, varra quem tem `Health` perto do ponto do golpe:

```csharp
private void HitAround(Vector2 center)
{
    foreach (var (target, _) in World!.Query<Health>())
    {
        if (target.Id == Entity.Id)          // não se acerta
            continue;

        if (target.Get<Transform>() is { } t && Vector2.Distance(t.Position, center) <= DamageRadius)
            World.Damage(target, Damage, Entity);
    }
}
```

`World.Damage` respeita `Health.Invulnerable` e os i-frames (`InvulnerabilityAfterHit`), e
chama `OnDamaged`/`OnDeath` nos scripts do alvo. Não mexa em `Health.Current` na mão.

Pra muitos inimigos, trocar o `Query<Health>` por um `Collider` de gatilho no efeito escala
melhor — mas até algumas centenas de entidades a varredura direta é mais simples e não pesa.

---

## 6. Usando os scripts

### Em código

```csharp
var player = World.CreateEntity("Player");
player.Add(new Transform(Vector2.Zero));
player.Add(new SpriteRenderer(Assets.LoadTexture("sprites/farmer.png"), layer: 10) { Size = new Vector2(46f, 46f) });
player.Add(new Collider { Shape = ColliderShape.Box, Width = 26f, Height = 18f });
player.Add(new PlayerMove());
player.Add(new PlayerAttack());
```

### No editor

1. Painel **SCRIPTS** → **"+ Novo…"**, template **Movimento** ou **Vazio**, cole o código.
2. **Ctrl+S** → o script aparece na hora no "+Add Componente".
3. Selecione a entidade do player → **+Add Componente** → `PlayerAttack`.
4. Os campos públicos aparecem no Inspector: `Cooldown`, `Reach`, `EffectSize`,
   `EffectTexture`, `SnapToEightDirections`, `Damage`…

Só campos `float`, `int`, `bool` e `string` viram campo de Inspector. `Vector2`, `Entity` e
`Texture2D` não — por isso `EffectTexture` é uma **string** de caminho, e não uma textura:
assim dá pra trocar o efeito pelo Inspector, sem recompilar.

---

## 7. Variações comuns

**Golpe solto no lugar (não gruda no player)** — tire o `Owner`:

```csharp
effect.Add(new AttackEffect { MaxLife = 0.4f });
```

**Sheet diferente por direção** (arte de cima/baixo/lado, estilo RPG Maker) — em vez de
girar, escolha o clipe:

```csharp
string clip = MathF.Abs(_facing.Y) > MathF.Abs(_facing.X)
    ? (_facing.Y > 0 ? "down" : "up")
    : "side";
effect.Get<Animator>()!.Play(clip);
effect.Get<SpriteRenderer>()!.FlipX = _facing.X < 0f;
```

**Som no golpe:**

```csharp
World.Audio?.Play("sounds/slash.wav");
```

**Partícula junto** (poeira, faísca) — ver
[GUIA-AGUA-LAVA-SANGUE.md](GUIA-AGUA-LAVA-SANGUE.md), seção "Sangue", que tem o padrão de
rajada única com `ParticleEmitter` + script `OneShot`.

**Combo de 2 golpes** — guarde um contador e o instante do último ataque:

```csharp
if (_comboTimer > 0f) _combo = (_combo + 1) % 2;
else                  _combo = 0;
_comboTimer = 0.6f;
// use _combo pra escolher o clipe ou espelhar o sheet (FlipY = _combo == 1)
```

---

## 8. Quando não funciona

| Sintoma | Causa quase certa |
|---|---|
| Nada acontece no clique | Script não foi anexado; ou `Cooldown` altíssimo; ou está lendo `IsMouseDown` esperando comportamento de `WasMouseClicked` |
| O corte aparece atrás do personagem | `EffectLayer` menor ou igual ao `Layer` do `SpriteRenderer` do player |
| O corte aponta sempre pro mesmo lado | A arte não aponta pra direita, ou faltou o `Rotation = MathF.Atan2(...)` |
| Sai torto quando o player anda | Usou `MousePosition` sem `Camera.ScreenToWorld` |
| O corte fica na tela pra sempre | `Loop = true` no clipe, ou faltou o `AttackEffect` |
| O jogo engasga depois de uns minutos | Efeitos não estão sendo destruídos — confira o `MaxLife` |
| A animação não roda (fica no quadro 0) | Falta `SpriteRenderer` na entidade, ou `FrameWidth`/`SheetColumns` errados |
| Clicar num botão do HUD também ataca | A UI **não consome** o clique do `InputManager` (ver abaixo) |

**O clique do HUD:** `UIManager` marca `UiButton.Clicked`, mas o `input.WasMouseClicked`
continua true no mesmo frame. Pra não atacar ao apertar um botão da interface, teste o botão
por nome antes:

```csharp
if (World.UI?.Find<UiButton>("Hud", "ActionButton") is { Clicked: true })
    return;   // o clique era da UI
```

---

## 9. Os arquivos prontos

`Scripts/PlayerAttack.cs` — movimento não incluso (fica no `PlayerMove` da seção 1, ou no seu
controller existente; os dois convivem na mesma entidade):

```csharp
using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;
using Silk.NET.Input;

namespace MeuJogo;

[SceneScript]
public sealed class PlayerAttack : Behavior
{
    public string EffectTexture = "sprites/slash.png";
    public float Cooldown = 0.35f;
    public float Reach = 26f;
    public float EffectSize = 52f;
    public float FrameDuration = 0.05f;
    public int EffectLayer = 11;
    public int EffectFrames;                 // 0 = descobre sozinho
    public bool SnapToEightDirections;
    public float Damage;                      // 0 = só visual
    public float DamageRadius = 34f;

    private float _cooldownTimer;
    private Vector2 _facing = new(0f, 1f);

    public override void Update(float deltaTime)
    {
        _cooldownTimer -= deltaTime;

        var input = World?.Input;
        var transform = Get<Transform>();
        if (World is null || input is null || transform is null)
            return;

        var move = new Vector2(input.AxisX, input.AxisY);
        if (move.LengthSquared() > 0.0001f)
            _facing = Vector2.Normalize(move);

        if (!input.WasMouseClicked(MouseButton.Left) || _cooldownTimer > 0f)
            return;

        if (AimFromMouse(transform.Position) is { } aim)
            _facing = aim;

        _cooldownTimer = Cooldown;
        SpawnEffect(transform.Position);

        if (Damage > 0f)
            HitAround(transform.Position + _facing * Reach);
    }

    private Vector2? AimFromMouse(Vector2 origin)
    {
        if (World?.Camera is not { } camera || World.Input is not { } input)
            return null;

        var direction = camera.ScreenToWorld(input.MousePosition) - origin;
        if (direction.LengthSquared() < 0.01f)
            return null;

        direction = Vector2.Normalize(direction);
        return SnapToEightDirections ? Snap(direction) : direction;
    }

    private static Vector2 Snap(Vector2 direction)
    {
        float step = MathF.PI / 4f;
        float angle = MathF.Round(MathF.Atan2(direction.Y, direction.X) / step) * step;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    private void SpawnEffect(Vector2 origin)
    {
        var texture = World!.Assets?.LoadTexture(EffectTexture);
        if (texture is null)
            return;

        int frameSize = texture.Height;
        int frames = EffectFrames > 0 ? EffectFrames : Math.Max(1, texture.Width / Math.Max(1, frameSize));

        var effect = World.CreateEntity("AttackEffect");

        effect.Add(new Transform(origin + _facing * Reach)
        {
            Rotation = MathF.Atan2(_facing.Y, _facing.X),
        });

        effect.Add(new SpriteRenderer(texture, EffectLayer)
        {
            Size = new Vector2(EffectSize, EffectSize),
        });

        effect.Add(new Animator
        {
            FrameWidth = frameSize,
            FrameHeight = frameSize,
            SheetColumns = frames,
            Clips =
            [
                new AnimationClip
                {
                    Name = "attack",
                    Frames = Enumerable.Range(0, frames).ToArray(),
                    FrameDuration = FrameDuration,
                    Loop = false,
                },
            ],
        });

        effect.Add(new AttackEffect { Owner = Entity, Offset = _facing * Reach });
    }

    private void HitAround(Vector2 center)
    {
        foreach (var (target, _) in World!.Query<Health>())
        {
            if (target.Id == Entity.Id)
                continue;

            if (target.Get<Transform>() is { } targetTransform
                && Vector2.Distance(targetTransform.Position, center) <= DamageRadius)
            {
                World.Damage(target, Damage, Entity);
            }
        }
    }
}
```

`Scripts/AttackEffect.cs` — o da seção 4.4, igual.

Rodando: `dotnet run --project samples/Aurora.Farm` e clique com o botão esquerdo.
