# Guia: Jogo Base Completo na Aurora Engine

Este guia monta um jogo 2D jogável do zero usando **só o Editor** (Assets → cenas → componentes),
com script mínimo só onde o Editor genuinamente não alcança (movimento do jogador, lógica de
ataque, boot da UI). Cobre: menu, troca de cena sem "UI grudada", HUD, pausa pra
inventário/configurações, player com collision e animação, ataques, partículas, diálogo com NPC,
pontos/dinheiro, inimigo com pathfinding, gamepad, save, resolução fixa, build final e export Android.

Todos os nomes de componente, campo e Action/Trigger citados aqui são os reais da engine — pode
copiar os trechos de JSON direto. Trechos marcados **testado** foram verificados nesta sessão
(rodando o jogo de verdade, ou em device Android real via `adb`); o resto segue o comportamento
já documentado no código-fonte.

---

## 0. Pré-requisitos

- `dotnet build src/Aurora.Editor` — editor compilado.
- `dotnet build src/Aurora.Runtime` — runtime compilado.
- Abrir `src/Aurora.Editor/bin/Debug/net10.0/Aurora.Editor.exe`.
- Editor fechado antes de rebuildar (`Aurora.Editor.exe` trava o build se estiver aberto).

---

## 1. Criar o projeto

**Arquivo → Novo Projeto…** (`Ctrl+Shift+N`). Escolha pasta e nome, ex. `MeuJogo`.

Gera automaticamente:

```
MeuJogo/
  MeuJogo.csproj          (referencia Aurora.Runtime)
  Program.cs               (using var game = new MeuJogoGame(); game.Run(...))
  MeuJogoGame.cs           (subclasse de Game — já carrega o menu e desenha a UI, ver abaixo)
  Spin.cs                  (script de exemplo com [SceneScript] — pode apagar ou manter de referência)
  aurora.project.json      (campo PROJETO já preenchido, Play funciona de cara)
  Assets/
    scenes/main.json       (cena inicial, com um sprite placeholder girando)
    scenes/MainMenu.json   (tela de UI: botão "Jogar" já ligado — ver seção 3)
    sprites/placeholder.png
    fonts/DejaVuSans.ttf   (fonte padrão, pro texto de UI já sair funcionando)
```

`MeuJogoGame.cs` já vem assim (não precisa escrever isso à mão):

```csharp
public sealed class MeuJogoGame : Game
{
    private Font _font = null!;

    protected override void OnLoad()
    {
        _font = Assets.LoadFont("fonts/DejaVuSans.ttf", 22f);
        UI.Load("scenes/MainMenu.json", Assets);
        LoadScene(BootScene ?? "scenes/main.json");
    }

    protected override void OnRenderUI(float dt)
    {
        UI.Draw(SpriteBatch, _font, State, Inventory, Quests, View.FramebufferSize.X, View.FramebufferSize.Y);
    }
}
```

Aperte **▶ Play** uma vez pra confirmar que builda e roda: deve aparecer o botão "Jogar"
centralizado. Clicar nele troca pra `scenes/main.json` (o placeholder girando).

> **Reabrir o projeto depois:** `Arquivo → Abrir Projeto…` (`Ctrl+Shift+O`) — escolhe a pasta
> do projeto (a que tem `aurora.project.json`) e reabre exatamente a última cena que você editou.

---

## 2. Estrutura de cenas do jogo

Um jogo completo normalmente usa (tudo dentro de `Assets/scenes/`; o que separa "cena de gameplay"
de "tela de UI" é só o campo `"UI": true` no JSON, não a pasta):

| Arquivo | Tipo (painel) | Papel |
|---|---|---|
| `scenes/MainMenu.json` | TELAS UI | Menu inicial — já vem pronto no scaffold (seção 1) |
| `scenes/main.json` | CENAS | Fase jogável (player, inimigos, moedas, NPC) |
| `scenes/GameplayUI.json` | TELAS UI | HUD/menu de pausa **durante** a fase (seções 5 e 6) |
| `scenes/PauseMenu.json` | TELAS UI | Tela de inventário/configurações sobreposta (seção 6) |
| `prefabs/Coin.json`, `Enemy.json`, `HitEffect.json` | PREFABS | Reuso |

