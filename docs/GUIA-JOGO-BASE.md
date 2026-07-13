# Guia: Jogo Base Completo na Aurora Engine

Este guia monta um jogo 2D jogável do zero usando **só o Editor** (Assets → cenas → componentes),
com script mínimo só onde o Editor genuinamente não alcança (movimento do jogador, lógica de
ataque). Cobre: menu, player com collision e animação, ataques, partículas, diálogo com NPC,
pontos/dinheiro, inimigo com pathfinding, e build final.

Todos os nomes de componente, campo e Action/Trigger citados aqui são os reais da engine
(testados nesta sessão) — pode copiar os trechos de JSON direto.

> **Testado ponta a ponta:** montei um jogo seguindo este guia (menu → mundo com tilemap,
> player, 2 moedas, NPC com loja, inimigo perseguidor) e rodei com um roteiro automatizado
> (`--smoke`, mesmo padrão do `samples/Aurora.Sandbox.Core`) que teleporta o jogador em cada
> gatilho e confere o resultado. Os 6 sistemas bateram: troca de cena, coleta de moeda
> (`AddItem`+`SetVariable`+`Destroy`), diálogo com escolha, transição `attack` do Animator,
> e perseguição do inimigo via `NavAgent`. Achei e documentei 2 detalhes reais nesse teste
> (ver avisos nas seções 6 e 9) — o resto funcionou exatamente como descrito abaixo.

---

## 0. Pré-requisitos

- `dotnet build src/Aurora.Editor` — editor compilado.
- `dotnet build src/Aurora.Runtime` — runtime compilado.
- Abrir `src/Aurora.Editor/bin/Debug/net10.0/Aurora.Editor.exe`.

---

## 1. Criar o projeto

**Arquivo → Novo Projeto…** (`Ctrl+Shift+N`). Escolha nome, ex. `MeuJogo`.

Gera automaticamente:

```
MeuJogo/
  MeuJogo.csproj          (referencia Aurora.Runtime)
  Program.cs               (using var game = new MeuJogoGame(); game.Run(...))
  MeuJogoGame.cs           (subclasse de Game, OnLoad() carrega a cena)
  Spin.cs                  (script de exemplo com [SceneScript] — pode apagar ou manter de referência)
  aurora.project.json      (campo PROJETO já preenchido, Play funciona de cara)
  Assets/
    scenes/main.json       (cena inicial, já com um sprite placeholder girando)
    sprites/placeholder.png
```

Aperte **▶ Play** uma vez pra confirmar que builda e roda antes de continuar.

---

## 2. Estrutura de cenas do jogo

Um jogo completo normalmente usa:

| Arquivo | Tipo (painel) | Papel |
|---|---|---|
| `Assets/scenes/MainMenu.json` | CENAS | Tela de título, "aperte Enter" |
| `Assets/scenes/World.json` | CENAS | Fase jogável (player, inimigos, moedas, NPC) |
| `Assets/ui/hud.json` | TELAS UI | HUD persistente (ouro, pontos, vida) — sobrevive a troca de cena |
| `Assets/prefabs/Coin.json`, `Enemy.json`, `HitEffect.json` | PREFABS | Reuso |

Crie `MainMenu` e `World` pelo botão **+** do painel CENAS; a tela de UI pelo **+** do painel TELAS UI.

---

## 3. Menu principal

A engine **não tem botão de UI clicável ainda** (UiText/UiImage/UiBar/UiPanel são só visuais,
sem input). O jeito zero-código: menu "aperte Enter".

Na cena `MainMenu.json`, crie uma entidade com texto de instrução (`+ Nova` → adiciona
`SpriteRenderer` com a arte do título, se tiver) e um `EventTrigger`:

```json
{
  "Name": "MenuLogic",
  "Components": [
    { "Type": "Transform", "X": 0, "Y": 0 },
    { "Type": "EventTrigger", "Trigger": "KeyPress", "Key": "Enter", "Once": true,
      "Actions": [
        { "Action": "ChangeScene", "Name": "scenes/World.json" }
      ]
    }
  ]
}
```

> Menu com várias opções navegáveis (Novo Jogo / Continuar / Sair) dá pra fazer reaproveitando
> `DialogueSystem.ShowChoice` (a mesma caixa de escolha do diálogo, ver seção 10) chamada no
> `SceneStart`, ou com um script `[SceneScript]` próprio lendo `Input.WasKeyPressed(Key.Up/Down)`.
> Fica de exercício — o "aperte Enter" já destrava jogo completo sem escrever isso.

---

## 4. HUD persistente

Tela de UI `hud.json` (painel TELAS UI → **+**). Edite os componentes pelo Inspector (sem
canvas visual ainda — X/Y são pixel de tela, campo numérico mesmo):

