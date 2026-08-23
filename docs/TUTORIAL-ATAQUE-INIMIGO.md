# Tutorial passo a passo — golpe no clique e inimigo que persegue

Construção do zero, em 14 passos, de duas coisas que todo RPG de ação precisa: **um golpe
corpo-a-corpo instanciado no clique do mouse** e **um inimigo que nasce, persegue o jogador,
machuca no contato e morre**.

Cada passo acrescenta poucas linhas e produz algo visível na tela — inclusive os passos em que
o resultado é "aparece, mas errado", que é onde se aprende o motivo da linha seguinte existir.
**Rode o jogo a cada passo.** Pular etapa transforma um erro pequeno e óbvio em três erros
misturados.

Ao final você terá quatro arquivos em `Scripts/`:

| Arquivo | Papel |
|---|---|
| `PlayerController.cs` | anda, e no clique instancia o golpe |
| `Knife.cs` | o golpe instanciado: acompanha o dono e se apaga sozinho |
| `EnemyController.cs` | persegue o jogador e machuca no contato |
| `EnemySpawner.cs` | faz nascer inimigo de tempos em tempos |

Os arquivos completos estão na seção final, para comparar com o seu.

**Pré-requisitos:** um projeto rodando com um `Player` na cena (`Transform` + `SpriteRenderer`),
uma câmera seguindo ele, e o `slash.png` em `Assets/sprites/` (pode copiar de
`samples/Aurora.Farm/Assets/sprites/slash.png` — 192×48, quatro quadros de 48×48).

Referência de qualquer componente citado aqui: [REFERENCIA-SCRIPTS-RPG.md](REFERENCIA-SCRIPTS-RPG.md).
Todos esses sistemas montados juntos num jogo de sobrevivência completo:
[GUIA-RPG-SURVIVOR.md](GUIA-RPG-SURVIVOR.md).

---

# Parte 1 — O golpe no clique

## Passo 1 — o esqueleto e o clique

`Scripts/PlayerController.cs`:

```csharp
using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;
using Silk.NET.Input;

namespace MeuJogo;

[SceneScript]
public sealed class PlayerController : Behavior
{
    public float Speed = 100f;

    public override void Update(float deltaTime)
    {
        var input = World?.Input;
        var transform = Get<Transform>();

        if (input is null || transform is null)
            return;

        var move = new Vector2(input.AxisX, input.AxisY);

        if (move.LengthSquared() > 0f)
        {
            move = Vector2.Normalize(move);
            transform.Position += move * Speed * deltaTime;
        }

        if (input.WasMouseClicked(MouseButton.Left))
            Console.WriteLine("cliquei");
    }
}
```

Anexe na entidade do jogador — `player.Add(new PlayerController());` em código, ou
"+Add Componente" no Inspector.

**Teste:** anda com WASD; cada clique imprime uma linha no console (a janela de terminal que
abre junto do jogo, ou o terminal do `dotnet run`).

**Aprendeu:** `WasMouseClicked()` é *neste frame*, como `WasKeyPressed`. Com `IsMouseDown()`
sairiam 60 linhas por segundo. E `MouseButton`/`Key` vêm do **`Silk.NET.Input`** — sem esse
`using`, o compilador diz que `MouseButton` não existe. (`WasMouseClicked()` sem argumento já
usa o botão esquerdo, se preferir não depender do enum.)

---

## Passo 2 — descobrir para onde o golpe vai

O mouse chega em **pixel de tela**; o mundo tem outra régua, porque a câmera anda e dá zoom.
Quem traduz uma na outra é a câmera.

```csharp
if (!input.WasMouseClicked(MouseButton.Left))
    return;

var target = World.Camera?.ScreenToWorld(input.MousePosition) ?? transform.Position;
var direction = target - transform.Position;

if (direction.LengthSquared() < 0.01f)
    return;                                  // cursor em cima do player: direção indefinida

direction = Vector2.Normalize(direction);

Console.WriteLine($"tela={input.MousePosition} mundo={target} dir={direction}");
```

Inclua `World is null` na guarda lá em cima, já que agora você usa `World.Camera`:

```csharp
if (World is null || input is null || transform is null)
    return;
```

**Teste:** ande até a câmera sair da origem e clique de novo. Compare `tela=` com `mundo=` —
os números divergem, e é essa diferença que faz o golpe sair torto de quem esquece o
`ScreenToWorld`.

**Aprendeu:** `Normalize` devolve só a **direção** (comprimento 1). Sem ele, clicar longe daria
um vetor gigante e o golpe nasceria fora da tela.

---

## Passo 3 — instanciar alguma coisa

Entidade nova é criar e adicionar componentes. Comece com o mínimo que dá para ver: onde está e
o que desenha.

```csharp
public string AtkSprite = "sprites/slash.png";
public float AtkDistance = 26f;

// no lugar do Console.WriteLine:
var atkTexture = World.Assets?.LoadTexture(AtkSprite);

if (atkTexture is null)
    return;

var efeito = World.CreateEntity("Golpe");
efeito.Add(new Transform(transform.Position + direction * AtkDistance));
efeito.Add(new SpriteRenderer(atkTexture, layer: 11) { Size = new Vector2(52f, 52f) });
```

**Teste:** clique. Aparece uma imagem larga e esticada à frente do jogador — e **fica lá para
sempre**. Clique dez vezes: dez entidades paradas no cenário.

**Aprendeu duas coisas de uma vez:**

- `layer: 11` precisa ser **maior** que a layer do `SpriteRenderer` do jogador, senão o corte
  sai desenhado por baixo dele.
- O PNG é um *sprite sheet*: 192×48 são quatro quadros de 48×48 lado a lado. Sem `Animator`, o
  `SpriteRenderer` desenha a tira inteira. Aquele borrão é o sheet cru — sintoma, não bug.

---

## Passo 4 — fazer o efeito morrer

Nada na engine apaga a entidade sozinha. Quem apaga é um `Behavior` na **própria entidade do
efeito**. Arquivo novo, `Scripts/Knife.cs`:

```csharp
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Scenes;

namespace MeuJogo;

[SceneScript]
public sealed class Knife : Behavior
{
    public float Life = 0.25f;

    private float _age;

    public override void Update(float deltaTime)
    {
        _age += deltaTime;

        if (_age >= Life)
            World?.Destroy(Entity);
    }
}
```

E mais uma linha no `PlayerController`, junto das outras: `efeito.Add(new Knife());`

**Teste:** o corte aparece e some em 0,25s. Clique muito: nada acumula no cenário.

**Aprendeu:** cada entidade é dona da própria vida. Esse padrão — script curto que conta tempo e
se destrói — serve para projétil, explosão, dano no chão, texto flutuante de dano, tudo.

---

## Passo 5 — cooldown

Clicando rápido o golpe vira metralhadora. O controle é um cronômetro regressivo.

```csharp
public float Cooldown = 0.35f;
private float _timer;

public override void Update(float deltaTime)
{
    _timer -= deltaTime;          // PRIMEIRA linha do Update, fora de qualquer if

    // ...

    if (input.WasMouseClicked(MouseButton.Left) && _timer <= 0f)
    {
        // ...direção, textura, entidade...

        _timer = Cooldown;        // por último: só recarrega quando o golpe realmente saiu
    }
}
```

**Teste:** clique o mais rápido que conseguir — no máximo ~3 golpes por segundo. Para ter
certeza de que o portão funciona, ponha `Cooldown = 2f` por um minuto: o intervalo fica óbvio.

**Aprendeu:** dois detalhes que quebram cooldown na prática.

1. É `_timer -= deltaTime`, **não** `_timer = deltaTime`. Com `=`, o timer vale ~0,016 todo
   frame, `_timer <= 0f` nunca é verdade e o ataque **nunca sai**. É um relógio parado no lugar
   de um regressivo.
2. O `-=` fica **fora** do `if`. Timer que só desconta dentro da condição nunca chega a zero.

E o `_timer = Cooldown` no fim, depois dos `return` de direção indefinida e textura ausente:
clique que não gerou golpe nenhum não deve comer o cooldown.

