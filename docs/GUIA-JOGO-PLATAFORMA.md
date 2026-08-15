# Guia — Jogo de Plataforma 2D

Referência prática do que muda para um jogo de plataforma (gravidade, pulo, colisão com o
chão) na Aurora. Tudo aqui está **rodando** em `samples/Aurora.Platformer`: duas fases,
colisão do jogador com o tilemap, pulo com coyote time e buffer, moedas, espinhos e bandeira
que leva para a próxima fase.

```bash
dotnet run --project samples/Aurora.Platformer
```

Controles: **A/D** ou setas movem, **Espaço/W/↑** pulam (analógico esquerdo + **A** no
controle), **R** reinicia a fase, **ESC** sai.

```bash
dotnet run --project samples/Aurora.Platformer -- --smoke
```

Roda o roteiro automatizado: confere que o chão segura o jogador, que o pulo sobe e volta,
que moeda e espinho reagem ao toque e que a bandeira troca de fase. Falhou, o processo morre
com exceção — é o teste de ponta a ponta desse sample.

Para abrir uma fase no editor:

```bash
dotnet run --project src/Aurora.Editor -- samples/Aurora.Platformer/Assets/scenes/level1.json
```

---

## 1. As três regras que quebram um platformer na Aurora

Antes do código, os três detalhes que fazem o jogador atravessar o chão ou o mapa não colidir:

1. **A entidade do tilemap precisa de `Transform`.** A colisão (e o desenho) roda em cima de
   `Query<Transform, Tilemap>` — tilemap sem Transform simplesmente não existe para a física,
   sem erro nenhum no console. É o bug mais fácil de cometer montando a cena na mão.
2. **`SolidTiles` precisa listar os índices sólidos.** Vazio = mapa decorativo, ninguém
   colide. No JSON aceita `"0, 1, 2, 3"` (formato do editor) ou `[0, 1, 2, 3]`.
3. **Y cresce para BAIXO.** Gravidade é `+Y`, pulo é `-Y`, e o topo do mapa é `Y = 0`.

---

## 2. Como a engine resolve colisão (e por que o script é escrito assim)

O `World.Update` roda **todos** os `Behavior.Update` primeiro e só depois faz o passo de
colisão, que empurra quem ficou sobreposto para fora e chama `OnCollision`. Não existe
"mover com colisão" — o script integra a posição livremente e o passo seguinte conserta.

Consequências práticas, todas visíveis em `Scripts/PlatformerController.cs`:

- **Descobrir se há chão** é olhar a normal no callback. `info.Normal` aponta para fora do
  outro corpo, ou seja, é a direção em que a engine te empurrou:

  ```csharp
  public override void OnCollision(Entity other, CollisionInfo info)
  {
      if (info.Normal.Y < -0.5f)        // empurrado para cima = chão embaixo
      {
          _grounded = true;
          if (_velocity.Y > 0f) _velocity.Y = 0f;
      }
      else if (info.Normal.Y > 0.5f)    // bateu a cabeça
      {
          if (_velocity.Y < 0f) _velocity.Y = 0f;
      }
      else if (MathF.Abs(info.Normal.X) > 0.5f)
      {
          _velocity.X = 0f;             // parede
      }
  }
  ```

- **`_grounded` é zerado no fim do `Update`**, não no começo. O passo de colisão vem logo
  depois do Update e remarca se ainda houver chão. Zerar no começo do Update apagaria o
  resultado da colisão do frame anterior e o jogador nunca conseguiria pular.

- **Velocidade de queda tem teto** (`MaxFallSpeed`). A colisão testa sobreposição na posição
  **já atualizada** — ela não varre o caminho. Se um frame mover mais que a espessura do
  tile, o jogador atravessa o chão (tunneling). Regra: `MaxFallSpeed * MaxDeltaTime` tem que
  ser menor que `TileHeight`. No sample: `600 * (1/45) ≈ 13 px < 16 px`.

  ```csharp
  MaxDeltaTime = 1f / 45f;   // em OnLoad do Game
  ```

- **Trigger que fica dentro/encostado em tile sólido precisa de `IsKinematic: true`.** A
  resolução contra tilemap empurra *todo* collider não-cinemático, inclusive trigger — sem o
  kinematic, moeda e espinho saem flutuando para fora do cenário. Moeda e espinho do sample
  usam `IsSolid: false` (não bloqueia) + `IsKinematic: true` (não é empurrado).

---

## 3. `PlatformerController` — os números que importam

Todos são campos públicos de um `[SceneScript]`, então aparecem no Inspector e no JSON da
cena; dá para tunar sem recompilar.

