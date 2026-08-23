# Guia completo — RPG Survivor 2D (top-down)

Guia único, de ponta a ponta, para montar um **RPG de sobrevivência visto de cima** na Aurora:
andar, atacar, construir, fabricar, água, vida, stamina, moedas, itens e inimigos — todos os
sistemas conversando entre si, com os scripts prontos para copiar.

Os outros documentos continuam valendo como consulta pontual; este aqui é o que costura tudo:

| Quando bater dúvida em... | Vá para |
|---|---|
| Assinatura exata de um componente/sistema | [REFERENCIA-SCRIPTS-RPG.md](REFERENCIA-SCRIPTS-RPG.md) |
| Menu, troca de cena, build, Android | [GUIA-JOGO-BASE.md](GUIA-JOGO-BASE.md), [GUIA-ANDROID.md](GUIA-ANDROID.md) |
| Passo a passo comentado de mover/atacar | [TUTORIAL-SCRIPTS-PLAYER.md](TUTORIAL-SCRIPTS-PLAYER.md) |
| Construir golpe e inimigo do zero, em 14 passos | [TUTORIAL-ATAQUE-INIMIGO.md](TUTORIAL-ATAQUE-INIMIGO.md) |
| Tileset de líquido, presets, geração de PNG | [GUIA-AGUA-LAVA-SANGUE.md](GUIA-AGUA-LAVA-SANGUE.md) |
| Mundo procedural gigante (estilo Terraria) | [GUIA-JOGO-SANDBOX.md](GUIA-JOGO-SANDBOX.md) |

### Índice