```json
{
  "Scene": "hud",
  "UI": true,
  "Objects": [
    { "Name": "Backdrop", "Components": [
      { "Type": "UiPanel", "X": 8, "Y": 8, "Width": 220, "Height": 60, "Color": "#000000A0" }
    ]},
    { "Name": "GoldLabel", "Components": [
      { "Type": "UiText", "X": 16, "Y": 14, "Text": "Ouro: {Gold}", "Color": "#FFD24DFF" }
    ]},
    { "Name": "PointsLabel", "Components": [
      { "Type": "UiText", "X": 16, "Y": 34, "Text": "Pontos: {Points}", "Color": "#FFFFFFFF" }
    ]},
    { "Name": "HpBar", "Components": [
      { "Type": "UiBar", "X": 16, "Y": 52, "Width": 150, "Height": 10,
        "Variable": "HP", "Max": 100, "FillColor": "#40C040FF", "BackColor": "#303030FF" }
    ]}
  ]
}
```

`{Gold}` e `{Points}` puxam variáveis do `GameState` (seção 11); `{Item:Nome}` puxaria
quantidade de inventário; `{Quest:Nome}` puxaria estágio de quest.

Pra a HUD aparecer e persistir entre cenas, carregue ela uma vez em `OnLoad()` do seu
`MeuJogoGame.cs` e desenhe em `OnRenderUI`:

```csharp
protected override void OnLoad()
{
    UI.Load("Assets/ui/hud.json", Assets);
    State.SetVariable("Gold", 0);
    State.SetVariable("Points", 0);
    State.SetVariable("HP", 100);
    LoadScene(BootScene ?? "scenes/MainMenu.json");
}

protected override void OnRenderUI(float dt)
{
    UI.Draw(SpriteBatch, _font, State, Inventory, Quests); // _font: Assets.LoadFont(...)
}
```

---

## 5. Player: Transform, Collider, Animator

Na cena `World.json`, crie a entidade `Player` (**+ Nova**), depois **+ Add** estes componentes:

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

## 6. Script do player (movimento + ataque)

Esse é o único código realmente necessário. Crie `PlayerController.cs` no projeto:

