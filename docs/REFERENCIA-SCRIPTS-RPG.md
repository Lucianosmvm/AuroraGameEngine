# Referência de Scripts — API da Engine (com exemplos de RPG)

Este documento é a referência do que dá pra usar dentro de um script `[SceneScript]` (a
classe que herda `Behavior`). Pra um tutorial passo a passo de como montar um jogo do zero,
veja `docs/GUIA-JOGO-BASE.md` — este arquivo aqui é consulta, não tutorial.

---

## 1. Os dois mundos: Componentes vs. C# normal

Um script tem acesso a **duas coisas diferentes**, e é fácil confundir:

1. **Componentes da própria entidade** — só via `Get<T>()`. É "dado" anexado à entidade
   pelo JSON da cena (`Transform`, `SpriteRenderer`, `Health`, outro script seu, etc.).
   Lista completa na seção 4.
2. **Sistemas da engine** — `InputManager`, `GameState`, `InventoryManager`,
   `QuestManager`, `DialogueSystem`, `UIManager`, `World`, `SaveManager`. Esses **não**
   vêm de `Get<T>()` — `Behavior` só recebe `World` automaticamente; todo o resto precisa
   ser **injetado na mão** pelo `Game.OnUpdate` (seção 6.6 explica o padrão).

Fora isso, é C# normal: pode usar LINQ, `System.Numerics.Vector2`, qualquer lib referenciada
no `.csproj`. A engine não sandboxa nada.

---

## 2. `Behavior` — classe base de todo script

`src/Aurora.Runtime/Ecs/Behavior.cs`

```csharp
public abstract class Behavior : IComponent
{
    public Entity Entity { get; }          // a própria entidade
    public World? World { get; }            // injetado automaticamente
    public bool Enabled { get; set; } = true;

    public virtual void Start() { }                                   // 1x, primeiro frame ativo
    public virtual void Update(float deltaTime) { }                   // todo frame
    public virtual void OnDestroy() { }                                // entidade destruída
    public virtual void OnCollision(Entity other, CollisionInfo info) { }  // colisão sólida
    public virtual void OnTriggerEnter(Entity other) { }               // entrou num trigger
    public virtual void OnTriggerExit(Entity other) { }                // saiu de um trigger
    public virtual void OnDamaged(float amount, Entity? source) { }    // dano real aplicado
    public virtual void OnDeath() { }                                   // Health.Current chegou a 0

    protected T? Get<T>() where T : class, IComponent;                 // atalho pra Entity.Get<T>()
}
```

`Enabled = false` pausa `Update` da entidade sozinha (não afeta as outras). Um script que
lança exceção é **desligado automaticamente** pelo `World` (não derruba o jogo inteiro).

### Campos scriptáveis (editáveis no Inspector / JSON da cena)

Só **`float`, `int`, `bool`, `string`** — campo/propriedade pública com um desses tipos vira
campo editável automaticamente, pelo próprio nome. Qualquer outro tipo (`Vector2`, `Entity`,
`InputManager`, listas, enums) **não aparece no Inspector** — só existe em C#, e precisa ser
setado via injeção manual (seção 6.6) ou lógica interna do próprio script.

```csharp
[SceneScript]
public sealed class Exemplo : Behavior
{
    public float Speed = 100f;      // aparece no Inspector, editável por entidade
    public int Ammo = 6;            // aparece
    public bool IsBoss = false;     // aparece
    public string DisplayName = ""; // aparece

    public InputManager? Input;     // NÃO aparece — precisa injeção manual
    private Vector2 _velocity;      // privado, nunca aparece
}
```

`Enabled` (herdado de `Behavior`) também é scriptável — todo script ganha um toggle
"Enabled" de graça no Inspector.

Requisito pro discovery funcionar: `[SceneScript]`, classe `sealed`, **construtor sem
parâmetro** (é assim que o editor instancia pra ler os valores-padrão).

---

## 3. `Entity` e `World`

### `Entity` (struct)

```csharp
entity.Id            // int
entity.Name          // string (não garantido único)
entity.IsAlive        // bool
entity.Add<T>(component)
entity.Get<T>()        // T?
entity.Has<T>()         // bool
entity.Destroy()
```