Cria cena nova pelo **+** do painel CENAS; tela de UI pelo **+** do painel TELAS UI (o editor já
marca `"UI": true` sozinho).

---

## 3. Menu principal

A engine tem um elemento `UiButton`: retângulo clicável (mouse no Windows, toque no Android)
com texto centralizado e uma lista `OnClick` de ações — mesmo vocabulário do `EventTrigger`.
Editável 100% pelo Inspector (painel TELAS UI → abre a cena → seleciona o botão):

- **AnchorX/AnchorY** — agora é ComboBox no Inspector (`Left`/`Center`/`Right` e
  `Left`/`Center`/`Bottom`... na prática `Top`/`Center`/`Bottom` pro eixo Y). **Menu quase sempre
  quer `Center`/`Center`** — coordenada fixa tipo `"X": 540` só cai no meio numa tela de exatamente
  1080px de largura; celular real costuma ser bem mais largo, e sem âncora o menu fica desalinhado.
- **OnClick** — painel ONCLICK → **+ Adicionar Ação**. O dropdown de ação já lista `ChangeScene`,
  `ShowUI`, `HideUI`, `ToggleUI`, `SetPause` (entre outras) — e quando a ação é uma dessas 4
  primeiras, o campo "Nome"/"Arquivo"/"Tela UI" também vira ComboBox listando as cenas/telas reais
  do projeto (nada de digitar caminho + `.json` na mão, nem acertar o nome de cor).

O botão "Jogar" do scaffold já sai assim (pode editar texto/posição à vontade):

```json
{
  "Type": "UiButton", "X": 0, "Y": 0, "AnchorX": "Center", "AnchorY": "Center",
  "Width": 200, "Height": 48, "Text": "Jogar",
  "OnClick": [
    { "Action": "HideUI", "Name": "MainMenu" },
    { "Action": "ChangeScene", "Name": "scenes/main.json" }
  ]
}
```

`UIManager.Update` (chamado automaticamente pelo `Game` a cada frame) faz o hit-test e dispara
`OnClick` — não precisa nenhum script pra isso. `Wait` dentro de `OnClick` é ignorado (clique é
síncrono); `Teleport`/`Destroy`/`PlayAnimation` precisam de `Name` explícito (não há entidade
"Self" dona do botão).

---

## 4. Trocar de cena sem deixar UI "grudada" (leitura obrigatória)

Isso pegou nesta sessão e vale a pena entender antes de continuar: **tela de UI (`ShowUI`/
`HideUI`) e cena de gameplay (`ChangeScene`) são dois sistemas independentes.** `ChangeScene` troca
só o `World` (entidades da fase); a tela de UI carregada via `UI.Load` **continua desenhada por
cima pra sempre**, mesmo depois de `ChangeScene`, até você mandar `HideUI` nela.

Erro clássico: botão "Jogar" só com `ChangeScene` (sem `HideUI`) — a fase carrega por trás, mas o
menu continua na tela por cima, parecendo que "não fez nada". A regra de ouro:

> **Todo botão que troca de "modo" (menu → jogo, jogo → menu, jogo → pausa) precisa de um
> `HideUI` (esconde a tela atual) + `ShowUI` (mostra a próxima), além do `ChangeScene` se for
> o caso.** `Name` do `HideUI`/`ShowUI` é o nome do arquivo **sem** `.json`.

Uma armadilha de teste: o botão **Play do editor sempre roda a cena que está aberta no momento**
(estilo Unity — ver `MainViewModel.Play()`). Se você testar com `MainMenu.json` aberto, o
`BootScene` vira essa tela, e `LoadScene(BootScene)` tenta carregar a tela de UI como se fosse
gameplay — funciona sem crashar (`UiButton` não registrado vira log e é ignorado), mas o mundo
fica vazio, o que pode confundir. Pra testar o boot de verdade, deixa `scenes/main.json` (ou a
cena que faz sentido) aberta antes de dar Play.