| Campo | Padrão | O que faz |
|---|---|---|
| `MoveSpeed` | 150 | Velocidade horizontal máxima (px/s) |
| `Acceleration` / `Friction` | 1200 / 1400 | Quão rápido acelera e freia (px/s²) |
| `AirControl` | 0.6 | Fração da aceleração/atrito valendo no ar |
| `Gravity` | 1400 | px/s² |
| `JumpSpeed` | 420 | Impulso do pulo (px/s) |
| `JumpCut` | 0.45 | Soltar o botão subindo corta a subida para essa fração |
| `MaxFallSpeed` | 600 | Teto da queda (ver tunneling acima) |
| `CoyoteTime` | 0.10 | Tempo em que ainda dá para pular depois da beirada |
| `JumpBufferTime` | 0.12 | Tempo em que um pulo apertado cedo demais ainda vale |
| `FallLimitY` | por fase | Y a partir do qual "caiu no vazio" e volta ao spawn |

**Altura do pulo** = `JumpSpeed² / (2 * Gravity)` → `420² / 2800 ≈ 63 px` (~4 tiles de 16).
**Alcance horizontal** de um pulo cheio ≈ `MoveSpeed * 2 * JumpSpeed / Gravity` → `150 * 0.6 = 90 px`
(~5,6 tiles). Use esses dois números para desenhar fase — é o que define buraco atravessável
e plataforma alcançável.

Coyote time e jump buffer não são luxo: sem eles o pulo "come" entrada em beirada e em queda,
e o jogo parece travado mesmo com a física certa.

Dois pontos de entrada além do teclado, úteis para toque no Android, cutscene ou teste:

```csharp
controller.ExternalAxis = 1f;   // eixo horizontal mantido (-1..1), somado ao teclado
controller.RequestJump();       // pulo agendado, respeitando coyote/buffer
```

---

## 4. A cena de uma fase

Cinco tipos de objeto, nada além disso (veja `Assets/scenes/level1.json`):

```jsonc
// chão: 1 draw call, colisão inclusa
{ "Name": "Ground", "Components": [
  { "Type": "Transform", "X": 0, "Y": 0 },
  { "Type": "Tilemap", "Texture": "tilesets/terrain.png",
    "TileWidth": 16, "TileHeight": 16, "Width": 60, "Height": 20,
    "SolidTiles": "0, 1, 2, 3", "Tiles": [ /* 60*20 índices, -1 = vazio */ ] } ] }

// jogador
{ "Name": "Player", "Components": [
  { "Type": "Transform", "X": 40, "Y": 260 },
  { "Type": "SpriteRenderer", "Texture": "sprites/player.png", "Layer": 10 },
  { "Type": "Collider", "Width": 12, "Height": 24 },
  { "Type": "PlatformerController", "FallLimitY": 440 } ] }

// câmera presa no jogador, limitada ao retângulo da fase
{ "Name": "Camera", "Components": [
  { "Type": "Transform", "X": 32, "Y": 272 },
  { "Type": "CameraController", "Follow": "Player", "FollowSpeed": 8, "Zoom": 3,
    "ClampBounds": true, "BoundsX": 0, "BoundsY": 0,
    "BoundsWidth": 960, "BoundsHeight": 320 } ] }

// bandeira: fim de fase, sem script nenhum
{ "Name": "Goal", "Components": [
  { "Type": "Transform", "X": 920, "Y": 224 },
  { "Type": "SpriteRenderer", "Texture": "sprites/flag.png", "Layer": 5 },
  { "Type": "EventTrigger", "Trigger": "PlayerTouch", "Radius": 18, "Once": true,
    "Actions": [
      { "Action": "ShowMessage", "Text": "Fase 1 concluída!" },
      { "Action": "ChangeScene", "Name": "scenes/level2.json" } ] } ] }
```

O collider do jogador é **mais estreito que o sprite** (12 de largura para um sprite de 16):
sobra folga para não enroscar em quina de tile ao correr rente à parede.

`ClampBounds` usa `Math.Clamp` com o meio-viewport — a fase precisa ser **maior que a área
visível**, senão o mínimo passa o máximo e a câmera lança exceção todo frame. Com
`Zoom: 3` e `DesignResolution` de 1280x720, a área visível é 426x240; as fases do sample têm
960x320.

`DesignResolution` no `Game` trava esse enquadramento em qualquer tamanho de janela — sem
ele, redimensionar a janela mudaria a área visível e quebraria o cálculo de bounds.

### Moeda e espinho: dois scripts de 10 linhas

```csharp
[SceneScript]
public sealed class Coin : Behavior
{
    public int Value = 1;

    public override void OnTriggerEnter(Entity other)
    {
        if (other.Get<PlatformerController>() is null) return;   // só o jogador coleta
        World?.State?.AddVariable("Coins", Value);
        Entity.Destroy();
    }
}

[SceneScript]
public sealed class Hazard : Behavior
{
    public override void OnTriggerEnter(Entity other)
        => other.Get<PlatformerController>()?.Respawn();
}
```