Não existe sistema de "tags" — só nome (`Name`) e composição de componente (`Has<T>()`).
Pra RPG, é comum usar prefixo no nome pra filtrar (`Name.StartsWith("Enemy")`) ou simplesmente
`Has<EnemyAI>()`.

### `World` (via `Behavior.World`, ou `Game.World`)

```csharp
World.CreateEntity(string name = "Entity") -> Entity
World.TryFind(string name, out Entity entity) -> bool     // 1º match por nome
World.Query<T1>() -> IEnumerable<(Entity, T1)>              // itera todo mundo com T1
World.Query<T1, T2>() -> IEnumerable<(Entity, T1, T2)>       // idem, 2 componentes
World.Destroy(Entity entity)                                  // adiado até fim do frame se em Update
World.Damage(Entity target, float amount, Entity? source = null) -> bool  // respeita i-frames, dispara OnDamaged/OnDeath
World.Heal(Entity target, float amount)                        // clampa em Health.Max
World.Paused                                                     // bool — pausa Update/colisão/partículas/vida
```

`World.TryFind` só pega o **primeiro** — pra várias entidades do mesmo tipo (inimigos, NPCs),
use `Query<T>()`:

```csharp
foreach (var (entity, enemy) in World.Query<EnemyAI>())
{
    if (enemy.Enabled) enemy.TargetEntity = playerEntity;
}
```

---

## 4. Referência de componentes

Todo componente abaixo é lido via `Get<T>()`. Campos aqui são os de verdade (nome, tipo,
default) — não confundir com "scriptável" (essa regra do float/int/bool/string é só pros
**seus** `[SceneScript]`, componentes nativos já têm leitor/escritor próprio no engine).

### `Transform`
```csharp
Position  Vector2  (0,0)
Rotation  float    0        // radianos
Scale     Vector2  (1,1)
```

### `SpriteRenderer`
```csharp
Texture     Texture2D?  null
Color       Color       White
Origin      Vector2     (0.5, 0.5)
Size        Vector2?    null      // null = tamanho natural da textura
Layer       int         0
FlipX/FlipY bool        false
Visible     bool        true
SourceRect  RectF?      null      // recorte da textura (spritesheet manual)
```

### `Health` — vida, base pra sistema de dano em RPG
```csharp
Max                        float  100
Current                     float  100   // NÃO setar direto — usa World.Damage/Heal
InvulnerabilityAfterHit     float  0     // segundos de i-frame após levar dano
Invulnerable                bool   false
DestroyOnDeath              bool   true
IsDead                       bool  { get; }  // Current <= 0
```

### `Collider`
```csharp
Shape        ColliderShape (Box | Circle)  Box
Width/Height float 16/16                    // se Shape=Box
Radius       float 8                         // se Shape=Circle
Offset       Vector2 (0,0)
IsSolid      bool true    // true = empurra fisicamente + OnCollision; false = trigger (OnTriggerEnter/Exit)
IsKinematic  bool false   // true = nunca é empurrado (paredes, chão)
Layer        int 1        // bitmask 1-30
Mask         int ~0       // com quem colide (todos por padrão)
```
Chão/parede de `Tilemap`: qualquer tile em `SolidTiles` empurra colliders não-kinemáticos
automaticamente, sem precisar de `Collider` na própria tile.

### `Animator`
```csharp
FrameWidth/FrameHeight  int  0/0
SheetColumns            int  1
Clips        List<AnimationClip>       // { Name, Frames[], FrameDuration, Loop }
Transitions  List<AnimatorTransition>  // { From, To, Parameter, IsBool, CompareOp, CompareValue, BoolValue }
CurrentClip  string?  { get; }
IsFinished   bool     { get; }

SetFloat(name, value) / GetFloat(name, fallback = 0)
SetBool(name, value)  / GetBool(name)
Play(clipName, restart = false)
Stop()
```
Parâmetros (`SetFloat`/`SetBool`) são locais ao `Animator` — não é o mesmo que
`GameState.SetVariable`. `Transitions` são avaliadas 1x por frame; `From: "Any"` casa com
qualquer clipe atual.