---

## 5. HUD / menu durante a gameplay

Cria `scenes/GameplayUI.json` (painel TELAS UI → **+**) com o que precisa ficar visível **durante**
a fase — Ouro/Pontos/HP, e/ou um botão "Menu" que leva pra pausa (seção 6):

```json
{
  "Scene": "GameplayUI",
  "UI": true,
  "Objects": [
    { "Name": "Backdrop", "Components": [
      { "Type": "UiPanel", "X": 8, "Y": 8, "Width": 220, "Height": 60, "Color": "#000000A0" }
    ]},
    { "Name": "GoldLabel", "Components": [
      { "Type": "UiText", "X": 16, "Y": 14, "Text": "Ouro: {Gold}", "Color": "#FFD24DFF" }
    ]},
    { "Name": "MenuButton", "Components": [
      { "Type": "UiButton", "X": -16, "Y": 16, "AnchorX": "Right", "AnchorY": "Top",
        "Width": 100, "Height": 32, "Text": "Menu",
        "OnClick": [
          { "Action": "SetPause", "On": true },
          { "Action": "ShowUI", "Name": "PauseMenu" }
        ]
      }
    ]}
  ]
}
```

`{Gold}` puxa variável do `GameState`; `{Item:Nome}` puxaria quantidade de inventário;
`{Quest:Nome}` puxaria estágio de quest.

**Código obrigatório** (`GameplayUI` precisa carregar no boot e começar escondida — senão o botão
"Menu" aparece junto com o "Jogar" do `MainMenu`, ou não aparece nunca se você esquecer o
`UI.Load`). Em `MeuJogoGame.cs`, `OnLoad()`:

```csharp
protected override void OnLoad()
{
    _font = Assets.LoadFont("fonts/DejaVuSans.ttf", 22f);
    UI.Load("scenes/MainMenu.json", Assets);
    UI.Load("scenes/GameplayUI.json", Assets);
    UI.Hide("GameplayUI");
    State.SetVariable("Gold", 0);
    LoadScene(BootScene ?? "scenes/main.json");
}
```

E o botão "Jogar" do `MainMenu` precisa **mostrar** a `GameplayUI` ao entrar (seção 4):

```json
"OnClick": [
  { "Action": "HideUI", "Name": "MainMenu" },
  { "Action": "ShowUI", "Name": "GameplayUI" },
  { "Action": "ChangeScene", "Name": "scenes/main.json" }
]
```

---

## 6. Menu de pausa (inventário, configurações)

Diferente de "voltar ao menu principal" (que troca de cena e descarta o `World`), abrir um
inventário/tela de configurações **no meio da fase** precisa congelar o jogo sem descartar nada —
pra isso existe a ação `SetPause`.

`SetPause` (`On: true/false`) liga/desliga `World.Paused`: com pausa ligada, `World.Update` para
de rodar Behaviors, colisão, partículas e vida — a cena continua desenhada atrás, só parada. UI
continua respondendo a clique normalmente (pausa não afeta `UIManager`).

Cria `scenes/PauseMenu.json` (carregada e escondida no boot, igual `GameplayUI` — mais uma linha
de `UI.Load`/`UI.Hide` em `OnLoad`):

```json
{
  "Scene": "PauseMenu",
  "UI": true,
  "Objects": [
    { "Name": "CloseButton", "Components": [
      { "Type": "UiButton", "X": 0, "Y": 0, "AnchorX": "Center", "AnchorY": "Center",
        "Width": 200, "Height": 48, "Text": "Continuar",
        "OnClick": [
          { "Action": "SetPause", "On": false },
          { "Action": "HideUI", "Name": "PauseMenu" }
        ]
      }
    ]}
  ]
}
```

Pra também dar a opção de sair de vez pro menu principal a partir da pausa (sem deixar o `World`
"vivo" escondido atrás), aponte outro botão pra uma cena de gameplay **vazia de verdade** (não
pra `MainMenu.json` — ela é tela de UI, não cena; ver seção 4), por exemplo `scenes/Boot.json`
(`{"Scene": "Boot", "Objects": []}`):