---

## Passo 6 — virar o golpe para a direção

`Transform.Rotation` é em **radianos**, e `0` aponta para a **direita** — a mesma convenção com
que o `slash.png` foi desenhado.

```csharp
efeito.Add(new Transform(transform.Position + direction * AtkDistance)
{
    Rotation = MathF.Atan2(direction.Y, direction.X),
});
```

**Teste:** clique em volta do jogador. O corte acompanha o cursor.

**Aprendeu:** `Atan2(y, x)` converte vetor em ângulo. Para o RPG clássico de 8 direções,
arredonde para múltiplos de 45° antes de usar:

```csharp
float step = MathF.PI / 4f;
float angle = MathF.Round(MathF.Atan2(direction.Y, direction.X) / step) * step;
direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
```

---

## Passo 7 — animar (resolve o borrão do passo 3)

O `Animator` recorta o sheet e troca de quadro. Ele precisa do tamanho do quadro e de quantos
existem — e dá para deduzir os dois, já que os quadros são quadrados: o lado é a **altura** da
textura.

```csharp
int side = atkTexture.Height;                        // 48
int frames = Math.Max(1, atkTexture.Width / side);   // 192/48 = 4

efeito.Add(new Animator
{
    FrameWidth = side,
    FrameHeight = side,
    SheetColumns = frames,
    Clips =
    [
        new AnimationClip
        {
            Name = "golpe",
            Frames = Enumerable.Range(0, frames).ToArray(),   // [0,1,2,3]
            FrameDuration = 0.05f,
            Loop = false,        // sem isto o corte fica piscando para sempre
        },
    ],
});
```

Agora o `Knife` pode morrer no fim da animação em vez de por cronômetro — mais preciso, e
funciona igual se você trocar por um sheet de 8 quadros:

```csharp
public float Life = 1.5f;        // vira só rede de segurança

public override void Update(float deltaTime)
{
    _age += deltaTime;

    bool finished = Get<Animator>() is { IsFinished: true };

    if (finished || _age >= Life)
        World?.Destroy(Entity);
}
```

**Teste:** quatro quadros em 0,2s, e some.

**Aprendeu:** `IsFinished` só vira `true` em clipe com `Loop = false`. Mantenha o teto de tempo
mesmo assim: se um dia o clipe virar `Loop = true`, sem ele a entidade vazaria para sempre.

---

## Passo 8 — grudar o efeito no dono

Ande enquanto o corte está na tela: ele fica para trás, plantado onde nasceu. O efeito precisa
seguir quem golpeou.

Em `Knife.cs`:

```csharp
/// <summary>Quem golpeou. Nullable de propósito: um Entity default não aponta pra World
/// nenhum e estoura ao ler IsAlive.</summary>
public Entity? Owner;
public Vector2 Offset;

public override void Update(float deltaTime)
{
    _age += deltaTime;

    if (Owner is { IsAlive: true } owner
        && owner.Get<Transform>() is { } ownerTransform
        && Get<Transform>() is { } mine)
    {
        mine.Position = ownerTransform.Position + Offset;
    }

    // ...resto igual...
}
```

E no `PlayerController`: `efeito.Add(new Knife { Owner = Entity, Offset = direction * AtkDistance });`

**Aprendeu:** `Entity` e `Vector2` **não aparecem no Inspector** — só `float`, `int`, `bool` e
`string` aparecem. E não precisam aparecer: quem preenche esses campos é o script que criou a
entidade, não o editor.

A Parte 1 acaba aqui. O que você escreveu é, linha por linha, o que existe em
[PlayerAttack.cs](../samples/Aurora.Farm/Scripts/PlayerAttack.cs) e
[AttackEffect.cs](../samples/Aurora.Farm/Scripts/AttackEffect.cs) — compare, você vai reconhecer
cada pedaço.

---

# Parte 2 — O inimigo

## Passo 9 — fazer um inimigo nascer

Primeiro um alvo parado, para ter o que perseguir e o que acertar. No `OnLoad` do seu `Game`
(ou monte a entidade no editor, dá no mesmo):