### `Camera` (`CameraController`)
```csharp
Follow        string?  null   // nome da entidade a seguir
FollowSpeed   float    5
Zoom          float    1
Offset        Vector2  (0,0)
ViewWidth/Height int   1280/720
ClampBounds   bool     false
BoundsX/Y/Width/Height  float  0,0,1280,720
```

### `NavAgent` — pathfinding automático (inimigos que perseguem, NPCs que patrulham)
```csharp
Speed             float  100
ArriveThreshold   float  4
HasTarget / IsMoving  bool  { get; }

SetTarget(float x, float y) / SetTarget(Vector2 target)
Stop()
```
Movimento e desvio de `SolidTiles` acontecem sozinhos dentro de `World.Update` — o script só
chama `SetTarget` quando quer mudar o destino.

### `ParticleEmitter`
```csharp
Texture Texture2D? null        // null = quadrado branco
Rate    float 10                 // partículas/seg
Emitting bool true
LifeMin/LifeMax   float 0.6/1.2
SpeedMin/SpeedMax float 20/60
AngleMin/AngleMax float 0/360
SizeStart/SizeEnd float 8/0
ColorStart/ColorEnd Color White / White(alpha 0)
Gravity Vector2 (0,0)
SpawnAreaWidth/Height float 0/0
Layer int 0
MaxParticles int 200
```

### `Projectile` — já vem com lógica de dano pronta (Behavior, não só dado)
```csharp
Life         float   2
Damage       float   20
TargetPrefix string  ""    // filtro por prefixo de nome; "" = qualquer um com Health, exceto Source
Velocity     Vector2 (0,0) // setado no spawn, não editável em JSON
Source       Entity? null  // quem atirou, passado pro World.Damage
```
Precisa de `Transform` + `Collider{IsSolid:false}` na mesma entidade. Uso: instancia via
prefab, seta `Velocity`/`Source` em código, o resto é sozinho (`OnTriggerEnter` já aplica
dano e se autodestrói).

### `Tilemap`
```csharp
Tileset Texture2D? null
TileWidth/Height int 16/16
Width/Height int 0/0      // dimensão do grid
Layer int 0
Tiles int[] []            // Width*Height índices, -1 = vazio
SolidTiles HashSet<int> [] // índices que bloqueiam movimento

GetTile(x, y) / SetTile(x, y, index)
```

### `GlobalTint` / `Light2D` — luz/escurecimento de cena (masmorra, noite)
```csharp
// GlobalTint
Color Color FromBytes(0,0,40)
Intensity float 0.3   // 0=sem efeito, 1=sólido
Enabled bool true

// Light2D
Radius float 100
Color Color FromBytes(255,220,150)
Intensity float 1
Enabled bool true
```

---

## 5. `InputManager`

Exposto como `Game.Input`, precisa ser injetado manualmente no script (seção 6.6).

```csharp
IsKeyDown(Key key) / WasKeyPressed(Key key)          // held / pressionado neste frame
AxisX / AxisY  float  { get; }                        // WASD+setas OU stick esquerdo, combinados
LeftStick / RightStick  Vector2
LeftTrigger / RightTrigger  float  0..1
IsGamepadButtonDown(ButtonName) / WasGamepadButtonPressed(ButtonName)
IsGamepadConnected  bool
MousePosition  Vector2
IsMouseDown(MouseButton = Left) / WasMouseClicked(MouseButton = Left)
ActiveTouches  IReadOnlyList<(int Id, Vector2 Position)>   // Android
```

`AxisX`/`AxisY` já resolvem teclado+gamepad sozinhos — não precisa checar os dois na mão.

---

## 6. Sistemas de RPG (todos em `Game`, precisam injeção manual no script)

### 6.1 `GameState` — variáveis e switches globais (estilo RPG Maker)
```csharp
GetVariable(name, fallback = 0) -> float
SetVariable(name, value)
AddVariable(name, delta)
GetSwitch(name) -> bool
SetSwitch(name, on)
Changed  event Action?
```
Uso típico: ouro, reputação, flags de progresso ("MatouChefao1" = switch).

### 6.2 `InventoryManager` — itens por nome, contagem
```csharp
GetCount(item) -> int
Has(item, count = 1) -> bool
Add(item, delta)     // nunca fica negativo
Remove(item, count)  // = Add(item, -count)
Changed  event Action?
```