`OnTriggerEnter` dispara **na borda de entrada** (uma vez por encostada). É o gatilho certo
aqui — `EventTrigger` com `PlayerTouch` reavalia distância todo frame e, com `Once: false`,
dispararia repetido enquanto o jogador estivesse dentro do raio.

`[SceneScript]` faz o registro sozinho: nada de `Scenes.Register` no `Game`, e os campos
públicos `float`/`int`/`bool`/`string` viram propriedades editáveis no Inspector.

---

## 5. Desenhando fase que dá para jogar

O sample tem duas fases de 60x20 tiles. As regras que sobreviveram ao teste (um bot que anda
sempre para a direita e pula em beirada/parede/espinho atravessa as duas sem morrer):

- **Buraco** de até 4-5 tiles é atravessável com folga; 3 tiles é confortável.
- **Subida** de até 3 tiles (48 px) cabe no pulo; 4 tiles (63 px) é o limite teórico, não use.
- **Depois de qualquer beirada, deixe ~6 tiles limpos** onde o jogador vai aterrissar. Pulo
  cheio anda ~90 px no ar: espinho logo depois de um buraco é morte garantida, não desafio.
- **Não coloque plataforma logo acima de onde o jogador precisa pular.** Ele bate a cabeça,
  perde a subida e cai no obstáculo seguinte. No sample a plataforma de madeira da fase 1
  termina 3 tiles antes dos espinhos exatamente por isso.
- **Espinho em par** (dois tiles juntos) é melhor que dois espinhos separados por um tile: o
  pulo passa por cima dos dois de uma vez, em vez de exigir aterrissagem milimétrica no vão.
- **Tile decorativo** (o tijolo escuro, índice 4) fica fora de `SolidTiles` — serve para dar
  volume ao cenário sem virar parede.

Fase 1 ensina: correr, buraco, plataforma, espinho, escadinha de plataformas até a bandeira.
Fase 2 cobra: subida em plataformas de 3 tiles, platô com espinhos, descida controlada e
torre final.

---

## 6. HUD e troca de fase

O `PlatformerGame` só faz três coisas além de carregar a cena:

```csharp
protected override void OnRenderUI(float dt)
{
    DrawLabel($"Moedas: {(int)State.GetVariable("Coins")}", new Vector2(16f, 14f), ...);
    Dialogue.Draw(SpriteBatch, _font, ScreenSize.X, ScreenSize.Y);
}
```

`Coins` e `Deaths` são variáveis globais do `GameState` — sobrevivem à troca de cena de
graça (e entram no save). A mensagem da bandeira usa a caixa de diálogo da engine: enquanto
`Dialogue.IsActive`, o `Game` desliga o controller (`controller.Enabled = false`) e a
sequência de ações do evento fica parada até o jogador dispensar — só então o `ChangeScene`
roda.

---

## 7. O que este sample **não** faz (e como seria)

- **Plataforma móvel**: `Collider` cinemático movido por script funciona como parede que
  anda, mas o jogador **não é carregado junto** — não há parentesco de transform. Precisaria
  o script da plataforma somar o próprio deslocamento na posição de quem está apoiado.
- **Plataforma atravessável por baixo** (one-way): a resolução é simétrica, então exigiria
  ignorar a colisão quando `_velocity.Y < 0` — hoje isso não dá para expressar só com
  `SolidTiles`.
- **Animação**: o jogador é um sprite único. Com um sprite sheet, um `Animator` e três
  clipes (`idle`/`run`/`jump`) o controller escolheria pelo estado — ver a seção de animação
  do `README.md`.
- **Controle por toque**: `ExternalAxis` e `RequestJump()` já são os ganchos; falta desenhar
  os botões (`UiButton` ou `Input.ActiveTouches`) como o `Aurora.Sandbox` faz com o toque.

---

## 8. Checklist

- [ ] Entidade do tilemap com `Transform` **e** `SolidTiles` preenchido
- [ ] Jogador com `Collider` não-cinemático mais estreito que o sprite
- [ ] `MaxFallSpeed * MaxDeltaTime < TileHeight`
- [ ] Triggers (moeda, espinho, checkpoint) com `IsSolid: false` **e** `IsKinematic: true`
- [ ] `_grounded` zerado no fim do `Update`, marcado no `OnCollision`
- [ ] Câmera com `ClampBounds` e fase maior que a área visível
- [ ] Fim de fase com `EventTrigger` + `ChangeScene`, e a próxima cena existindo mesmo