```csharp
private void SpawnEnemy(Vector2 position)
{
    var enemy = World.CreateEntity("Inimigo");
    enemy.Add(new Transform(position));
    enemy.Add(new SpriteRenderer(Assets.LoadTexture("sprites/slime.png"), layer: 10)
    {
        Size = new Vector2(28f, 28f),
    });

    // Collider menor que o sprite e deslocado pros pés: em jogo de cima, o corpo colide e
    // a cabeça passa por cima do cenário.
    enemy.Add(new Collider { Shape = ColliderShape.Box, Width = 20f, Height = 14f, Offset = new Vector2(0f, 8f) });
}
```

Chame com `SpawnEnemy(new Vector2(200f, 0f));`.

**Teste:** o bicho aparece parado, e você esbarra nele em vez de atravessar.

**Aprendeu:** `Collider` com `IsSolid = true` (o padrão) empurra fisicamente. Se quiser que ele
atravesse e só avise, é `IsSolid = false` — aí em vez de `OnCollision` você recebe
`OnTriggerEnter`/`OnTriggerExit`.

---

## Passo 10 — seguir o jogador (versão na mão)

Antes de usar o pathfinding da engine, faça o movimento à mão — são quatro linhas e é o que
deixa claro o que o `NavAgent` faz por você depois.

`Scripts/EnemyController.cs`:

```csharp
using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace MeuJogo;

[SceneScript]
public sealed class EnemyController : Behavior
{
    public string TargetName = "Player";
    public float Speed = 60f;

    public override void Update(float deltaTime)
    {
        var transform = Get<Transform>();

        if (World is null || transform is null)
            return;

        // Cada inimigo acha o alvo sozinho — funciona pra quantas cópias a cena tiver,
        // sem nenhuma linha no Game.OnUpdate.
        if (!World.TryFind(TargetName, out var target))
            return;

        if (target.Get<Transform>() is not { } targetTransform)
            return;

        var direction = targetTransform.Position - transform.Position;

        if (direction.LengthSquared() < 1f)
            return;                                  // já está em cima: parar evita tremedeira

        direction = Vector2.Normalize(direction);
        transform.Position += direction * Speed * deltaTime;
    }
}
```

Adicione `enemy.Add(new EnemyController());` no `SpawnEnemy`.

**Teste:** ele vem atrás de você. Agora ponha uma parede entre os dois (qualquer entidade com
`Collider` sólido e `IsKinematic = true`): o inimigo **gruda na parede** e fica empurrando, sem
contornar.

**Aprendeu:** o nome `"Player"` na busca precisa bater **exatamente** com o nome da entidade do
jogador. E `TryFind` devolve só o primeiro com aquele nome — para achar vários do mesmo tipo,
o caminho é `World.Query<T>()`.

---

## Passo 11 — trocar por `NavAgent` (contorna parede)

O `NavAgent` faz o movimento e o desvio dentro do `World.Update`. O script só decide **para
onde** ir.

No `SpawnEnemy`, some o componente:

```csharp
enemy.Add(new NavAgent { Speed = 60f, ArriveThreshold = 6f });
```

E o `Update` do `EnemyController` vira:

```csharp
public string TargetName = "Player";
public float SightRange = 220f;
/// <summary>Segundos entre dois recálculos de caminho. Ver a nota abaixo.</summary>
public float RepathInterval = 0.25f;

private float _repathTimer;

public override void Update(float deltaTime)
{
    _repathTimer -= deltaTime;

    var nav = Get<NavAgent>();
    var transform = Get<Transform>();

    if (World is null || nav is null || transform is null)
        return;

    if (!World.TryFind(TargetName, out var target) || target.Get<Transform>() is not { } targetTransform)
    {
        nav.Stop();
        return;
    }

    if (_repathTimer > 0f)
        return;

    _repathTimer = RepathInterval;

    if (Vector2.Distance(transform.Position, targetTransform.Position) <= SightRange)
        nav.SetTarget(targetTransform.Position);      // persegue
    else
        nav.Stop();                                    // perdeu de vista: para
}
```