### 6.3 `QuestManager` — id → estágio (int)
```csharp
GetStage(quest) -> int          // 0 = não iniciada
IsAtLeast(quest, stage) -> bool
SetStage(quest, stage)
Advance(quest, delta = 1)
Changed  event Action?
```
Não guarda texto de quest — isso é responsabilidade do autor (via `ShowMessage`/UI).

### 6.4 `DialogueSystem` — caixa de diálogo (fila)
```csharp
IsActive  bool { get; }
Current   DialogueEntry?
ShowMessage(text, speaker = null)
ShowChoice(prompt, options, onChosen: Action<int>)
Advance()          // dispensa mensagem / confirma escolha
SelectNext() / SelectPrevious()
Draw(spriteBatch, font, screenWidth, screenHeight)   // chamar em OnRenderUI
```
Não existe componente `Npc` — diálogo de NPC é `EventTrigger` (`PlayerTouch`) chamando
`ShowMessage`/`ShowChoice`, ou script customizado chamando `Dialogue` direto.

### 6.5 `UIManager` — HUD/menus (`Game.UI`)
```csharp
Load(path, assets) -> UiScreen
Show(id) / Hide(id) / Toggle(id) -> bool
IsVisible(id) -> bool
Find<T>(screenId, elementName) -> T?   // ex.: ler UiJoystick.Value do código
```
Telas de UI (JSON com `"UI": true`) são independentes da cena de gameplay — trocar de cena
(`ChangeScene`) não esconde HUD sozinho, precisa `HideUI`/`ShowUI` explícito.

### 6.6 Padrão de injeção manual

`Behavior` só ganha `World` de graça. Tudo mais (`Input`, `State`, `Inventory`, `Quests`,
`Dialogue`) precisa de propriedade pública settable no script + `Game.OnUpdate` injetando:

```csharp
// No script (campo NÃO scriptável — tipo não é float/int/bool/string, tudo bem):
public InputManager? Input;
public GameState? State;
public InventoryManager? Inventory;

// No Game (MeuJogoGame.cs):
protected override void OnUpdate(float dt)
{
    if (World.TryFind("Player", out var player))
    {
        var pc = player.Get<PlayerController>();
        if (pc is not null && pc.Input is null) pc.Input = Input;
    }
    foreach (var (_, shop) in World.Query<ShopKeeper>())
    {
        shop.State ??= State;
        shop.Inventory ??= Inventory;
        shop.Dialogue ??= Dialogue;
    }
}
```

### 6.7 `SaveManager` (`Game.Save`)
```csharp
Save(slot = 0) / Load(slot = 0)
HasSave(slot = 0) -> bool
Delete(slot = 0)
GetInfo(slot = 0) -> SaveInfo?
AutoSave() / LoadAutoSave() / HasAutoSave()
PlayerEntityName  string  "Player"   // entidade cuja Transform.Position é salva
```
Salva `GameState` (vars+switches) + `Inventory` + `Quests` + posição do `Player`. Disparado
via ação `Save` de `EventTrigger`, ou chamando `Game.Save.Save()` direto.

---

## 7. `EventTrigger` — lógica sem código (RPG Maker style)

Componente `IComponent`, não `Behavior` — a lógica roda no `EventSystem` central, não no
próprio script.

```csharp
Trigger   string   "PlayerTouch"   // SceneStart | PlayerTouch | SwitchOn | KeyPress | Timer
                                     // | VariableCompare | HasItem | QuestStageAtLeast
Switch    string?                   // p/ SwitchOn
Radius    float 20                  // p/ PlayerTouch (pixels)
Key       string "E"                // p/ KeyPress (nome do Silk.NET.Input.Key)
Interval  float 5                   // p/ Timer
Variable  string?                   // nome var/item/quest p/ VariableCompare|HasItem|QuestStageAtLeast
CompareOp string ">="               // ==, !=, >=, <=, >, <
CompareValue float 0
Once      bool true
Actions   List<EventAction>
```