```json
"OnClick": [
  { "Action": "SetPause", "On": false },
  { "Action": "HideUI", "Name": "PauseMenu" },
  { "Action": "HideUI", "Name": "GameplayUI" },
  { "Action": "ShowUI", "Name": "MainMenu" },
  { "Action": "ChangeScene", "Name": "scenes/Boot.json" }
]
```

---

## 7. Player: Transform, Collider, Animator

Na cena `main.json`, crie a entidade `Player` (**+ Nova**), depois **+ Add** estes componentes:

- **SpriteRenderer** — arraste o spritesheet do player.
- **Collider** — `Shape: Box`, ajuste Width/Height pro hitbox; `IsSolid: true`.
- **Animator** — `FrameWidth`/`FrameHeight` do spritesheet, `SheetColumns`.

Clipes (botão **+ Clipe** no Animator):

```json
"Clips": [
  { "Name": "idle",   "Duration": 0.2,  "Frames": [0,1,2,3], "Loop": true },
  { "Name": "walk",   "Duration": 0.1,  "Frames": [4,5,6,7], "Loop": true },
  { "Name": "attack", "Duration": 0.06, "Frames": [8,9,10],  "Loop": false }
]
```

Transições (botão **+ Transição**) — troca de clipe sozinha, sem código:

```json
"Transitions": [
  { "From": "idle",   "To": "walk",  "Parameter": "Speed",  "CompareOp": ">=", "CompareValue": 1 },
  { "From": "walk",   "To": "idle",  "Parameter": "Speed",  "CompareOp": "<",  "CompareValue": 1 },
  { "From": "Any",    "To": "attack","Parameter": "Attack", "IsBool": true,    "BoolValue": true },
  { "From": "attack", "To": "idle",  "Parameter": "Attack", "IsBool": true,    "BoolValue": false }
]
```

A última regra é o que tira do `attack` — sem ela o Animator fica preso no último frame pra
sempre (o clipe não é `Loop`). Por isso o script precisa chamar `anim.SetBool("Attack", false)`
depois que o golpe termina (ver comentário no script abaixo).

Quem alimenta `Speed`/`Attack` é o script do player (próxima seção) chamando
`anim.SetFloat("Speed", ...)` / `anim.SetBool("Attack", ...)`.

---

## 8. Script do player (movimento + ataque)

Esse é o único código realmente necessário pro movimento. Crie `PlayerController.cs` no projeto:

> **Ordem de execução:** `Game.OnUpdate` roda **antes** de `World.Update` a cada frame — é lá
> que `Behavior.Update` de cada entidade (Animator incluso) realmente executa. Se seu `Game`
> chama `anim.SetBool("Attack", true)` direto no `OnUpdate` e no mesmo instante lê
> `anim.CurrentClip`, ainda vai ver o clipe antigo — a transição só é avaliada no `Update` do
> Animator, que roda *depois*, e só fica visível no frame seguinte. Normal, não trava nem perde
> estado, só não é instantâneo dentro do mesmo tick.