**Teste:** a mesma parede de antes — agora ele contorna. E fuja para longe: passando de
`SightRange` ele para de seguir.

**Aprendeu três coisas sobre o `NavAgent`:**

- Ele só desvia se existir na cena um `Tilemap` com `SolidTiles` preenchido — é dali que sai a
  grade do A*. Sem isso (ou fora da área do tilemap), ele anda **reto** até o alvo, igual ao
  passo 10.
- Cada `SetTarget` **descarta o caminho e recalcula** no próximo frame. Chamar todo frame
  funciona, mas com muitos inimigos é A* rodando 60 vezes por segundo por bicho — daí o
  `RepathInterval`. Um quarto de segundo de atraso é invisível para quem joga.
- Ao chegar no fim do caminho ele zera `HasTarget` sozinho; `IsMoving` conta essa mesma
  história, útil para alternar animação parado/andando.

---

## Passo 12 — machucar no contato

O jogador precisa de `Health` para poder levar dano:

```csharp
player.Add(new Health
{
    Max = 100f,
    Current = 100f,
    InvulnerabilityAfterHit = 0.6f,   // i-frames: sem isso, encostar num inimigo mata em meio segundo
    DestroyOnDeath = false,            // o jogador não some da cena ao morrer
});
```

E no `EnemyController`:

```csharp
public float ContactDamage = 8f;
/// <summary>Segundos entre duas mordidas.</summary>
public float AttackInterval = 0.8f;

private float _attackTimer;

// (no início do Update, junto do outro cronômetro)
// _attackTimer -= deltaTime;

public override void OnCollision(Entity other, CollisionInfo info)
{
    if (_attackTimer > 0f || other.Name != TargetName || !other.Has<Health>())
        return;

    _attackTimer = AttackInterval;
    World?.Damage(other, ContactDamage, Entity);
}
```

**Teste:** deixe o bicho te alcançar. A vida cai em mordidas espaçadas, não de uma vez.

**Aprendeu:** `OnCollision` dispara **todo frame** enquanto os dois estão encostados — por isso
o intervalo próprio. Os i-frames do `Health` já seguram parte disso, mas o timer no inimigo
deixa o ritmo do ataque explícito e ajustável por bicho. E `World.Damage` é sempre o caminho:
ele respeita i-frames e dispara `OnDamaged`/`OnDeath` nos scripts do alvo — escrever em
`Health.Current` na mão pula tudo isso.

---

## Passo 13 — o seu golpe machuca (e o inimigo morre)

Feche o ciclo. Primeiro dê `Health` ao inimigo, no `SpawnEnemy`:

```csharp
enemy.Add(new Health { Max = 50f, Current = 50f, InvulnerabilityAfterHit = 0.15f });
```

Depois, no `PlayerController`, o golpe passa a varrer quem está perto do ponto atingido:

```csharp
public float Damage = 20f;
public float DamageRadius = 34f;

// ...logo depois de criar o efeito, ainda dentro do if do clique:
var center = transform.Position + direction * AtkDistance;

foreach (var (other, _) in World.Query<Health>())
{
    if (other.Id == Entity.Id)
        continue;                                   // não bater em si mesmo

    if (other.Get<Transform>() is { } otherTransform
        && Vector2.Distance(otherTransform.Position, center) <= DamageRadius)
    {
        World.Damage(other, Damage, Entity);
    }
}
```

E o inimigo pode reagir, no `EnemyController`:

```csharp
public override void OnDamaged(float amount, Entity? source)
{
    // Levou porrada? Vem pra cima de quem bateu, mesmo que estivesse fora do alcance de visão.
    if (source is { IsAlive: true } attacker && attacker.Get<Transform>() is { } t)
    {
        Get<NavAgent>()?.SetTarget(t.Position);
        _repathTimer = RepathInterval;
    }
}

public override void OnDeath()
{
    World?.Audio?.Play("audio/enemy_die.wav");
    // Aqui é o lugar de largar item/moeda: OnDeath roda ANTES da entidade ser destruída,
    // então a posição ainda é legível.
}
```