Ações suportadas (`Type` de cada `EventAction`): `SetVariable`, `SetSwitch`, `Teleport`,
`Destroy`, `Damage`, `Heal`, `ShowMessage`, `Save`, `AddItem`, `RemoveItem`, `SetQuestStage`,
`AdvanceQuest`, `ShowUI`, `HideUI`, `ToggleUI`, `ChangeScene`, `SetPause`, `Quit`,
`PlaySound`, `PlayMusic`, `StopMusic`, `PlayAnimation`, `StopAnimation`, `SetActive`,
`ShowChoice`, `Wait`.

---

## 8. Exemplos de uso — RPG

### 8.1 Movimento + animação (já visto no guia base)
```csharp
[SceneScript]
public sealed class PlayerController : Behavior
{
    public float Speed = 100f;
    public InputManager? Input;

    public override void Update(float dt)
    {
        if (Input is null) return;
        var transform = Get<Transform>()!;
        var anim = Get<Animator>();

        var move = new Vector2(Input.AxisX, Input.AxisY);
        if (move.LengthSquared() > 0f)
        {
            move = Vector2.Normalize(move);
            transform.Position += move * Speed * dt;
        }
        anim?.SetFloat("Speed", move.Length() * Speed);
    }
}
```

### 8.2 Coletável (moeda) — zero código, só `EventTrigger`
```json
{
  "Name": "Moeda",
  "Components": [
    { "Type": "Transform", "X": 300, "Y": 150 },
    { "Type": "SpriteRenderer", "Texture": "sprites/coin.png" },
    { "Type": "EventTrigger", "Trigger": "PlayerTouch", "Radius": 16, "Once": true,
      "Actions": [
        { "Type": "AddItem", "Name": "Gold", "Value": 10 },
        { "Type": "Destroy" }
      ] }
  ]
}
```

### 8.3 NPC com diálogo + escolha
```json
{
  "Name": "Anciao",
  "Components": [
    { "Type": "Transform", "X": 500, "Y": 300 },
    { "Type": "SpriteRenderer", "Texture": "sprites/npc_old.png" },
    { "Type": "Collider", "IsSolid": true, "IsKinematic": true },
    { "Type": "EventTrigger", "Trigger": "KeyPress", "Key": "E", "Once": false,
      "Actions": [
        { "Type": "ShowMessage", "Text": "Viajante... aceita ajudar a vila?" },
        { "Type": "ShowChoice", "Text": "Aceitar a missão?",
          "Options": [ { "Text": "Sim" }, { "Text": "Não" } ] }
      ] }
  ]
}
```
`KeyPress` só dispara perto do NPC se ele também tiver `Collider` bloqueando o jogador —
senão o jogador nunca fica "parado" perto o bastante. Alternativa mais simples: `PlayerTouch`
com `Radius` pequeno, sem precisar de tecla.

### 8.4 Loja com custo — precisa de script (EventTrigger não faz "E se X E Y")
```csharp
[SceneScript]
public sealed class ShopKeeper : Behavior
{
    public int PotionCost = 10;

    public GameState? State;
    public InventoryManager? Inventory;
    public DialogueSystem? Dialogue;

    public void TryBuyPotion()
    {
        if (Inventory is null || State is null || Dialogue is null) return;

        if (Inventory.GetCount("Gold") >= PotionCost)
        {
            Inventory.Remove("Gold", PotionCost);
            Inventory.Add("Potion", 1);
            Dialogue.ShowMessage("Poção comprada!");
        }
        else
        {
            Dialogue.ShowMessage("Ouro insuficiente.");
        }
    }
}
```
Chame `TryBuyPotion()` a partir de um `EventTrigger` `KeyPress`/`PlayerTouch` não dá — ação
`RunActions` não chama método de script direto. Padrão real: liga a compra num `UiButton`
(`OnClick`) que dispara um evento próprio, ou chama o método via `World.Query<ShopKeeper>()`
dentro do `Game.OnUpdate` quando detecta a tecla de interação.

### 8.5 HUD de vida/ouro/quest (`UiText`/`UiBar` com tokens)
```json
{ "Type": "UiBar", "Name": "HpBar", "X": 20, "Y": 20, "AnchorX": "Left", "AnchorY": "Top",
  "Width": 160, "Height": 14, "Variable": "PlayerHP", "Max": 100,
  "FillColor": "#E04040FF", "BackColor": "#00000080" }
```
```json
{ "Type": "UiText", "Name": "GoldLabel", "X": -20, "Y": 20,
  "AnchorX": "Right", "AnchorY": "Top", "Text": "Ouro: {Item:Gold}" }
```
`PlayerHP` precisa ser sincronizada em `Update` do `PlayerController`:
```csharp
State?.SetVariable("PlayerHP", Get<Health>()?.Current ?? 0);
```