```csharp
using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Input;
using Aurora.Runtime.Scenes;
using Silk.NET.Input;

namespace MeuJogo;

[SceneScript]
public sealed class PlayerController : Behavior
{
    public float Speed = 100f;
    public float AttackCooldown = 0.4f;

    public InputManager? Input; // injetado pelo Game (ver abaixo) — null até o primeiro frame

    private float _attackTimer;
    private bool _attacking;

    public override void Update(float dt)
    {
        if (Input is null) return;

        var transform = Get<Transform>()!;
        var anim = Get<Animator>();

        // AxisX/AxisY já combinam teclado (WASD/setas) + analógico esquerdo do gamepad
        // sozinhos — suporte a controle sem nenhum código extra aqui.
        var move = new Vector2(Input.AxisX, Input.AxisY);

        if (move.LengthSquared() > 0f)
        {
            move = Vector2.Normalize(move);
            transform.Position += move * Speed * dt;
        }

        anim?.SetFloat("Speed", move.Length() * Speed);

        if (_attackTimer > 0f)
        {
            _attackTimer -= dt;
            if (_attackTimer <= 0f && _attacking)
            {
                _attacking = false;
                anim?.SetBool("Attack", false); // sem isso o Animator trava no clipe attack
            }
        }

        // Espaço no teclado OU botão A do gamepad atacam.
        bool attackPressed = Input.WasKeyPressed(Key.Space) || Input.WasGamepadButtonPressed(ButtonName.A);
        if (attackPressed && _attackTimer <= 0f)
        {
            _attacking = true;
            _attackTimer = AttackCooldown;
            anim?.SetBool("Attack", true);
            // Dano em inimigos próximos: exponha World pro script (mesmo esquema do Input
            // abaixo) e filtre Query<Transform, Collider>() por distância, ou marque inimigos
            // com uma tag própria. Efeito visual: instancie o prefab HitEffect (seção 10).
        }
    }
}
```

`Behavior` só acessa componentes da própria entidade via `Get<T>()` — sem `Input`/`World`
diretos. O `Game` injeta a dependência por propriedade pública assim que a cena carrega
(scripts `[SceneScript]` exigem construtor sem parâmetro, então não dá pra injetar no
construtor):

```csharp
protected override void OnUpdate(float dt)
{
    if (World.TryFind("Player", out var player))
    {
        var pc = player.Get<PlayerController>();
        if (pc is not null && pc.Input is null)
            pc.Input = Input;
    }
}
```

---

## 9. Collision

- **Chão/paredes**: `Tilemap` com `SolidTiles` (ex.: `"1, 2"` = índices de tile sólidos). O
  `Player`/inimigos com `Collider` são empurrados pra fora automaticamente — zero código.
- **Pickups (moeda)**: `Collider` com `IsSolid: false` (vira trigger, não bloqueia), **ou** mais
  simples: `EventTrigger` `PlayerTouch` na própria moeda (seção 12) — sem precisar de `Collider`
  nenhum nela.
- **Paredes soltas** (objeto avulso, não tile): entidade com `Transform` + `Collider`
  (`IsSolid: true`, `IsKinematic: true`).

---

## 10. Partículas (impacto de ataque, brilho de moeda)

`ParticleEmitter` não tem "modo rajada" dedicado — simula rajada com `MaxParticles` baixo +
desligando `Emitting` logo depois de nascer. Prefab `HitEffect.json`:

```json
{
  "Name": "HitEffect",
  "Components": [
    { "Type": "Transform", "X": 0, "Y": 0 },
    { "Type": "ParticleEmitter", "Rate": 40, "MaxParticles": 10,
      "LifeMin": 0.2, "LifeMax": 0.4, "SpeedMin": 40, "SpeedMax": 90,
      "SizeStart": 6, "SizeEnd": 0,
      "ColorStart": "#FFCC44FF", "ColorEnd": "#FF000000" }
  ]
}
```

Salve como prefab (selecione a entidade → **Salvar como Prefab…**), depois instancie via
duplo-clique no painel PREFABS ou, em runtime, carregue o JSON do prefab e adicione via
`Scenes.Load`/`World.CreateEntity` manual no ponto de impacto. Depois de ~0.5s, destrua a
entidade do efeito (`Entity.Destroy()` num Behavior com timer, ou reaproveite `EventTrigger`
`Timer` + Action `Destroy`).

---

## 11. Diálogo com NPC

Zero código. `PlayerTouch` funciona só por distância entre Transforms — não precisa de
`Collider` nenhum (só use `Collider` se também quiser que o NPC bloqueie passagem física,
caso em que ele fica `IsSolid: true` separado do trigger de diálogo). Entidade `NPC`:

```json
{ "Type": "EventTrigger", "Trigger": "PlayerTouch", "Radius": 20, "Once": true,
  "Actions": [
    { "Action": "ShowMessage", "Name": "Ferreiro", "Text": "Bem-vindo à forja!" },
    { "Action": "ShowChoice", "Text": "Quer comprar uma espada por 10 de ouro?",
      "Options": [
        { "Text": "Sim", "Switch": "comprou_espada" },
        { "Text": "Não" }
      ]
    }
  ]
}
```

**Não** encadeie um `EventTrigger` `SwitchOn` puro pra cobrar o ouro: `RemoveItem` nunca deixa a
quantidade negativa (trava em 0), e `EventTrigger` não combina duas condições (AND) num só
componente — não dá pra checar "`HasItem` Gold≥10 **e** switch ligado" de uma vez, então o
jogador ganharia a espada de graça sem ouro suficiente. Pra uma loja de verdade use um script
pequeno como gate, injetado igual `Input` no `PlayerController`:

```csharp
[SceneScript]
public sealed class ShopKeeper : Behavior
{
    public string Switch = "comprou_espada";
    public string Currency = "Gold";
    public int Price = 10;
    public string ItemToBuy = "Espada";

    public GameState? State { get; set; }         // injetado pelo Game
    public InventoryManager? Inventory { get; set; }
    public DialogueSystem? Dialogue { get; set; }

    public override void Update(float dt)
    {
        if (State is null || Inventory is null || !State.GetSwitch(Switch)) return;
        State.SetSwitch(Switch, false); // consome, permite tentar de novo depois

        if (Inventory.GetCount(Currency) >= Price)
        {
            Inventory.Add(Currency, -Price);
            Inventory.Add(ItemToBuy, 1);
            Dialogue?.ShowMessage($"{ItemToBuy} comprada!");
        }
        else
        {
            Dialogue?.ShowMessage("Ouro insuficiente.");
        }
    }
}
```

```json
{ "Name": "Shop", "Components": [
  { "Type": "Transform", "X": -60, "Y": 0 },
  { "Type": "ShopKeeper", "Switch": "comprou_espada", "Currency": "Gold", "Price": 10, "ItemToBuy": "Espada" }
] }
```

```csharp
// No Game, junto da injeção de Input:
foreach (var (_, shop) in World.Query<ShopKeeper>())
{
    shop.State = State;
    shop.Inventory = Inventory;
    shop.Dialogue = Dialogue;
}
```

Seu jogo precisa chamar `Dialogue.Draw(...)` e ler `Input` (Espaço/Enter avança, W/S ou setas
navegam escolha) em `OnRenderUI`/`OnUpdate` — ver `samples/Aurora.Sandbox.Core/SandboxGame.cs`
pro padrão pronto.

---

## 12. Pontos e dinheiro

Duas opções, ambas sem código:

- **Variável simples** (`GameState`, via `SetVariable`) — mais direto pra "Ouro"/"Pontos" como
  número solto:
  ```json
  { "Action": "SetVariable", "Name": "Points", "Op": "Add", "Value": 10 }
  ```
- **Inventário** (`InventoryManager`, via `AddItem`/`RemoveItem`) — melhor se "ouro" convive
  com outros itens (poções, chaves) que também têm quantidade:
  ```json
  { "Action": "AddItem", "Name": "Gold", "Value": 5 }
  ```

Moeda coletável, sem `Collider`, só `EventTrigger`:

```json
{
  "Name": "Coin1",
  "Components": [
    { "Type": "Transform", "X": 120, "Y": 80 },
    { "Type": "SpriteRenderer", "Texture": "sprites/coin.png" },
    { "Type": "EventTrigger", "Trigger": "PlayerTouch", "Radius": 12, "Once": true,
      "Actions": [
        { "Action": "AddItem", "Name": "Gold", "Value": 1 },
        { "Action": "SetVariable", "Name": "Points", "Op": "Add", "Value": 10 },
        { "Action": "PlaySound", "Name": "sounds/coin.wav" },
        { "Action": "Destroy" }
      ]
    }
  ]
}
```

`GameplayUI` (seção 5) já mostra `{Item:Gold}` ou `{Gold}` conforme a opção escolhida.