**Teste:** três golpes derrubam o bicho (50 de vida, 20 por golpe).

**Aprendeu:** ataque corpo-a-corpo por **varredura de distância** é mais previsível do que
depender de colisão — o golpe dura poucos frames, e um trigger pode simplesmente não coincidir
com eles. E `InvulnerabilityAfterHit` no inimigo, mesmo curtinho (0,15s), é o que impede um
único golpe de contar em vários frames seguidos e matar na hora.

---

## Passo 14 — vários inimigos

Um "diretor" de partida: uma entidade vazia com um script que faz nascer bicho de tempos em
tempos, num anel ao redor do jogador — longe o bastante para não aparecer do nada na tela.

`Scripts/EnemySpawner.cs`:

```csharp
using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace MeuJogo;

[SceneScript]
public sealed class EnemySpawner : Behavior
{
    public string TargetName = "Player";
    public string EnemySprite = "sprites/slime.png";
    public float SpawnInterval = 3f;
    public int MaxAlive = 15;
    public float DistanceMin = 320f;
    public float DistanceMax = 420f;
    public float EnemyHealth = 50f;
    public float EnemySpeed = 60f;

    private float _timer;

    public override void Update(float deltaTime)
    {
        _timer -= deltaTime;

        if (World is null || _timer > 0f)
            return;

        _timer = SpawnInterval;

        if (!World.TryFind(TargetName, out var player) || player.Get<Transform>() is not { } playerTransform)
            return;

        if (CountAlive() >= MaxAlive)
            return;                                  // teto: protege o frame rate

        float angle = Random.Shared.NextSingle() * MathF.Tau;
        float distance = DistanceMin + Random.Shared.NextSingle() * (DistanceMax - DistanceMin);
        var position = playerTransform.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;

        Spawn(position);
    }

    private int CountAlive()
    {
        int count = 0;

        foreach (var _ in World!.Query<EnemyController>())
            count++;

        return count;
    }

    private void Spawn(Vector2 position)
    {
        var texture = World!.Assets?.LoadTexture(EnemySprite);

        if (texture is null)
            return;

        var enemy = World.CreateEntity("Inimigo");
        enemy.Add(new Transform(position));
        enemy.Add(new SpriteRenderer(texture, layer: 10) { Size = new Vector2(28f, 28f) });
        enemy.Add(new Collider { Shape = ColliderShape.Box, Width = 20f, Height = 14f, Offset = new Vector2(0f, 8f) });
        enemy.Add(new Health { Max = EnemyHealth, Current = EnemyHealth, InvulnerabilityAfterHit = 0.15f });
        enemy.Add(new NavAgent { Speed = EnemySpeed, ArriveThreshold = 6f });
        enemy.Add(new EnemyController { TargetName = TargetName });
    }
}
```

Ligar é uma entidade só, no `OnLoad`:

```csharp
var director = World.CreateEntity("Diretor");
director.Add(new EnemySpawner());
```

**Teste:** deixe rodando um minuto. Os bichos aparecem fora da tela e convergem para você.

**Aprendeu:** `Query<EnemyController>()` contando as cópias vivas é o jeito barato de pôr teto
no spawn. Sem esse teto, um jogo deixado aberto acumula entidades até engasgar — e o culpado é
sempre difícil de achar depois.

Dificuldade crescente é uma variável a mais: guarde uma `Onda` no `GameState`, suba a cada X
segundos e multiplique `EnemyHealth`/`EnemySpeed` por ela.

---

# Erros comuns