> **Ordem de execução (testada):** `Game.OnUpdate` roda **antes** de `World.Update` a cada
> frame — é lá que `Behavior.Update` de cada entidade (Animator incluso) realmente executa.
> Se seu `Game` chama `anim.SetBool("Attack", true)` direto no `OnUpdate` e no mesmo instante
> lê `anim.CurrentClip`, ainda vai ver o clipe antigo — a transição só é avaliada no
> `Update` do Animator, que roda *depois*, e só fica visível no frame seguinte. Isso é normal,
> não trava nem perde o estado, só não é instantâneo dentro do mesmo tick.

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

        var move = Vector2.Zero;
        if (Input.IsKeyDown(Key.D) || Input.IsKeyDown(Key.Right)) move.X += 1;
        if (Input.IsKeyDown(Key.A) || Input.IsKeyDown(Key.Left))  move.X -= 1;
        if (Input.IsKeyDown(Key.S) || Input.IsKeyDown(Key.Down))  move.Y += 1;
        if (Input.IsKeyDown(Key.W) || Input.IsKeyDown(Key.Up))    move.Y -= 1;

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

        if (Input.WasKeyPressed(Key.Space) && _attackTimer <= 0f)
        {
            _attacking = true;
            _attackTimer = AttackCooldown;
            anim?.SetBool("Attack", true);
            // Dano em inimigos próximos: exponha World pro script (mesmo esquema do Input
            // abaixo) e filtre Query<Transform, Collider>() por distância, ou marque inimigos
            // com uma tag própria. Efeito visual: instancie o prefab HitEffect (seção 8).
        }
    }
}
```

`Behavior` só acessa componentes da própria entidade via `Get<T>()` — sem `Input`/`World`
diretos. O `Game` injeta a dependência por propriedade pública assim que a cena carrega
(scripts `[SceneScript]` exigem construtor sem parâmetro, então não dá pra injetar no
construtor como o sample antigo faz):

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

## 7. Collision

- **Chão/paredes**: `Tilemap` com `SolidTiles` (ex.: `"1, 2"` = índices de tile sólidos). O
  `Player`/inimigos com `Collider` são empurrados pra fora automaticamente — zero código.
- **Pickups (moeda)**: `Collider` com `IsSolid: false` (vira trigger, não bloqueia). Detecte
  com `OnTriggerEnter` num Behavior, **ou** mais simples: `EventTrigger` `PlayerTouch` na
  própria moeda (seção 11) — sem precisar de `Collider` nenhum nela.
- **Paredes soltas** (objeto avulso, não tile): entidade com `Transform` + `Collider`
  (`IsSolid: true`, `IsKinematic: true`).

---

## 8. Partículas (impacto de ataque, brilho de moeda)

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
`MainViewModel.CreatePrefabInstance` no editor (duplo-clique no painel PREFABS) ou, em
runtime, carregue o JSON do prefab e adicione via `Scenes.Load`/`World.CreateEntity` manual
no ponto de impacto. Depois de ~0.5s, destrua a entidade do efeito (`Entity.Destroy()` num
Behavior com timer, ou reaproveite `EventTrigger` `Timer` + Action `Destroy`).

---

## 9. Diálogo com NPC

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

Encadeie outro `EventTrigger` (`SwitchOn`, `Switch: comprou_espada`) pra dar o item e cobrar o ouro:

```json
{ "Type": "EventTrigger", "Trigger": "SwitchOn", "Switch": "comprou_espada", "Once": true,
  "Actions": [
    { "Action": "RemoveItem", "Name": "Gold", "Value": 10 },
    { "Action": "AddItem", "Name": "Espada", "Value": 1 },
    { "Action": "ShowMessage", "Text": "Espada comprada!" }
  ]
}
```

Seu jogo precisa chamar `Dialogue.Draw(...)` e `Input` (Espaço/Enter avança, W/S ou setas
navegam escolha) em `OnRenderUI`/`OnUpdate` — ver `samples/Aurora.Sandbox.Core/SandboxGame.cs`
pro padrão pronto.

> **Limitação conhecida (testada):** esse exemplo cobra o ouro sem checar se o jogador tem o
> suficiente antes — `RemoveItem` nunca deixa a quantidade negativa (trava em 0), então o
> jogador ganha a espada de graça se não tiver ouro. `EventTrigger` não combina duas condições
> (AND) num só componente, então não dá pra checar "`HasItem` Gold≥10 **e** switch ligado" de
> uma vez sem código. Pra uma loja de verdade, ou aceite essa simplificação num protótipo, ou
> resolva num script pequeno (`if (Inventory.Has("Gold", 10)) { ...cobra e dá o item... }`
> chamado a partir do `OnChosen` do `ShowChoice`/de um Behavior que observa o switch).

---

## 10. Pontos e dinheiro

Duas opções, ambas sem código:

- **Variável simples** (`GameState`, via `SetVariable`/`AddVariable`) — mais direto pra
  "Ouro"/"Pontos" como número solto:
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

HUD (seção 4) já mostra `{Item:Gold}` ou `{Gold}` conforme a opção escolhida.

---

## 11. Inimigo com pathfinding

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
// No Game, junto da injeção de Input:
if (World.TryFind("Enemy1", out var enemy))
{
    var ai = enemy.Get<EnemyAI>();
    if (ai is not null && !ai.HasTargetEntity && World.TryFind("Player", out var target))
    {
        ai.TargetEntity = target;
        ai.HasTargetEntity = true;
    }
}
```

Salve `Enemy` como prefab pra espalhar várias cópias pela fase. **Testado:** o inimigo se
move de verdade em direção ao player (confirmado medindo a posição antes/depois num
roteiro automatizado) sem atravessar as paredes do tilemap.

---

## 12. Salvar progresso

`Save` já persiste `GameState` (variáveis/switches) + `Inventory` + `Quests` juntos.
Gatilho comum: item/alavanca de save, ou tecla dedicada.

```json
{ "Action": "Save", "Value": 0 }
```

(`Value` = número do slot.)

---

## 13. Build final

**Arquivo → Build Jogo (Release)…** — escolhe pasta, publica self-contained pra plataforma
atual, abre a pasta no Explorer ao terminar. `PROJETO` no Inspector precisa apontar pro
`.csproj` (não `.exe`) pra essa opção funcionar.

---

## Checklist

- [ ] Novo Projeto criado, Play funcionando no scaffold padrão
- [ ] Cena `MainMenu` com EventTrigger KeyPress → ChangeScene
- [ ] Tela de UI `hud.json` com Gold/Points/HP, carregada em `OnLoad`
- [ ] `Player`: SpriteRenderer + Collider + Animator (clips + transitions) + PlayerController
- [ ] Tilemap com SolidTiles definindo o chão/paredes da fase
- [ ] Ao menos uma moeda com EventTrigger PlayerTouch (AddItem/SetVariable + Destroy)
- [ ] NPC com EventTrigger PlayerTouch → ShowMessage/ShowChoice
- [ ] Prefab HitEffect (ParticleEmitter) instanciado no ataque do player
- [ ] Ao menos um `Enemy` com NavAgent perseguindo o player
- [ ] Ação Save amarrada a algum gatilho
- [ ] Build Jogo (Release) gerando executável standalone

Com isso: menu, player, collision, ataque, partícula, animação, diálogo, pontos/dinheiro,
inimigo com IA de movimento e save — tudo no editor, com só ~2 scripts pequenos
(`PlayerController`, `EnemyAI`) de código real.