### 8.6 Inimigo perseguidor com dano por contato
```csharp
[SceneScript]
public sealed class EnemyAI : Behavior
{
    public float SightRange = 150f;
    public float ContactDamage = 10f;

    public Entity? TargetEntity;   // injetado pelo Game (World.TryFind("Player"))

    public override void Update(float dt)
    {
        if (TargetEntity is not { IsAlive: true } target) return;

        var nav = Get<NavAgent>();
        var transform = Get<Transform>();
        if (nav is null || transform is null) return;

        var targetPos = target.Get<Transform>()?.Position ?? transform.Position;
        if (Vector2.Distance(transform.Position, targetPos) <= SightRange)
            nav.SetTarget(targetPos);
        else
            nav.Stop();
    }

    public override void OnCollision(Entity other, CollisionInfo info)
    {
        if (other.Has<Health>())
            World?.Damage(other, ContactDamage, Entity);
    }
}
```
Pra várias instâncias, injeta com `Query`, não `TryFind`:
```csharp
protected override void OnUpdate(float dt)
{
    if (!World.TryFind("Player", out var player)) return;
    foreach (var (_, enemy) in World.Query<EnemyAI>())
        enemy.TargetEntity ??= player;
}
```

### 8.7 Quest com múltiplos estágios
```csharp
// Ao entregar item pro NPC (via ação customizada ou script de interação):
if (Inventory.Has("Erva Medicinal", 3) && Quests.GetStage("CurarVila") == 1)
{
    Inventory.Remove("Erva Medicinal", 3);
    Quests.SetStage("CurarVila", 2);
    Dialogue.ShowMessage("Obrigado, viajante! A vila está salva.");
}
```
```json
{ "Type": "UiText", "Text": "Missão: {Quest:CurarVila}" }
```

---

## 9. Cheat sheet

| Quero... | Uso |
|---|---|
| Mover entidade | `Get<Transform>()!.Position += ...` |
| Tocar animação | `Get<Animator>()?.SetFloat/SetBool(...)` |
| Causar dano | `World.Damage(target, amount, source)` |
| Curar | `World.Heal(target, amount)` |
| Perseguir alvo | `Get<NavAgent>()?.SetTarget(pos)` |
| Ler teclado/gamepad | `Input.AxisX/AxisY`, `WasKeyPressed`, `WasGamepadButtonPressed` |
| Ouro/pontos | `Inventory.Add("Gold", n)` / `GetCount` |
| Flag global | `State.SetSwitch("X", true)` / `GetSwitch` |
| Progresso de quest | `Quests.SetStage/GetStage/Advance` |
| Falar com jogador | `Dialogue.ShowMessage(...)` / `ShowChoice(...)` |
| Trocar de cena | `Game.LoadScene(...)` / ação `ChangeScene` |
| Achar entidade única | `World.TryFind(name, out entity)` |
| Achar todas de um tipo | `World.Query<T>()` |
| Salvar jogo | `Game.Save.Save(slot)` / ação `Save` |
| Pausar | `World.Paused = true` / ação `SetPause` |

Referência de arquivos-fonte, se quiser ler o código de verdade:
- `src/Aurora.Runtime/Ecs/Behavior.cs`, `Entity.cs`, `World.cs`
- `src/Aurora.Runtime/Ecs/Components/*.cs`
- `src/Aurora.Runtime/Input/InputManager.cs`
- `src/Aurora.Runtime/GameState.cs`, `InventoryManager.cs`, `QuestManager.cs`
- `src/Aurora.Runtime/UI/DialogueSystem.cs`, `UIManager.cs`, `UiElement.cs`
- `src/Aurora.Runtime/Events/EventTrigger.cs`, `EventSystem.cs`
- `src/Aurora.Runtime/Saves/SaveManager.cs`
- `src/Aurora.Runtime/Scenes/SceneScriptAttribute.cs`, `SceneSerializer.cs`