| Sintoma | Causa |
|---|---|
| `MouseButton`/`Key` não existe | falta `using Silk.NET.Input;` (os templates Movimento/Item/Vazio do editor não trazem) |
| O ataque nunca sai | `_timer = deltaTime` em vez de `_timer -= deltaTime` — o timer nunca chega a zero |
| Cooldown não segura nada | o `-=` está dentro do `if`, então só desconta quando você clica |
| Um clique só come vários cooldowns | `_timer = Cooldown` antes dos `return` de validação |
| Golpe sai torto quando a câmera anda | mira sem `Camera.ScreenToWorld` (mouse vem em pixel de tela) |
| Golpe sai sempre para a direita | faltou o `Rotation` no `Transform`, ou o sheet não aponta para a direita |
| Corte aparece esticado/borrado | falta `Animator`: o `SpriteRenderer` está desenhando o sheet inteiro |
| Corte fica piscando para sempre | clipe com `Loop = true`, então `IsFinished` nunca vira `true` |
| Corte fica para trás quando você anda | faltou `Owner`/`Offset` no `Knife` |
| Entidades de golpe acumulando | esqueceu o `Knife` (nada na engine destrói entidade sozinha) |
| Corte desenhado por baixo do player | `layer` do efeito menor ou igual ao do jogador |
| Inimigo não persegue | nome em `TargetName` diferente do nome real da entidade do jogador |
| Inimigo gruda na parede | está no passo 10 (movimento na mão); use `NavAgent` |
| `NavAgent` não desvia de nada | não há `Tilemap` com `SolidTiles` preenchido na cena |
| Um golpe mata o inimigo na hora | `InvulnerabilityAfterHit = 0`: o mesmo golpe conta em vários frames |
| Encostar no inimigo mata o jogador | falta `InvulnerabilityAfterHit` no `Health` dele |
| O jogador some ao morrer | `DestroyOnDeath` ficou `true` no `Health` do jogador |
| Script não aparece no "+Add Componente" | falta `[SceneScript]`, classe não é `sealed`, ou tem construtor com parâmetro |
| Campo não aparece no Inspector | o tipo não é `float`/`int`/`bool`/`string` |

---

# Arquivos completos

## `Scripts/PlayerController.cs`

```csharp
using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Scenes;
using Silk.NET.Input;

namespace MeuJogo;

[SceneScript]
public sealed class PlayerController : Behavior
{
    public float Speed = 100f;

    public string AtkSprite = "sprites/slash.png";
    public float AtkDistance = 26f;
    public float AtkSize = 52f;
    public float FrameDuration = 0.05f;
    public float Cooldown = 0.35f;
    public float Damage = 20f;
    public float DamageRadius = 34f;

    private float _timer;

    public override void Update(float deltaTime)
    {
        _timer -= deltaTime;

        var input = World?.Input;
        var transform = Get<Transform>();

        if (World is null || input is null || transform is null)
            return;

        Move(input, transform, deltaTime);

        if (!input.WasMouseClicked(MouseButton.Left) || _timer > 0f)
            return;

        var target = World.Camera?.ScreenToWorld(input.MousePosition) ?? transform.Position;
        var direction = target - transform.Position;

        if (direction.LengthSquared() < 0.01f)
            return;

        direction = Vector2.Normalize(direction);

        var atkTexture = World.Assets?.LoadTexture(AtkSprite);

        if (atkTexture is null)
            return;

        _timer = Cooldown;

        var center = transform.Position + direction * AtkDistance;

        SpawnSlash(atkTexture, center, direction);
        Hit(center);
    }

    private void Move(Aurora.Runtime.Input.InputManager input, Transform transform, float deltaTime)
    {
        var move = new Vector2(input.AxisX, input.AxisY);

        if (move.LengthSquared() > 0f)
        {
            move = Vector2.Normalize(move);
            transform.Position += move * Speed * deltaTime;

            if (Get<SpriteRenderer>() is { } sprite && MathF.Abs(move.X) > 0.01f)
                sprite.FlipX = move.X < 0f;
        }

        Get<Animator>()?.SetFloat("Speed", move.Length() * Speed);
    }

    private void SpawnSlash(Texture2D texture, Vector2 center, Vector2 direction)
    {
        int side = texture.Height;
        int frames = Math.Max(1, texture.Width / side);

        var efeito = World!.CreateEntity("Golpe");

        efeito.Add(new Transform(center)
        {
            Rotation = MathF.Atan2(direction.Y, direction.X),
        });

        efeito.Add(new SpriteRenderer(texture, layer: 11) { Size = new Vector2(AtkSize, AtkSize) });

        efeito.Add(new Animator
        {
            FrameWidth = side,
            FrameHeight = side,
            SheetColumns = frames,
            Clips =
            [
                new AnimationClip
                {
                    Name = "golpe",
                    Frames = Enumerable.Range(0, frames).ToArray(),
                    FrameDuration = FrameDuration,
                    Loop = false,
                },
            ],
        });

        efeito.Add(new Knife { Owner = Entity, Offset = direction * AtkDistance });
    }

    private void Hit(Vector2 center)
    {
        foreach (var (other, _) in World!.Query<Health>())
        {
            if (other.Id == Entity.Id)
                continue;

            if (other.Get<Transform>() is { } otherTransform
                && Vector2.Distance(otherTransform.Position, center) <= DamageRadius)
            {
                World.Damage(other, Damage, Entity);
            }
        }
    }
}
```