---

## 13. Inimigo com pathfinding

`Enemy` com `Transform`, `SpriteRenderer`, `Collider` (`IsSolid: true`), `NavAgent`:

```json
{ "Type": "NavAgent", "Speed": 60, "ArriveThreshold": 4 }
```

A grade de navegação nasce sozinha do primeiro `Tilemap` com `SolidTiles` da cena — não
precisa desenhar área andável à parte. Um script simples persegue o player:

```csharp
[SceneScript]
public sealed class EnemyAI : Behavior
{
    public float RepathInterval = 0.4f;
    public float ChaseRange = 200f;

    public Entity TargetEntity;   // injetado pelo Game, igual Input no PlayerController
    public bool HasTargetEntity;

    private float _timer;

    public override void Update(float dt)
    {
        if (!HasTargetEntity) return;

        _timer -= dt;
        if (_timer > 0f) return;
        _timer = RepathInterval;

        var nav = Get<NavAgent>();
        var transform = Get<Transform>();
        var targetTransform = TargetEntity.Get<Transform>();
        if (nav is null || transform is null || targetTransform is null) return;

        float dist = Vector2.Distance(transform.Position, targetTransform.Position);
        if (dist <= ChaseRange)
            nav.SetTarget(targetTransform.Position);
        else
            nav.Stop();
    }
}
```

```csharp
// No Game, junto da injeção de Input. NÃO faça World.TryFind("Enemy1", ...) —
// só liga UM inimigo com esse nome exato e todo o resto do bicho fica parado.
// Use Query pra pegar TODOS os EnemyAI da cena de uma vez:
bool hasPlayer = World.TryFind("Player", out var player);
foreach (var (_, enemy) in World.Query<EnemyAI>())
{
    enemy.HasTargetEntity = hasPlayer;
    if (hasPlayer) enemy.TargetEntity = player;
}
```

Salve `Enemy` como prefab pra espalhar várias cópias pela fase.

---

## 14. Gamepad

Suporte a controle (analógico + botões) já existe em `InputManager`, backend-agnóstico (mesmo
código no desktop e — em teoria, não validado em device real ainda — no Android via SDL):

- `Input.AxisX` / `Input.AxisY` já combinam teclado+analógico esquerdo automaticamente — se seu
  `PlayerController` já usa essas duas props (seção 8), suporte a controle é de graça.
- `Input.IsGamepadButtonDown(ButtonName.A)` / `Input.WasGamepadButtonPressed(ButtonName.A)` —
  A/B/X/Y, `LeftBumper`/`RightBumper`, `DPadUp/Down/Left/Right`, `Start`/`Back`, etc.
  (`using Silk.NET.Input;` pro `ButtonName`).
- `Input.LeftStick`/`Input.RightStick` (`Vector2`, com deadzone) e
  `Input.LeftTrigger`/`Input.RightTrigger` (`float`, 0..1) pra controle fino além do `AxisX/Y`.
- `Input.IsGamepadConnected` — útil pra só mostrar dica de botão de controle na tela se tiver um plugado.

Tudo isso é código (`Input.*`), não tem equivalente no editor — não faz sentido pra Trigger/Action
de cena, já que controle é por script do jogador, não por entidade da cena.

---

## 15. Salvar progresso

`Save` já persiste `GameState` (variáveis/switches) + `Inventory` + `Quests` + a posição
(`Transform`) da entidade `Player` juntos — carregar um save volta o jogador exatamente onde
salvou, não onde o JSON da cena originalmente colocou. Gatilho comum: item/alavanca de save,
ou tecla dedicada.

```json
{ "Action": "Save", "Value": 0 }
```

(`Value` = número do slot.)

---

## 16. Resolução fixa / letterbox (opcional)

Por padrão a câmera/UI se ajustam ao tamanho real da janela/tela — o jogo mostra mais ou menos
mundo dependendo do aparelho. Pra travar numa proporção fixa (barra preta centralizada no resto,
em vez de esticar/cortar — importante em mobile, onde a proporção de tela varia muito),
defina `DesignResolution` **antes** de `Run(...)`, em `Program.cs`:

```csharp
using var game = new MeuJogoGame();
game.DesignResolution = new(1280, 720); // ou 720x1280 pra jogo vertical (seção 17)
game.ParseArgs(args);
game.Run("MeuJogo");
```

Isso também corrige mouse/toque automaticamente (sem essa correção o clique ficaria visualmente
certo mas fisicamente errado) — não precisa mexer em mais nada.

---

## 17. Build final (desktop)

**Arquivo → Build Jogo (Release)…** — escolhe pasta, publica self-contained pra plataforma
atual, abre a pasta no Explorer ao terminar. `PROJETO` no Inspector precisa apontar pro
`.csproj` (não `.exe`) pra essa opção funcionar.

---

## 18. Exportar Android (vertical, paisagem, rotação)

**Arquivo → Exportar Android (APK)…** gera um segundo projeto Android a partir do jogo desktop
e builda em Release automaticamente.

No Inspector, campo **"Orientação Android"** (novo — persiste em `aurora.project.json`):

| Opção | Comportamento |
|---|---|
| `Landscape` (padrão) | Paisagem fixa, nunca gira |
| `Portrait` | **Retrato fixo, nunca gira — é assim que se faz jogo vertical.** |
| `SensorLandscape` | Gira entre paisagem normal/invertida com o sensor |
| `SensorPortrait` | Gira entre retrato normal/invertido com o sensor |
| `Sensor` | Gira livre nas 4 orientações (retrato + paisagem) com o sensor |

Um bug antigo do Silk.NET/SDL no Android (crash ao rotacionar durante o boot) é por isso que o
padrão sempre foi paisagem fixa. **Retestado manualmente nesta sessão** num device Android 14
real (Xiaomi/MIUI): `Sensor` completo, incluindo troca pra retrato, rodou sem crashar. Isso pode
variar por aparelho/versão de Android/driver — se o seu device crashar ao girar, volta pra
`Landscape`/`Portrait` fixo.

Pra jogo vertical: `Portrait` (fixo) é o caminho seguro; combine com `DesignResolution` na
vertical (ex. `new(720, 1280)`, seção 16) pra travar a câmera/UI na proporção certa também.

---

## Checklist

- [ ] Novo Projeto criado, Play funcionando no scaffold padrão (botão "Jogar" já aparece)
- [ ] Botão "Jogar": `HideUI(MainMenu)` + `ShowUI(GameplayUI)` + `ChangeScene` — não só o `ChangeScene`
- [ ] `GameplayUI` carregada e escondida no boot (`UI.Load`/`UI.Hide` em `OnLoad`)
- [ ] Menu de pausa: `SetPause(On:true)` ao abrir, `SetPause(On:false)` ao fechar
- [ ] `Player`: SpriteRenderer + Collider + Animator (clips + transitions) + PlayerController
- [ ] `PlayerController` usa `Input.AxisX`/`AxisY` (gamepad de graça)
- [ ] Tilemap com SolidTiles definindo o chão/paredes da fase
- [ ] Ao menos uma moeda com EventTrigger PlayerTouch (AddItem/SetVariable + Destroy)
- [ ] NPC com EventTrigger PlayerTouch → ShowMessage/ShowChoice
- [ ] Prefab HitEffect (ParticleEmitter) instanciado no ataque do player
- [ ] Ao menos um `Enemy` com NavAgent perseguindo o player
- [ ] Ação Save amarrada a algum gatilho
- [ ] Build Jogo (Release) gerando executável standalone
- [ ] (Mobile) Orientação Android escolhida de propósito (Portrait fixo pra vertical, Sensor* só se testado no seu device)

Com isso: menu, HUD, pausa, player, collision, ataque, partícula, animação, diálogo,
pontos/dinheiro, inimigo com IA de movimento, gamepad, save e export Android — tudo no editor,
com só ~2-3 scripts pequenos (`PlayerController`, `EnemyAI`, opcionalmente `ShopKeeper`) de
código real.