| | | |
|---|---|---|
| [0. Engine × seu código](#0-o-que-a-engine-já-faz-e-o-que-você-escreve) | [6. Ataque](#6-ataque) | [12. Save](#12-save) |
| [1. Contrato de dados](#1-o-contrato-de-dados-leia-antes-de-escrever-qualquer-script) | [7. Inimigos](#7-inimigos) | [13. Montagem final](#13-montagem-final--survivorgamecs) |
| [2. Esqueleto do projeto](#2-esqueleto-do-projeto) | [8. Itens, drops e moedas](#8-itens-drops-e-moedas) | [14. Checklist de erros](#14-checklist-de-por-que-não-funciona) |
| [3. Ajudante de tiles](#3-ajudante-de-tiles-usado-por-construção-água-e-mineração) | [9. Fabricação](#9-fabricação-craft) | [15. Cheat sheet](#15-cheat-sheet-do-survivor) |
| [4. Mundo, água e construção](#4-o-mundo-terreno-água-e-a-camada-de-construção) | [10. Construção](#10-construção) | [16. O que fica de fora](#16-o-que-este-guia-deixa-de-fora-e-onde-continuar) |
| [5. Jogador: mover, vida, stamina](#5-o-jogador) | [11. HUD](#11-hud) | |

Ordem sugerida de leitura na primeira vez: 1 → 2 → 4 → 5 → 13, rodar o jogo andando, e só
depois voltar pras seções 6 a 10, uma por vez.

---

## 0. O que a engine já faz e o que você escreve

Metade da briga é saber onde parar de programar. A tabela abaixo é o mapa deste guia:

| Sistema | Vem pronto na engine | Você escreve |
|---|---|---|
| Movimento | `Transform`, `Collider`, colisão contra `SolidTiles`, `Input.AxisX/AxisY`, `UiJoystick` | um `Behavior` de ~30 linhas |
| Câmera | `CameraController` (segue por nome, zoom, limites) | nada |
| Vida | `Health` + `World.Damage/Heal` + i-frames + `OnDamaged`/`OnDeath` | regeneração e o que acontece ao morrer |
| Stamina / sede | — | variáveis no `GameState` + um script `Vitals` |
| Ataque | `Animator`, `World.Query<Health>()`, `Projectile` (à distância) | corpo-a-corpo: cooldown + área de dano |
| Inimigos | `NavAgent` (pathfinding e desvio de tile sólido) | decisão de perseguir/atacar, drops |
| Itens / moedas | `InventoryManager` (contagem por nome, entra no save) | o item caído no chão e a coleta |
| Fabricação | `InventoryManager` | tabela de receitas + o "posso fabricar?" |
| Construção | `Tilemap` (`SetTile`, `SolidTiles`) | mira, custo e devolução ao quebrar |
| Água / lava | `LiquidTileset` + `Tilemap.Autotile` + animação | efeito de pisar (beber, queimar) |
| HUD | `UiBar`, `UiText` com tokens, `UiButton`, `UiJoystick` | sincronizar `Health.Current` → variável |
| Save | `SaveManager` (variáveis, switches, inventário, quests, posição do player) | o que você inventou fora disso |

Regra que evita a maior parte dos erros: **componente nativo guarda dado, `Behavior` guarda
decisão.**

---

## 1. O contrato de dados (leia antes de escrever qualquer script)

Todos os sistemas abaixo conversam por **nome de variável** e **nome de item**. Se cada script
inventar o seu, a HUD mostra zero e o craft não acha material. Fixe esta tabela no começo do
projeto — é o único "banco de dados" do jogo.

### Variáveis do `GameState` (números globais, salvos)

| Nome | Faixa | Quem escreve | Quem lê |
|---|---|---|---|
| `Vida` | 0..100 | `Vitals` (espelha `Health.Current`) | `UiBar` da HUD |
| `Stamina` | 0..100 | `Vitals` (gasta em corrida/ataque, regenera) | `UiBar`, `PlayerMove`, `PlayerCombat` |
| `Agua` | 0..100 | `Vitals` (cai com o tempo, enche na lagoa) | `UiBar` |
| `Onda` | 1..∞ | `EnemySpawner` | `UiText` da HUD |

`Vida` é **espelho**, não fonte: quem manda na vida é o componente `Health` (por causa dos
i-frames e do `OnDeath`). O script só copia o valor pra variável, porque `UiBar` lê `GameState`,
não componente.

### Itens do `InventoryManager` (contagem por nome, salvos)

| Item | De onde vem | Pra que serve |
|---|---|---|
| `Moeda` | inimigo morto, baú | comprar em NPC |
| `Madeira` | árvore quebrada, drop | craft, construir |
| `Pedra` | rocha quebrada, drop | craft, construir |
| `Fibra` | mato, drop de gosma | bandagem |
| `Bandagem` | craft | curar |
| `Muro` | craft | construir parede |

Nome com acento funciona, mas aparece em token de HUD (`{Item:Bandagem}`) e em campo do
Inspector — prefira nomes curtos e sem espaço.

### Camadas de desenho (`Layer`)

| Layer | Conteúdo |
|---|---|
| 0 | terreno (`Tilemap` de chão) |
| 1 | água / lava (`Tilemap` de líquido) |
| 2 | construções do jogador (`Tilemap` de blocos) |
| 5 | itens caídos |
| 10 | jogador e inimigos |
| 11 | efeitos de golpe, partículas |

---

## 2. Esqueleto do projeto

```
MeuSurvivor/
  Program.cs
  SurvivorGame.cs          <- monta o mundo (herda Game)
  Assets/
    tilesets/terreno.png   <- 32px: 0 grama, 1 terra, 2 pedra, 3 piso de madeira
    tilesets/water.png     <- copie de samples/Aurora.Farm/Assets/tilesets/
    sprites/player.png, slime.png, item_wood.png, slash.png
    ui/hud.json            <- tela de UI (id = "hud")
    fonts/DejaVuSans.ttf
  Scripts/
    TileMath.cs  Vitals.cs  PlayerMove.cs  PlayerCombat.cs  HitEffect.cs
    EnemyAI.cs  EnemySpawner.cs  ItemDrop.cs  Crafting.cs  Builder.cs
```

`Program.cs`:

```csharp
using MeuSurvivor;

var game = new SurvivorGame();
game.ParseArgs(args);
game.Run("Meu Survivor", 1280, 720);
```

Todo script deste guia começa com:

```csharp
using System.Numerics;
using Aurora.Runtime;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Scenes;
using Aurora.Runtime.UI;
using Silk.NET.Input;

namespace MeuSurvivor;
```

**`MouseButton` e `Key` vêm do `Silk.NET.Input`, não da Aurora.** Sem a linha
`using Silk.NET.Input;` o compilador acusa *"o tipo ou nome de namespace 'MouseButton' não
existe"* — e é exatamente o que acontece quando o script nasce dos templates *Movimento*,
*Item* ou *Vazio* do painel SCRIPTS, que não trazem esse `using` (só *Arma* e *Magia* trazem).
Alternativas sem `using`: `input.WasMouseClicked()` (o padrão já é o botão esquerdo) ou o nome
completo, `Silk.NET.Input.MouseButton.Left`.

Requisitos pro script ser descoberto pelo editor (senão ele não aparece no "+Add Componente"):
`[SceneScript]`, classe `sealed`, **construtor sem parâmetro**. Campo público de
`float`/`int`/`bool`/`string` vira campo editável no Inspector; qualquer outro tipo
(`Vector2`, `Texture2D`, `Entity`) só existe em C#.

---

## 3. Ajudante de tiles (usado por construção, água e mineração)

Três sistemas precisam da mesma conta: "que célula do tilemap está debaixo deste ponto do
mundo?". Escreva uma vez, em `Scripts/TileMath.cs` — classe estática comum, **sem**
`[SceneScript]` (não é componente, é utilitário):

```csharp
public static class TileMath
{
    /// <summary>Converte ponto do mundo em célula do tilemap da entidade. False = fora da grade.</summary>
    public static bool TryWorldToTile(Entity mapEntity, Vector2 world, out int tx, out int ty)
    {
        tx = ty = -1;
        if (!mapEntity.IsAlive) return false;

        var map = mapEntity.Get<Tilemap>();
        var origin = mapEntity.Get<Transform>()?.Position ?? Vector2.Zero;
        if (map is null || map.TileWidth <= 0 || map.TileHeight <= 0) return false;

        tx = (int)MathF.Floor((world.X - origin.X) / map.TileWidth);
        ty = (int)MathF.Floor((world.Y - origin.Y) / map.TileHeight);
        return tx >= 0 && ty >= 0 && tx < map.Width && ty < map.Height;
    }

    /// <summary>Centro da célula, em mundo — pra nascer item/efeito no lugar certo.</summary>
    public static Vector2 TileCenter(Entity mapEntity, int tx, int ty)
    {
        var map = mapEntity.Get<Tilemap>()!;
        var origin = mapEntity.Get<Transform>()?.Position ?? Vector2.Zero;
        return origin + new Vector2((tx + 0.5f) * map.TileWidth, (ty + 0.5f) * map.TileHeight);
    }

    /// <summary>Índice do tile sob um ponto; -1 = vazio ou fora da grade.</summary>
    public static int TileAt(Entity mapEntity, Vector2 world)
        => TryWorldToTile(mapEntity, world, out int x, out int y)
            ? mapEntity.Get<Tilemap>()!.GetTile(x, y)
            : -1;
}
```

---

## 4. O mundo: terreno, água e a camada de construção

Três `Tilemap` empilhados, cada um na sua entidade. Separar é o que permite construir sem
apagar o chão, e ter água por cima do terreno com as bordas transparentes deixando o chão
aparecer.

```csharp
public const int Cell = 32;
public const int MapW = 80, MapH = 60;

private void BuildWorld()
{
    var origin = new Vector2(-(MapW * Cell) / 2f, -(MapH * Cell) / 2f);

    // --- Camada 0: chão.
    var terrain = World.CreateEntity("Terreno");
    terrain.Add(new Transform(origin));
    var ground = terrain.Add(new Tilemap
    {
        Tileset = Assets.LoadTexture("tilesets/terreno.png"),
        TileWidth = Cell, TileHeight = Cell,
        Width = MapW, Height = MapH,
        Layer = 0,
    });
    ground.Fill(0, 0, MapW, MapH, 0);              // 0 = grama

    // --- Camada 1: lagoa animada. Detalhes em GUIA-AGUA-LAVA-SANGUE.md.
    var pond = World.CreateEntity("Agua");
    pond.Add(new Transform(origin));
    var water = pond.Add(new Tilemap
    {
        Tileset = Assets.LoadTexture("tilesets/water.png"),
        TileWidth = Cell, TileHeight = Cell,
        Width = MapW, Height = MapH,
        Layer = 1,
        AnimationFrames = 4,
        AnimationFrameDuration = 0.18f,
        AnimationColumns = LiquidTileset.Columns,   // 16
    });
    water.Fill(0, 0, MapW, MapH, -1);               // -1 = seco
    water.Fill(48, 30, 12, 10, 0);                  // marca a lagoa com qualquer índice >= 0
    water.Autotile(outsideIsFilled: false);         // margem, canto e miolo saem daqui

    // Água atravessável (o jogador entra e bebe). Pra água que bloqueia:
    //   for (int m = 0; m <= LiquidTileset.Center; m++) water.SolidTiles.Add(m);

    // --- Camada 2: o que o jogador construir. Nasce vazia.
    var build = World.CreateEntity("Construcao");
    build.Add(new Transform(origin));
    var built = build.Add(new Tilemap
    {
        Tileset = Assets.LoadTexture("tilesets/terreno.png"),
        TileWidth = Cell, TileHeight = Cell,
        Width = MapW, Height = MapH,
        Layer = 2,
    });
    built.Fill(0, 0, MapW, MapH, -1);
    built.SolidTiles.Add(2);   // pedra construída = parede: empurra quem tem Collider
}
```

Detalhe que economiza depuração: **`SolidTiles` do tilemap de construção é o que dá colisão ao
que o jogador levanta** — não precisa de `Collider` por bloco. Quem tem `Collider` com
`IsKinematic = false` já é empurrado sozinho, jogador e inimigo incluídos, e o `NavAgent`
desvia desses tiles no pathfinding.

---

## 5. O jogador

### 5.1 A entidade

```csharp
private void SpawnPlayer(Vector2 position)
{
    var player = World.CreateEntity("Player");     // o nome importa: câmera, save e inimigos procuram por ele
    player.Add(new Transform(position));
    player.Add(new SpriteRenderer(Assets.LoadTexture("sprites/player.png"), layer: 10)
    {
        Size = new Vector2(32f, 32f),
    });

    // Collider menor que o sprite e deslocado pros pés: em top-down o corpo colide e a
    // cabeça passa por cima do cenário. Sem isso o personagem "engorda" nas paredes.
    player.Add(new Collider { Shape = ColliderShape.Box, Width = 20f, Height = 14f, Offset = new Vector2(0f, 9f) });

    player.Add(new Health
    {
        Max = 100f, Current = 100f,
        InvulnerabilityAfterHit = 0.6f,   // sem i-frame, encostar num inimigo mata em meio segundo
        DestroyOnDeath = false,           // o jogador não some: quem trata a morte é o Vitals
    });

    player.Add(new Vitals());
    player.Add(new PlayerMove());
    player.Add(new PlayerCombat { Damage = 18f });
    player.Add(new Builder());
    player.Add(new Crafting());

    var cam = World.CreateEntity("Camera");
    cam.Add(new CameraController { Follow = "Player", FollowSpeed = 6f, Zoom = 2f });
}
```

### 5.2 Movimento, corrida e direção — `Scripts/PlayerMove.cs`

```csharp
[SceneScript]
public sealed class PlayerMove : Behavior
{
    public float Speed = 130f;
    public float RunMultiplier = 1.7f;
    public float RunStaminaPerSecond = 18f;

    /// <summary>Tela e elemento do joystick virtual (Android). Sem a tela, cai no teclado.</summary>
    public string HudScreenId = "hud";
    public string StickName = "MoveStick";

    /// <summary>Última direção encarada — ataque e construção miram por ela.</summary>
    public Vector2 Facing { get; private set; } = new(0f, 1f);

    public override void Update(float dt)
    {
        var transform = Get<Transform>();
        var input = World?.Input;
        if (transform is null || input is null) return;

        // Joystick manda quando tocado; sem toque, teclado/gamepad (AxisX/AxisY já combina os dois).
        var move = World!.UI?.Find<UiJoystick>(HudScreenId, StickName)?.Value ?? Vector2.Zero;
        if (move.LengthSquared() <= 0.0001f)
            move = new Vector2(input.AxisX, input.AxisY);

        if (move.LengthSquared() <= 0.0001f)
        {
            Get<Animator>()?.SetFloat("Speed", 0f);
            return;
        }

        if (move.LengthSquared() > 1f)
            move = Vector2.Normalize(move);          // diagonal não pode ser mais rápida

        // Correr só enquanto houver stamina — o Vitals é o dono do número.
        float speed = Speed;
        bool wantsRun = input.IsKeyDown(Key.ShiftLeft) || input.LeftTrigger > 0.5f;
        if (wantsRun && Get<Vitals>() is { } vitals && vitals.SpendStamina(RunStaminaPerSecond * dt))
            speed *= RunMultiplier;

        transform.Position += move * speed * dt;
        Facing = Vector2.Normalize(move);

        if (Get<SpriteRenderer>() is { } sprite && MathF.Abs(move.X) > 0.01f)
            sprite.FlipX = move.X < 0f;

        // Animator: uma transição Idle<->Walk comparando o parâmetro "Speed".
        Get<Animator>()?.SetFloat("Speed", speed);
    }
}
```

`Facing` é `Vector2` — **não aparece no Inspector**, e não precisa: é estado de runtime, lido
pelos outros scripts com `Get<PlayerMove>()?.Facing`.

### 5.3 Vida, stamina e água — `Scripts/Vitals.cs`

Este é o script central do "survivor": espelha a vida pra HUD, gasta e regenera stamina, faz a
sede cair com o tempo, enche a garrafa quando o jogador entra na lagoa e trata a morte.

```csharp
[SceneScript]
public sealed class Vitals : Behavior
{
    // --- Stamina
    public float StaminaMax = 100f;
    public float StaminaRegenPerSecond = 14f;
    /// <summary>Segundos sem gastar antes de voltar a regenerar (evita corrida infinita).</summary>
    public float StaminaRegenDelay = 0.8f;

    // --- Água / sede
    public float WaterMax = 100f;
    public float WaterLossPerSecond = 0.6f;       // 100 -> 0 em ~2min45; suba pra jogo mais duro
    public float WaterRefillPerSecond = 40f;
    public string WaterMapEntity = "Agua";
    /// <summary>Dano por segundo quando a água chega a zero (desidratação).</summary>
    public float ThirstDamagePerSecond = 3f;

    // --- Vida
    public float HealthRegenPerSecond = 0f;       // 0 = sem regeneração natural
    public bool RespawnOnDeath = true;            // false = liga a switch "GameOver" e para

    private float _staminaCooldown;
    private float _thirstAccumulator;
    private float _flashTimer;
    private Vector2 _spawn;

    public override void Start()
    {
        _spawn = Get<Transform>()?.Position ?? Vector2.Zero;

        var state = World?.State;
        if (state is null) return;

        // Só inicializa o que ainda não existe — assim carregar um save não zera nada.
        if (state.GetVariable("Stamina", -1f) < 0f) state.SetVariable("Stamina", StaminaMax);
        if (state.GetVariable("Agua", -1f) < 0f) state.SetVariable("Agua", WaterMax);
    }

    public override void Update(float dt)
    {
        var state = World?.State;
        var health = Get<Health>();
        if (state is null || health is null) return;

        // 1) Vida -> variável, todo frame. É isso que faz a UiBar "Vida" andar.
        if (HealthRegenPerSecond > 0f && !health.IsDead)
            World!.Heal(Entity, HealthRegenPerSecond * dt);
        state.SetVariable("Vida", health.Current);

        // 2) Stamina regenera depois de um respiro.
        _staminaCooldown = MathF.Max(0f, _staminaCooldown - dt);
        if (_staminaCooldown <= 0f)
            state.SetVariable("Stamina",
                MathF.Min(StaminaMax, state.GetVariable("Stamina", StaminaMax) + StaminaRegenPerSecond * dt));

        // 3) Água: enche dentro da lagoa, seca fora dela.
        UpdateWater(state, dt);

        // 4) Devolve a cor normal depois do flash de dano.
        if (_flashTimer > 0f)
        {
            _flashTimer -= dt;
            if (_flashTimer <= 0f && Get<SpriteRenderer>() is { } sprite)
                sprite.Color = Color.White;
        }
    }

    private void UpdateWater(GameState state, float dt)
    {
        float water = state.GetVariable("Agua", WaterMax);
        bool inWater = IsStandingOnWater();

        water = Math.Clamp(water + (inWater ? WaterRefillPerSecond : -WaterLossPerSecond) * dt, 0f, WaterMax);
        state.SetVariable("Agua", water);

        if (water > 0f)
        {
            _thirstAccumulator = 0f;
            return;
        }

        // Dano de desidratação em pulsos de 1s: chamar Damage todo frame seria engolido
        // pelos i-frames e ainda encheria o log de eventos à toa.
        _thirstAccumulator += dt;
        if (_thirstAccumulator >= 1f)
        {
            _thirstAccumulator -= 1f;
            World?.Damage(Entity, ThirstDamagePerSecond);
        }
    }

    private bool IsStandingOnWater()
    {
        if (World is null || Get<Transform>() is not { } transform) return false;
        if (!World.TryFind(WaterMapEntity, out var map)) return false;
        return TileMath.TileAt(map, transform.Position) >= 0;   // -1 = célula seca
    }

    /// <summary>Tenta gastar stamina. False = não tinha o bastante (quem chamou não corre/ataca).</summary>
    public bool SpendStamina(float amount)
    {
        var state = World?.State;
        if (state is null) return false;

        float current = state.GetVariable("Stamina", StaminaMax);
        if (current < amount) return false;

        state.SetVariable("Stamina", current - amount);
        _staminaCooldown = StaminaRegenDelay;
        return true;
    }

    public override void OnDamaged(float amount, Entity? source)
    {
        World?.Audio?.Play("audio/hit.wav", volume: 0.7f);

        // Pisca vermelho — sem feedback visual o jogador não percebe que levou dano.
        if (Get<SpriteRenderer>() is { } sprite)
        {
            sprite.Color = Color.FromBytes(255, 120, 120);
            _flashTimer = 0.12f;
        }
    }

    public override void OnDeath()
    {
        var state = World?.State;
        var health = Get<Health>();
        if (state is null || health is null) return;

        if (!RespawnOnDeath)
        {
            // Script não tem `Game` direto: sinalize por switch e trate no Game.OnUpdate
            // (LoadScene) ou num UiButton com a ação ChangeScene.
            state.SetSwitch("GameOver", true);
            return;
        }

        health.Current = health.Max;                      // Health.Current só se escreve direto aqui,
        state.SetVariable("Agua", WaterMax);              // no renascimento; no resto use Damage/Heal.
        state.SetVariable("Stamina", StaminaMax);
        if (Get<Transform>() is { } t) t.Position = _spawn;

        int moedas = World?.Inventory?.GetCount("Moeda") ?? 0;
        World?.Inventory?.Remove("Moeda", moedas / 2);
        World?.Dialogue?.ShowMessage("Você desmaiou... e perdeu metade das moedas.");
    }
}
```

`DestroyOnDeath = false` no `Health` do jogador é obrigatório com esse `OnDeath` — senão a
entidade é destruída logo depois de você reposicioná-la.

---

## 6. Ataque

Corpo-a-corpo tem três partes: **cooldown** (senão o clique vira metralhadora), **efeito
visual** na direção do golpe e **área de dano** ao redor do ponto atingido. Custo de stamina
liga o ataque ao sistema de sobrevivência: quem bate sem parar não consegue fugir.

### 6.1 `Scripts/PlayerCombat.cs`

```csharp
[SceneScript]
public sealed class PlayerCombat : Behavior
{
    public float Damage = 18f;
    public float Cooldown = 0.35f;
    public float StaminaCost = 8f;

    /// <summary>Distância do centro do jogador até o centro do golpe, em pixels.</summary>
    public float Reach = 26f;
    public float DamageRadius = 30f;

    public string EffectTexture = "sprites/slash.png";
    public float EffectSize = 52f;
    public float FrameDuration = 0.05f;
    public int EffectLayer = 11;
    /// <summary>Quadros do sheet; 0 = descobre sozinho (largura ÷ altura, quadros quadrados).</summary>
    public int EffectFrames;

    /// <summary>True = golpe arredondado nas 8 direções, como RPG clássico.</summary>
    public bool SnapToEightDirections;

    private float _cooldownTimer;
    private Vector2 _facing = new(0f, 1f);

    public override void Update(float dt)
    {
        _cooldownTimer -= dt;

        var input = World?.Input;
        var transform = Get<Transform>();
        if (World is null || input is null || transform is null) return;

        // Direção: o movimento manda enquanto anda...
        if (Get<PlayerMove>() is { } move && move.Facing.LengthSquared() > 0.0001f)
            _facing = move.Facing;

        bool pressed = input.WasMouseClicked(MouseButton.Left)
            || input.WasKeyPressed(Key.Space)
            || (World.UI?.Find<UiButton>("hud", "BotaoAtaque")?.Clicked ?? false);

        if (!pressed || _cooldownTimer > 0f) return;

        // ...mas no clique a mira do mouse tem a palavra final.
        if (AimFromMouse(transform.Position) is { } aim)
            _facing = aim;

        // Sem stamina, sem golpe (e sem cooldown gasto).
        if (StaminaCost > 0f && Get<Vitals>() is { } vitals && !vitals.SpendStamina(StaminaCost))
            return;

        _cooldownTimer = Cooldown;
        World.Audio?.Play("audio/swing.wav", pitch: 0.9f + Random.Shared.NextSingle() * 0.2f);

        SpawnEffect(transform.Position);
        HitAround(transform.Position + _facing * Reach);
    }

    /// <summary>Direção do jogador até o cursor em coordenadas de MUNDO. O mouse vem em pixel
    /// de tela: sem ScreenToWorld o golpe erra assim que a câmera sai da origem ou o zoom muda.</summary>
    private Vector2? AimFromMouse(Vector2 origin)
    {
        if (World?.Camera is not { } camera || World.Input is not { } input) return null;

        var direction = camera.ScreenToWorld(input.MousePosition) - origin;
        if (direction.LengthSquared() < 0.01f) return null;   // cursor em cima do jogador

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
        if (texture is null) return;

        int frameSize = texture.Height;                        // sheet de uma linha, quadros quadrados
        int frames = EffectFrames > 0 ? EffectFrames : Math.Max(1, texture.Width / Math.Max(1, frameSize));

        var effect = World.CreateEntity("Golpe");
        effect.Add(new Transform(origin + _facing * Reach)
        {
            Rotation = MathF.Atan2(_facing.Y, _facing.X),      // radianos; 0 = apontando pra direita
        });
        effect.Add(new SpriteRenderer(texture, EffectLayer) { Size = new Vector2(EffectSize, EffectSize) });
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
                    Loop = false,        // sem isso o corte fica piscando pra sempre
                },
            ],
        });
        effect.Add(new HitEffect { Owner = Entity, Offset = _facing * Reach });
    }

    /// <summary>Dano em todo mundo com Health dentro do raio, menos o próprio atacante.</summary>
    private void HitAround(Vector2 center)
    {
        foreach (var (target, _) in World!.Query<Health>())
        {
            if (target.Id == Entity.Id) continue;

            if (target.Get<Transform>() is { } t && Vector2.Distance(t.Position, center) <= DamageRadius)
                World.Damage(target, Damage, Entity);
        }
    }
}
```

### 6.2 `Scripts/HitEffect.cs` — o efeito que se apaga sozinho

Sem isso, cada clique deixa uma entidade parada no cenário pra sempre.

```csharp
[SceneScript]
public sealed class HitEffect : Behavior
{
    /// <summary>Quem lançou. Nullable de propósito: um Entity default não aponta pra World
    /// nenhum e estoura ao ler IsAlive.</summary>
    public Entity? Owner;
    public Vector2 Offset;
    /// <summary>Rede de segurança: se não houver Animator (ou o clipe estiver em loop),
    /// a entidade morre por tempo em vez de vazar.</summary>
    public float MaxLife = 1.5f;

    private float _age;

    public override void Update(float dt)
    {
        _age += dt;

        var transform = Get<Transform>();
        if (transform is null || World is null) return;

        // Gruda em quem golpeou: sem isso, andar durante o golpe deixa o corte pra trás.
        if (Owner is { IsAlive: true } owner && owner.Get<Transform>() is { } ownerTransform)
            transform.Position = ownerTransform.Position + Offset;

        if (Get<Animator>() is { IsFinished: true } || _age >= MaxLife)
            World.Destroy(Entity);
    }
}
```

Versão rodando, com comentário linha a linha: `samples/Aurora.Farm/Scripts/PlayerAttack.cs`
e `AttackEffect.cs` — e o passo a passo em [TUTORIAL-SCRIPTS-PLAYER.md](TUTORIAL-SCRIPTS-PLAYER.md).

### 6.3 Ataque à distância (arco, magia)

Não precisa de script novo: `Projectile` já aplica dano e se autodestrói. Instancie a flecha
e só preencha `Velocity`/`Source`:

```csharp
var arrow = World!.CreateEntity("Flecha");
arrow.Add(new Transform(origin + _facing * Reach));
arrow.Add(new SpriteRenderer(World.Assets!.LoadTexture("sprites/arrow.png"), layer: 11));
arrow.Add(new Collider { Shape = ColliderShape.Circle, Radius = 4f, IsSolid = false });  // trigger
arrow.Add(new Projectile
{
    Velocity = _facing * 320f,
    Damage = 12f,
    Life = 2f,
    Source = Entity,           // impede acertar quem atirou
    TargetPrefix = "Inimigo",  // "" = qualquer coisa com Health
});
```

O `Collider` **precisa** de `IsSolid = false`: projétil que empurra em vez de atravessar
ricocheteia no alvo em vez de acertar.

---

## 7. Inimigos

### 7.1 `Scripts/EnemyAI.cs` — perseguir, bater, morrer largando item

`NavAgent` já anda e desvia de tile sólido sozinho dentro do `World.Update`; o script só
decide o **destino** e o que acontece no contato e na morte.

```csharp
[SceneScript]
public sealed class EnemyAI : Behavior
{
    public string TargetName = "Player";
    public float SightRange = 220f;
    public float ContactDamage = 8f;
    /// <summary>Segundos entre duas mordidas no mesmo alvo.</summary>
    public float AttackInterval = 0.8f;

    /// <summary>Distância a partir da qual ele desiste e volta ao posto (0 = nunca desiste).</summary>
    public float LeashRange = 500f;

    // --- Recompensa
    public int CoinDrop = 3;
    public string LootItem = "Fibra";
    public int LootAmount = 1;
    /// <summary>Chance 0..1 de largar o LootItem.</summary>
    public float LootChance = 0.5f;

    private float _attackTimer;
    private Vector2 _home;

    public override void Start() => _home = Get<Transform>()?.Position ?? Vector2.Zero;

    public override void Update(float dt)
    {
        _attackTimer -= dt;

        var nav = Get<NavAgent>();
        var transform = Get<Transform>();
        if (World is null || nav is null || transform is null) return;

        // Cada inimigo acha o alvo sozinho: funciona pra quantas cópias a cena tiver,
        // sem nenhum código no Game.OnUpdate.
        if (!World.TryFind(TargetName, out var target) || target.Get<Transform>() is not { } targetTransform)
        {
            nav.Stop();
            return;
        }

        float distance = Vector2.Distance(transform.Position, targetTransform.Position);
        bool tooFarFromHome = LeashRange > 0f && Vector2.Distance(transform.Position, _home) > LeashRange;

        if (distance <= SightRange && !tooFarFromHome)
            nav.SetTarget(targetTransform.Position);
        else if (tooFarFromHome)
            nav.SetTarget(_home);
        else
            nav.Stop();
    }

    // Colisão sólida: acontece todo frame enquanto encostado, por isso o intervalo próprio.
    // (O i-frame do Health do jogador já segura parte disso, mas o timer deixa o ritmo explícito.)
    public override void OnCollision(Entity other, CollisionInfo info)
    {
        if (_attackTimer > 0f || !other.Has<Health>() || other.Name != TargetName) return;

        _attackTimer = AttackInterval;
        World?.Damage(other, ContactDamage, Entity);
    }

    public override void OnDamaged(float amount, Entity? source)
    {
        World?.Audio?.Play("audio/enemy_hit.wav", volume: 0.6f);
        if (Get<SpriteRenderer>() is { } sprite)
            sprite.Color = Color.FromBytes(255, 140, 140);

        // Levou porrada de longe? Vem pra cima de quem bateu.
        if (source is { IsAlive: true } attacker && attacker.Get<Transform>() is { } t)
            Get<NavAgent>()?.SetTarget(t.Position);
    }

    // OnDeath roda ANTES do Destroy do Health — dá pra ler a posição e nascer o drop.
    public override void OnDeath()
    {
        var position = Get<Transform>()?.Position ?? Vector2.Zero;

        if (CoinDrop > 0)
            ItemDrop.Spawn(World!, "Moeda", CoinDrop, position, "sprites/coin.png");

        if (LootAmount > 0 && Random.Shared.NextSingle() < LootChance)
            ItemDrop.Spawn(World!, LootItem, LootAmount, position + new Vector2(8f, 0f), "sprites/item_fiber.png");

        World?.Audio?.Play("audio/enemy_die.wav");
    }
}
```

A entidade do inimigo, montada em código:

```csharp
public Entity SpawnEnemy(Vector2 position, float hp, float speed, string sprite)
{
    var enemy = World.CreateEntity("Inimigo");       // prefixo no nome ajuda a filtrar depois
    enemy.Add(new Transform(position));
    enemy.Add(new SpriteRenderer(Assets.LoadTexture(sprite), layer: 10) { Size = new Vector2(28f, 28f) });
    enemy.Add(new Collider { Shape = ColliderShape.Box, Width = 20f, Height = 14f, Offset = new Vector2(0f, 8f) });
    enemy.Add(new Health { Max = hp, Current = hp, InvulnerabilityAfterHit = 0.15f });
    enemy.Add(new NavAgent { Speed = speed, ArriveThreshold = 6f });
    enemy.Add(new EnemyAI());
    return enemy;
}
```

`InvulnerabilityAfterHit` pequeno (0,1–0,2s) no inimigo é o que impede um golpe só contar
várias vezes por causa de frames seguidos dentro do raio.

### 7.2 `Scripts/EnemySpawner.cs` — ondas

Uma entidade vazia com este script é o "diretor" da partida. Ele nasce inimigos **num anel ao
redor do jogador** (fora da tela, nunca em cima dele), com dificuldade subindo por onda.

```csharp
[SceneScript]
public sealed class EnemySpawner : Behavior
{
    public string TargetName = "Player";
    public float WaveDuration = 45f;        // segundos por onda
    public float SpawnInterval = 3f;
    public int MaxAlive = 25;

    /// <summary>Raio do anel de nascimento: longe o bastante pra não aparecer na tela.</summary>
    public float SpawnDistanceMin = 320f;
    public float SpawnDistanceMax = 420f;

    public float BaseHealth = 30f;
    public float HealthPerWave = 8f;
    public float BaseSpeed = 70f;
    public float SpeedPerWave = 4f;
    public string EnemySprite = "sprites/slime.png";

    private float _waveTimer;
    private float _spawnTimer;
    private int _wave = 1;

    public override void Start() => World?.State?.SetVariable("Onda", _wave);

    public override void Update(float dt)
    {
        if (World is null || !World.TryFind(TargetName, out var player)) return;
        if (player.Get<Transform>() is not { } playerTransform) return;

        _waveTimer += dt;
        if (_waveTimer >= WaveDuration)
        {
            _waveTimer = 0f;
            _wave++;
            World.State?.SetVariable("Onda", _wave);
            World.Dialogue?.ShowMessage($"Onda {_wave}!");
        }

        _spawnTimer -= dt;
        if (_spawnTimer > 0f) return;
        _spawnTimer = SpawnInterval;

        if (CountAlive() >= MaxAlive) return;      // teto de inimigos: protege o frame rate

        float angle = Random.Shared.NextSingle() * MathF.Tau;
        float distance = SpawnDistanceMin + Random.Shared.NextSingle() * (SpawnDistanceMax - SpawnDistanceMin);
        var position = playerTransform.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;

        Spawn(position);
    }

    private int CountAlive()
    {
        int count = 0;
        foreach (var _ in World!.Query<EnemyAI>()) count++;
        return count;
    }

    private void Spawn(Vector2 position)
    {
        var texture = World!.Assets?.LoadTexture(EnemySprite);
        if (texture is null) return;

        float hp = BaseHealth + HealthPerWave * (_wave - 1);

        var enemy = World.CreateEntity("Inimigo");
        enemy.Add(new Transform(position));
        enemy.Add(new SpriteRenderer(texture, layer: 10) { Size = new Vector2(28f, 28f) });
        enemy.Add(new Collider { Shape = ColliderShape.Box, Width = 20f, Height = 14f, Offset = new Vector2(0f, 8f) });
        enemy.Add(new Health { Max = hp, Current = hp, InvulnerabilityAfterHit = 0.15f });
        enemy.Add(new NavAgent { Speed = BaseSpeed + SpeedPerWave * (_wave - 1), ArriveThreshold = 6f });
        enemy.Add(new EnemyAI
        {
            TargetName = TargetName,
            ContactDamage = 6f + _wave,
            CoinDrop = 2 + _wave / 2,
        });
    }
}
```

Ligar na cena é uma entidade só:

```csharp
var director = World.CreateEntity("Diretor");
director.Add(new EnemySpawner { EnemySprite = "sprites/slime.png" });
```

Variante "só à noite": some um relógio no `Game` (`State.SetVariable("Hora", ...)`) e comece o
`Update` do spawner com `if (World.State?.GetVariable("Hora") is > 6 and < 18) return;`.

---

## 8. Itens, drops e moedas

Contagem, save e HUD de item já são do `InventoryManager` (`Add`, `Remove`, `GetCount`, `Has`,
`Items`). O que falta escrever é o **item no chão**: cai, é atraído pelo jogador quando ele
chega perto e some ao ser coletado.

### 8.1 `Scripts/ItemDrop.cs`

```csharp
[SceneScript]
public sealed class ItemDrop : Behavior
{
    public string Item = "Moeda";
    public int Amount = 1;

    /// <summary>Distância em que o item começa a voar pro jogador.</summary>
    public float MagnetRange = 60f;
    public float MagnetSpeed = 260f;
    /// <summary>Distância em que ele é coletado de fato.</summary>
    public float PickupRange = 12f;
    /// <summary>Segundos até sumir sozinho (0 = fica pra sempre).</summary>
    public float Lifetime = 60f;
    public string TargetName = "Player";

    private float _age;

    public override void Update(float dt)
    {
        _age += dt;

        var transform = Get<Transform>();
        if (World is null || transform is null) return;

        if (Lifetime > 0f && _age > Lifetime)
        {
            World.Destroy(Entity);
            return;
        }

        if (!World.TryFind(TargetName, out var player) || player.Get<Transform>() is not { } playerTransform)
            return;

        float distance = Vector2.Distance(transform.Position, playerTransform.Position);

        if (distance <= PickupRange)
        {
            World.Inventory?.Add(Item, Amount);
            World.Audio?.Play("audio/pickup.wav", volume: 0.5f);
            World.Destroy(Entity);
            return;
        }

        if (distance <= MagnetRange)
        {
            var direction = Vector2.Normalize(playerTransform.Position - transform.Position);
            transform.Position += direction * MagnetSpeed * dt;
        }
    }

    /// <summary>Fábrica usada pelos drops de inimigo, mineração e baú.</summary>
    public static Entity Spawn(World world, string item, int amount, Vector2 position, string texturePath)
    {
        var entity = world.CreateEntity($"Drop:{item}");
        entity.Add(new Transform(position));

        if (world.Assets?.LoadTexture(texturePath) is { } texture)
            entity.Add(new SpriteRenderer(texture, layer: 5) { Size = new Vector2(14f, 14f) });

        entity.Add(new ItemDrop { Item = item, Amount = amount });
        return entity;
    }
}
```

Repare que o drop **não usa `Collider`**: distância em `Update` é mais barata e não empurra o
jogador. Colisão só entra quando o objeto tem que bloquear ou disparar `OnTriggerEnter`.

### 8.2 Moeda parada no cenário — zero código

Para o baú/moeda posicionado à mão no editor, nem script precisa:

```
Transform         X 300   Y 150
SpriteRenderer    Texture  sprites/coin.png
EventTrigger      Trigger [PlayerTouch]  Radius 16  ☑ Once
  AÇÕES
    AddItem   Item: Moeda   Quantidade: 10
    Destroy
```

### 8.3 Mostrar na HUD

`UiText` com token, sem nenhuma linha de C#:

```
Text   Moedas: {Item:Moeda}   Madeira: {Item:Madeira}
```

---

## 9. Fabricação (craft)

O `InventoryManager` sabe contar; a receita é sua. O truque para caber no Inspector — que só
edita `string`/`float`/`int`/`bool` — é escrever as receitas **numa string** e interpretá-las
no `Start`.

### 9.1 `Scripts/Crafting.cs`

```csharp
[SceneScript]
public sealed class Crafting : Behavior
{
    /// <summary>Receitas: "Resultado=Item*qtd+Item*qtd;Outro=...".
    /// Ex.: "Bandagem=Fibra*2;Muro=Pedra*5+Madeira*2;Tocha=Madeira*1+Fibra*1"</summary>
    public string Recipes = "Bandagem=Fibra*2;Muro=Pedra*5+Madeira*2;Tocha=Madeira*1+Fibra*1";

    /// <summary>Tela de UI onde estão os botões. Cada receita procura o botão "Craft<Nome>".</summary>
    public string HudScreenId = "craft";
    /// <summary>Tecla que abre/fecha a tela de fabricação.</summary>
    public string ToggleKey = "C";

    private readonly Dictionary<string, Dictionary<string, int>> _recipes = new();

    public override void Start() => Parse();

    private void Parse()
    {
        _recipes.Clear();
        foreach (var line in Recipes.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue;

            var cost = new Dictionary<string, int>();
            foreach (var term in parts[1].Split('+', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = term.Split('*', 2);
                cost[pair[0].Trim()] = pair.Length > 1 && int.TryParse(pair[1], out int n) ? n : 1;
            }
            _recipes[parts[0].Trim()] = cost;
        }
    }

    public override void Update(float dt)
    {
        var ui = World?.UI;
        if (ui is null) return;

        // Abre/fecha o painel. Enum de tecla vem do Silk.NET: Enum.TryParse resolve a string.
        if (Enum.TryParse<Key>(ToggleKey, true, out var key) && World?.Input?.WasKeyPressed(key) == true)
            ui.Toggle(HudScreenId);

        if (!ui.IsVisible(HudScreenId)) return;

        // Um botão por receita, nomeado "Craft" + resultado (ex.: entidade "CraftBandagem").
        foreach (var name in _recipes.Keys)
        {
            if (ui.Find<UiButton>(HudScreenId, "Craft" + name)?.Clicked == true)
                TryCraft(name);
        }
    }

    /// <summary>Tem todos os materiais?</summary>
    public bool CanCraft(string result)
    {
        var inventory = World?.Inventory;
        if (inventory is null || !_recipes.TryGetValue(result, out var cost)) return false;

        foreach (var (item, amount) in cost)
            if (!inventory.Has(item, amount)) return false;

        return true;
    }

    /// <summary>Gasta os materiais e entrega o item. False = faltou material.</summary>
    public bool TryCraft(string result)
    {
        var inventory = World?.Inventory;
        if (inventory is null || !CanCraft(result))
        {
            World?.Dialogue?.ShowMessage("Faltam materiais.");
            return false;
        }

        // Só depois de CanCraft: gastar em duas passadas evita consumir metade e falhar.
        foreach (var (item, amount) in _recipes[result])
            inventory.Remove(item, amount);

        inventory.Add(result, 1);
        World?.Audio?.Play("audio/craft.wav");
        World?.Dialogue?.ShowMessage($"{result} fabricado!");
        return true;
    }
}
```

### 9.2 Usar o item fabricado

Consumível é uma linha em quem tem o efeito — por exemplo, a bandagem no `Vitals`:

```csharp
// dentro de Vitals.Update
if (World?.Input?.WasKeyPressed(Key.Q) == true
    && World.Inventory?.Has("Bandagem") == true
    && Get<Health>() is { Current: var hp, Max: var max } && hp < max)
{
    World.Inventory.Remove("Bandagem", 1);
    World.Heal(Entity, 35f);
}
```

A checagem `hp < max` evita queimar bandagem com a vida cheia — detalhe pequeno que os
jogadores agradecem.

### 9.3 A tela `ui/craft.json`

Um `UiPanel` de fundo, um `UiText` por receita mostrando o custo (com tokens, que atualizam
sozinhos) e um `UiButton` chamado `Craft<Nome>` por receita:

```
Fundo        -> UiPanel   AnchorX Center  AnchorY Center  Width 320  Height 220  Color #101020DD
Titulo       -> UiText    Text "FABRICAR"
CustoBanda   -> UiText    Text "Bandagem — Fibra {Item:Fibra}/2"
CraftBandagem-> UiButton  Text "Fabricar"        <- o nome da ENTIDADE é o que o Find<T> acha
```

Lembre: `Find<UiButton>("craft", "CraftBandagem")` procura o **nome da entidade** na tela, não
o do componente. E `Clicked` vale por **um frame** — leia todo `Update`, como `WasKeyPressed`.

---

## 10. Construção

Construir é escrever no `Tilemap` da camada 2. O mesmo botão faz as duas coisas: clicar numa
célula vazia **coloca** (gastando o item), clicar numa célula construída **remove** (devolvendo
o item). Botão direito para construir mantém o esquerdo livre pro ataque.

### 10.1 `Scripts/Builder.cs`

```csharp
[SceneScript]
public sealed class Builder : Behavior
{
    public string BuildMapEntity = "Construcao";
    /// <summary>Blocos disponíveis: "Item:índiceDoTile", separados por vírgula.
    /// As teclas 1..9 escolhem na ordem.</summary>
    public string Blocks = "Muro:2,Piso:3";

    /// <summary>Alcance máximo em pixels — sem isso dá pra construir do outro lado do mapa.</summary>
    public float BuildRange = 96f;
    /// <summary>Impede fechar o bloco em cima de alguém (raio checado no centro da célula).</summary>
    public float ClearRadius = 14f;

    private readonly List<(string Item, int Tile)> _blocks = new();
    private int _selected;

    public override void Start()
    {
        foreach (var entry in Blocks.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = entry.Split(':', 2);
            if (pair.Length == 2 && int.TryParse(pair[1], out int tile))
                _blocks.Add((pair[0].Trim(), tile));
        }
    }

    public override void Update(float dt)
    {
        var input = World?.Input;
        var transform = Get<Transform>();
        if (World is null || input is null || transform is null || _blocks.Count == 0) return;

        // Teclas 1..9 trocam o bloco selecionado.
        for (int i = 0; i < _blocks.Count && i < 9; i++)
            if (input.WasKeyPressed(Key.Number1 + i))
                _selected = i;

        if (!input.WasMouseClicked(MouseButton.Right)) return;
        if (World.Camera is not { } camera) return;
        if (!World.TryFind(BuildMapEntity, out var mapEntity)) return;

        var world = camera.ScreenToWorld(input.MousePosition);
        if (Vector2.Distance(world, transform.Position) > BuildRange)
        {
            World.Dialogue?.ShowMessage("Longe demais.");
            return;
        }

        if (!TileMath.TryWorldToTile(mapEntity, world, out int tx, out int ty)) return;

        var map = mapEntity.Get<Tilemap>()!;
        if (map.GetTile(tx, ty) >= 0)
            Remove(mapEntity, map, tx, ty);
        else
            Place(mapEntity, map, tx, ty);
    }

    private void Place(Entity mapEntity, Tilemap map, int tx, int ty)
    {
        var (item, tile) = _blocks[_selected];

        if (World!.Inventory?.Has(item) != true)
        {
            World.Dialogue?.ShowMessage($"Sem {item}.");
            return;
        }

        // Não emparedar ninguém: qualquer coisa com Collider dentro da célula cancela.
        var center = TileMath.TileCenter(mapEntity, tx, ty);
        foreach (var (entity, _) in World.Query<Collider>())
        {
            if (entity.Get<Transform>() is { } t
                && Vector2.Distance(t.Position, center) < ClearRadius + map.TileWidth * 0.5f)
                return;
        }

        map.SetTile(tx, ty, tile);
        World.Inventory!.Remove(item, 1);
        World.Audio?.Play("audio/build.wav");
    }

    private void Remove(Entity mapEntity, Tilemap map, int tx, int ty)
    {
        int tile = map.GetTile(tx, ty);
        map.SetTile(tx, ty, -1);                     // -1 = célula vazia

        // Devolve o item cujo índice de tile bate com o que estava ali.
        foreach (var (item, index) in _blocks)
        {
            if (index != tile) continue;
            ItemDrop.Spawn(World!, item, 1, TileMath.TileCenter(mapEntity, tx, ty), "sprites/item_wood.png");
            break;
        }

        World?.Audio?.Play("audio/break.wav");
    }
}
```

### 10.2 O que faz o bloco virar parede

Nada no `Builder` — é o `SolidTiles` do tilemap de construção, definido uma vez na montagem
do mundo (`built.SolidTiles.Add(2)`). Quem tem `Collider` não-cinemático passa a ser empurrado
na hora seguinte, e o `NavAgent` dos inimigos já contorna. **Se o bloco não bloqueia, o índice
dele não está em `SolidTiles`** — é sempre isso.

### 10.3 Mineração do cenário

Quebrar rocha/árvore do mapa base é o mesmo código apontado pro tilemap do terreno, com
devolução de material:

```csharp
// clique esquerdo com a picareta selecionada
if (TileMath.TryWorldToTile(terrainEntity, world, out int tx, out int ty))
{
    var map = terrainEntity.Get<Tilemap>()!;
    int tile = map.GetTile(tx, ty);
    if (tile == 2)                                   // 2 = pedra
    {
        map.SetTile(tx, ty, 1);                      // vira terra
        ItemDrop.Spawn(World!, "Pedra", 1, TileMath.TileCenter(terrainEntity, tx, ty), "sprites/item_stone.png");
    }
}
```

Para mineração com progresso (várias picaretadas por bloco), guarde um
`Dictionary<(int,int), float>` de dano por célula no próprio script — é o que o sample
`Aurora.SlandSurvivor` faz em `Gameplay/PlayerController.cs`.

---

## 11. HUD

Barra de vida, stamina e água são três `UiBar` lendo as variáveis do contrato (seção 1) — não
precisa de código nenhum pra elas andarem, desde que o `Vitals` esteja escrevendo as variáveis.

`Assets/ui/hud.json` (id da tela = `hud`, o nome do arquivo sem extensão):

```json
{
  "Scene": "hud",
  "UI": true,
  "Objects": [
    { "Name": "Fundo",   "Components": [
      { "Type": "UiPanel", "X": 8, "Y": 8, "Width": 190, "Height": 78, "Color": "#000000A0" } ] },

    { "Name": "BarraVida", "Components": [
      { "Type": "UiBar", "X": 16, "Y": 16, "Width": 170, "Height": 14,
        "Variable": "Vida", "Max": 100, "FillColor": "#E04040FF", "BackColor": "#30303080" } ] },

    { "Name": "BarraStamina", "Components": [
      { "Type": "UiBar", "X": 16, "Y": 36, "Width": 170, "Height": 10,
        "Variable": "Stamina", "Max": 100, "FillColor": "#E0C040FF", "BackColor": "#30303080" } ] },

    { "Name": "BarraAgua", "Components": [
      { "Type": "UiBar", "X": 16, "Y": 52, "Width": 170, "Height": 10,
        "Variable": "Agua", "Max": 100, "FillColor": "#4090E0FF", "BackColor": "#30303080" } ] },

    { "Name": "Recursos", "Components": [
      { "Type": "UiText", "X": -20, "Y": 16, "AnchorX": "Right",
        "Text": "Moedas {Item:Moeda}   Madeira {Item:Madeira}   Pedra {Item:Pedra}" } ] },

    { "Name": "LabelOnda", "Components": [
      { "Type": "UiText", "X": 0, "Y": 16, "AnchorX": "Center", "Text": "Onda {Onda}" } ] },

    { "Name": "MoveStick", "Components": [
      { "Type": "UiJoystick", "X": 110, "Y": 190, "AnchorX": "Left", "AnchorY": "Bottom", "Radius": 90 } ] },

    { "Name": "BotaoAtaque", "Components": [
      { "Type": "UiButton", "X": 110, "Y": 190, "AnchorX": "Right", "AnchorY": "Bottom",
        "Width": 140, "Height": 140, "Text": "Atacar" } ] }
  ]
}
```

Três coisas que quebram HUD e são difíceis de achar:

1. **Elemento `Ui*` só funciona em tela com `"UI": true`.** Numa cena normal de gameplay o
   `SceneSerializer` ignora o componente com um aviso no console e o elemento simplesmente não
   aparece.
2. **Âncora.** Com `Left/Top` a coordenada só fica certa na resolução em que você autorou.
   Canto direito usa `AnchorX: "Right"` com X negativo; centro usa `Center`.
3. **`UiBar` lê `GameState`, não `Health`.** Se a barra não anda, quem parou foi a linha
   `state.SetVariable("Vida", health.Current)` do `Vitals`.

Trocar de cena **não** esconde a HUD: `UI.Show("hud")` / `UI.Hide("hud")` são explícitos.

---

## 12. Save

`SaveManager` já grava variáveis, switches, inventário, quests e a posição da entidade
`Player` — ou seja: vida (via `Vida`), stamina, água, onda, moedas e todos os materiais já
estão cobertos pelo contrato da seção 1.

```csharp
World?.Save?.Save(0);       // slot 0
World?.Save?.Load(0);
World?.Save?.HasSave(0);
World?.Save?.AutoSave();
```

Duas coisas **não** entram no save e precisam de tratamento seu:

- **O que você escreveu no `Tilemap` de construção.** Serialize à mão: uma string com os
  índices e uma variável por linha, ou grave um arquivo próprio ao lado do save.
- **Vida como componente.** O save restaura a variável `Vida`, não `Health.Current`. Recoloque
  no `Start` de quem sabe:

```csharp
// em Vitals.Start, depois de inicializar as outras variáveis
float salva = World?.State?.GetVariable("Vida", -1f) ?? -1f;
if (salva > 0f && Get<Health>() is { } health)
    health.Current = MathF.Min(health.Max, salva);
```

Salvar por tecla, no `Game.OnUpdate`:

```csharp
if (Input.WasKeyPressed(Key.F5)) Save.Save();
if (Input.WasKeyPressed(Key.F9) && Save.HasSave()) Save.Load();
```

---

## 13. Montagem final — `SurvivorGame.cs`

A ordem importa: fonte e UI antes do mundo (o `Vitals` já lê `State` no primeiro `Start`),
mundo antes do jogador (o `Builder` procura a entidade `Construcao`), jogador antes da câmera
e do spawner (ambos procuram `Player` por nome).

```csharp
using System.Numerics;
using Aurora.Runtime;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace MeuSurvivor;

public sealed class SurvivorGame : Game
{
    public const int Cell = 32;
    public const int MapW = 80, MapH = 60;

    private Font _font = null!;

    public SurvivorGame()
    {
        // Trava câmera, UI e toque nessa proporção em qualquer aparelho.
        DesignResolution = new Vector2D<int>(1280, 720);
        GameName = "MeuSurvivor";
    }

    protected override void OnLoad()
    {
        ClearColor = Color.FromBytes(40, 60, 40);

        _font = Assets.LoadFont("fonts/DejaVuSans.ttf", 20f);
        UI.Load("ui/hud.json", Assets);
        UI.Load("ui/craft.json", Assets);
        UI.Hide("craft");                    // fabricação começa fechada

        BuildWorld();                        // seção 4
        SpawnPlayer(Vector2.Zero);           // seção 5.1

        var director = World.CreateEntity("Diretor");
        director.Add(new EnemySpawner { EnemySprite = "sprites/slime.png" });

        Audio.PlayMusic("audio/tema.ogg", loop: true, volume: 0.5f);
    }

    protected override void OnUpdate(float deltaTime)
    {
        if (Input.WasKeyPressed(Key.F5)) Save.Save();
        if (Input.WasKeyPressed(Key.F9) && Save.HasSave()) Save.Load();
        if (Input.WasKeyPressed(Key.Escape)) World.Paused = !World.Paused;

        // Morte definitiva (Vitals com RespawnOnDeath = false).
        if (State.GetSwitch("GameOver"))
        {
            State.SetSwitch("GameOver", false);
            LoadSceneWithFade("scenes/gameover.json");
        }
    }

    protected override void OnRenderUI(float dt)
    {
        // ScreenSize (não View.FramebufferSize): é o tamanho que o UI.Update usa no hit-test
        // dos botões e que respeita o DesignResolution.
        UI.Draw(SpriteBatch, _font, State, Inventory, Quests, ScreenSize.X, ScreenSize.Y);
        Dialogue.Draw(SpriteBatch, _font, ScreenSize.X, ScreenSize.Y);
    }
}
```

Fazer tudo em código (como acima) ou montar a cena no editor e só escrever os scripts são
caminhos equivalentes — o editor grava o mesmo `World`. Em código é mais fácil de versionar e
de gerar mapa procedural; no editor é mais rápido de posicionar cenário à mão.

---

## 14. Checklist de "por que não funciona"

| Sintoma | Causa quase sempre |
|---|---|
| `MouseButton`/`Key` não existe | falta `using Silk.NET.Input;` (os templates Movimento/Item/Vazio não trazem) |
| Script não aparece no "+Add Componente" | falta `[SceneScript]`, ou a classe não é `sealed`, ou tem construtor com parâmetro |
| Campo não aparece no Inspector | o tipo não é `float`/`int`/`bool`/`string` (`Vector2`, `Entity`, `Texture2D` nunca aparecem) |
| Personagem atravessa a parede construída | o índice do tile não está em `SolidTiles` do tilemap de construção |
| Personagem "engorda" nas paredes | `Collider` do tamanho do sprite; diminua e desloque pros pés |
| Barra da HUD parada | o `Vitals` não está escrevendo a variável, ou o nome não bate (`Vida` ≠ `vida`) |
| HUD não aparece | tela sem `"UI": true`, ou `UI.Show("hud")` nunca chamado, ou o id não é o nome do arquivo |
| Ataque sai na direção errada | mira do mouse sem `Camera.ScreenToWorld` (mouse vem em pixel de tela) |
| Um golpe mata o inimigo instantaneamente | `InvulnerabilityAfterHit` = 0 no `Health`: o mesmo golpe conta em vários frames |
| Efeito do golpe fica pra sempre na tela | clipe com `Loop = true` ou falta o `HitEffect` que destrói a entidade |
| O jogador some ao morrer | `DestroyOnDeath` ficou `true` no `Health` dele |
| Inimigo não persegue | falta `NavAgent`, ou o nome do alvo não é exatamente `Player` |
| Drop não é coletado | o item nasceu longe demais, ou `TargetName` não bate com o nome da entidade do jogador |
| Craft consome e não entrega | receita mal escrita na string (`Item*qtd` separado por `+`, receitas por `;`) |
| Nada acontece ao carregar o save | o save restaura variáveis, não componentes: reaplique `Vida` em `Health.Current` |
| Jogo trava com muitos inimigos | `MaxAlive` alto demais no spawner, ou drops com `Lifetime = 0` acumulando |

---

## 15. Cheat sheet do survivor

| Quero... | Uso |
|---|---|
| Mover | `Get<Transform>()!.Position += dir * speed * dt` |
| Ler direcional (teclado+gamepad) | `World?.Input?.AxisX / AxisY` |
| Ler joystick de toque | `World?.UI?.Find<UiJoystick>("hud", "MoveStick")?.Value` |
| Botão da HUD | `World?.UI?.Find<UiButton>("hud", "BotaoAtaque")?.Clicked` |
| Mira do mouse no mundo | `World?.Camera?.ScreenToWorld(input.MousePosition)` |
| Dar dano | `World?.Damage(alvo, valor, Entity)` |
| Curar | `World?.Heal(Entity, valor)` |
| Todos com vida | `World?.Query<Health>()` |
| Achar o jogador | `World?.TryFind("Player", out var player)` |
| Vida na HUD | `State.SetVariable("Vida", Get<Health>()!.Current)` + `UiBar` |
| Stamina / água | `State.GetVariable/SetVariable` (não existe componente pronto) |
| Moedas e materiais | `World?.Inventory?.Add("Moeda", 3)` / `GetCount` / `Has` |
| Mostrar contagem | `UiText` com `{Item:Moeda}` |
| Colocar bloco | `map.SetTile(tx, ty, índice)` |
| Tirar bloco | `map.SetTile(tx, ty, -1)` |
| Bloco vira parede | `map.SolidTiles.Add(índice)` |
| Tile sob um ponto | `TileMath.TileAt(mapEntity, posição)` |
| Perseguir | `Get<NavAgent>()?.SetTarget(pos)` |
| Nascer item no chão | `ItemDrop.Spawn(World!, "Moeda", 3, pos, "sprites/coin.png")` |
| Salvar / carregar | `World?.Save?.Save(0)` / `Load(0)` |
| Pausar | `World.Paused = true` |
| Som / música | `World?.Audio?.Play("audio/x.wav")` / `PlayMusic("audio/tema.ogg")` |
| Mensagem na tela | `World?.Dialogue?.ShowMessage("...")` |

---

## 16. O que este guia deixa de fora (e onde continuar)

- **Mundo procedural grande, caverna, minério, luz por propagação, ciclo dia/noite** — está
  pronto e comentado no sample `samples/Aurora.SlandSurvivor`; leitura em
  [GUIA-JOGO-SANDBOX.md](GUIA-JOGO-SANDBOX.md).
- **Menu principal, troca de cena, pausa com inventário, build final e Android** —
  [GUIA-JOGO-BASE.md](GUIA-JOGO-BASE.md) e [GUIA-ANDROID.md](GUIA-ANDROID.md).
- **NPC, loja, quests em estágios, diálogo com escolha** — seções 6.3, 6.4 e 8.3–8.7 da
  [REFERENCIA-SCRIPTS-RPG.md](REFERENCIA-SCRIPTS-RPG.md).
- **Coop LAN** — [GUIA-JOGO-COOP.md](GUIA-JOGO-COOP.md). Num survivor, lembre do
  `if (Get<NetworkIdentity>() is { IsMine: false }) return;` no topo do `Update` de
  `PlayerMove`, `PlayerCombat` e `Builder`.
- **Equipamento com estatística (arma que muda dano/alcance)** — não existe sistema pronto:
  o caminho curto é guardar o nome do equipado numa variável do `GameState` e o
  `PlayerCombat` ler `Damage`/`Reach` a partir dela.
- **Empilhamento por slot, peso, mochila com grade** — o `InventoryManager` é contagem por
  nome, sem slot. Mochila com grade é UI sua por cima desse dicionário (`Inventory.Items`).