## `Scripts/Knife.cs`

```csharp
using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace MeuJogo;

[SceneScript]
public sealed class Knife : Behavior
{
    public Entity? Owner;
    public Vector2 Offset;
    public float Life = 1.5f;

    private float _age;

    public override void Update(float deltaTime)
    {
        _age += deltaTime;

        if (World is null)
            return;

        if (Owner is { IsAlive: true } owner
            && owner.Get<Transform>() is { } ownerTransform
            && Get<Transform>() is { } mine)
        {
            mine.Position = ownerTransform.Position + Offset;
        }

        if (Get<Animator>() is { IsFinished: true } || _age >= Life)
            World.Destroy(Entity);
    }
}
```

## `Scripts/EnemyController.cs`

```csharp
using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace MeuJogo;

[SceneScript]
public sealed class EnemyController : Behavior
{
    public string TargetName = "Player";
    public float SightRange = 220f;
    public float RepathInterval = 0.25f;
    public float ContactDamage = 8f;
    public float AttackInterval = 0.8f;

    private float _repathTimer;
    private float _attackTimer;

    public override void Update(float deltaTime)
    {
        _repathTimer -= deltaTime;
        _attackTimer -= deltaTime;

        var nav = Get<NavAgent>();
        var transform = Get<Transform>();

        if (World is null || nav is null || transform is null)
            return;

        if (!World.TryFind(TargetName, out var target) || target.Get<Transform>() is not { } targetTransform)
        {
            nav.Stop();
            return;
        }

        if (_repathTimer > 0f)
            return;

        _repathTimer = RepathInterval;

        if (Vector2.Distance(transform.Position, targetTransform.Position) <= SightRange)
            nav.SetTarget(targetTransform.Position);
        else
            nav.Stop();
    }

    public override void OnCollision(Entity other, CollisionInfo info)
    {
        if (_attackTimer > 0f || other.Name != TargetName || !other.Has<Health>())
            return;

        _attackTimer = AttackInterval;
        World?.Damage(other, ContactDamage, Entity);
    }

    public override void OnDamaged(float amount, Entity? source)
    {
        if (source is { IsAlive: true } attacker && attacker.Get<Transform>() is { } t)
        {
            Get<NavAgent>()?.SetTarget(t.Position);
            _repathTimer = RepathInterval;
        }
    }

    public override void OnDeath()
    {
        // Lugar de largar moeda/item: a posição ainda é legível aqui.
    }
}
```

`EnemySpawner.cs` está inteiro no passo 14.

---

# Para onde ir agora

- **Custo de stamina no golpe, vida/sede na HUD, itens, construção e fabricação** —
  [GUIA-RPG-SURVIVOR.md](GUIA-RPG-SURVIVOR.md), que usa exatamente esta base.
- **Ataque à distância** — o componente `Projectile` já aplica dano e se destrói sozinho; você
  só preenche `Velocity` e `Source` (seção 6.3 do guia acima).
- **Drop de item ao morrer** — `OnDeath` do `EnemyController` (seção 8 do guia).
- **Combo de ataque** — contar cliques dentro de uma janela de tempo e trocar o clipe do
  `Animator`.
- **Referência de qualquer componente** — [REFERENCIA-SCRIPTS-RPG.md](REFERENCIA-SCRIPTS-RPG.md).
