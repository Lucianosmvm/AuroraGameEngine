# Aurora Engine

Game engine 2D em C# focada em jogos mobile, com editor visual (futuro), ECS próprio e exportação para Android (futuro).

## Estado atual — Fase 1 (Runtime básico)

- ✅ Janela e game loop (Silk.NET / GLFW)
- ✅ Renderização de sprites com batching automático (OpenGL 3.3)
- ✅ ECS mínimo: `World`, `Entity`, `Transform`, `SpriteRenderer`, `Behavior` (scripts)
- ✅ Câmera 2D com zoom e follow suave
- ✅ Letterbox/pillarbox opcional (`Game.DesignResolution`): trava a proporção de câmera/UI/toque numa resolução fixa, com barra centralizada em vez de esticar/cortar em telas com proporção diferente
- ✅ Input de teclado, mouse, toque e gamepad (analógicos + botões; `AxisX`/`AxisY` já combinam teclado+analógico esquerdo sem mudar script nenhum)
- ✅ Carregamento de texturas (PNG/JPG via StbImageSharp) com cache
- ✅ Assets abstraídos por `IAssetSource`: pasta no desktop, APK no Android
- ✅ Cenas em JSON (`scenes/*.json`) com registro extensível de componentes
- ✅ Tilemaps com culling e 1 draw call por mapa; pintura no editor
- ✅ Variáveis e switches globais (`GameState`) com save/load em JSON
- ✅ Eventos visuais (`EventTrigger`): gatilhos SceneStart/PlayerTouch/SwitchOn e
  ações SetVariable, SetSwitch, Teleport, Destroy, Wait, ShowMessage
- ✅ **Áudio** (OpenAL via Silk.NET): WAV/OGG, pool de SFX, canal de música, ações
  PlaySound/PlayMusic/StopMusic nos eventos visuais
- ✅ **Animação de sprites**: componente `Animator` com clipes de sprite sheet, troca de clipe em runtime

## Editor

```bash
dotnet run --project src/Aurora.Editor -- samples/Aurora.Sandbox.Core/Assets/scenes/forest.json
```

- **Hierarquia** (esquerda): seleciona entidades
- **Cena** (centro): arrastar move a entidade; botão do meio/direito = pan; scroll = zoom
- **Inspector** (direita): edita Transform, SpriteRenderer e mostra componentes de script
- **Ctrl+S** salva de volta no JSON — componentes que o editor não conhece são preservados intactos

## Rodando a demo

```bash
dotnet run --project samples/Aurora.Sandbox
```

### Android (APK)

```bash
dotnet publish samples/Aurora.Sandbox.Android -c Release -f net10.0-android
```

APK sai em `samples/Aurora.Sandbox.Android/bin/Release/net10.0-android/publish/com.auroraengine.sandbox-Signed.apk`.
Copie para o celular e instale (permitir "fontes desconhecidas"). Controle por toque: segure o dedo e o jogador segue.

Controles: **WASD/setas** movem o jogador, câmera segue, **ESC** sai.
O título da janela mostra FPS, contagem de entidades e draw calls.

`--smoke` fecha a janela sozinha após 1,5 s (usado para teste automatizado).

## Estrutura

```
src/Aurora.Runtime      Núcleo da engine (sem dependência de editor/UI desktop)
  Game.cs               Classe base: janela, loop, inicialização GL
  Graphics/             SpriteBatch, Shader, Texture2D, Camera2D, Color
  Ecs/                  World, Entity, Behavior, componentes
  Input/                InputManager (teclado/mouse)
  Assets/               AssetManager (cache de texturas)
samples/Aurora.Sandbox  Demo jogável da Fase 1
```

## Formato de cena

```json
{
  "Scene": "Forest",
  "Objects": [
    {
      "Name": "Player",
      "Components": [
        { "Type": "Transform", "X": 0, "Y": 0 },
        { "Type": "SpriteRenderer", "Texture": "sprites/player.png", "Layer": 10 },
        { "Type": "PlayerController" }
      ]
    }
  ]
}
```

Componentes próprios entram no mesmo registro dos nativos:

```csharp
Scenes.Register<BobBehavior>("Bob",
    (json, ctx) => new BobBehavior(SceneSerializer.GetFloat(json, "Amplitude", 4f)),
    (json, c, ctx) => json.WriteNumber("Amplitude", ((BobBehavior)c).Amplitude));
LoadScene("scenes/forest.json");
```

## Exemplo de uso

```csharp
public class MyGame : Game
{
    protected override void OnLoad()
    {
        var player = World.CreateEntity("Player");
        player.Add(new Transform(0, 0));
        player.Add(new SpriteRenderer(Assets.LoadTexture("player.png")));
        player.Add(new PlayerController(Input)); // Behavior customizado
    }
}

new MyGame().Run("Meu Jogo", 1280, 720);
```

## Animação de sprites

Coloque o sprite sheet como qualquer textura e adicione um `Animator`:

```csharp
var hero = World.CreateEntity("Hero");
hero.Add(new Transform(0, 0));
hero.Add(new SpriteRenderer(Assets.LoadTexture("sprites/hero.png")));
var anim = hero.Add(new Animator
{
    FrameWidth = 32, FrameHeight = 48, SheetColumns = 4,
    Clips =
    [
        new AnimationClip { Name = "idle", Frames = [0, 1, 2, 3], FrameDuration = 0.2f },
        new AnimationClip { Name = "walk", Frames = [4, 5, 6, 7], FrameDuration = 0.1f },
        new AnimationClip { Name = "attack", Frames = [8, 9, 10], FrameDuration = 0.08f, Loop = false },
    ],
});
```

Dentro de um `Behavior`:

```csharp
var anim = Get<Animator>()!;
if (moving) anim.Play("walk");
else        anim.Play("idle");

if (attacking && anim.IsFinished) anim.Play("idle");
```

O frame ativo é calculado do índice na grade: `col = frame % SheetColumns`, `row = frame / SheetColumns`.
O `Animator` atualiza o `SourceRect` do `SpriteRenderer` automaticamente a cada frame.

### Cena JSON

```json
{
  "Type": "Animator",
  "FrameWidth": 32, "FrameHeight": 48, "SheetColumns": 4,
  "Clips": [
    { "Name": "idle", "Frames": [0,1,2,3], "Duration": 0.2 },
    { "Name": "walk", "Frames": [4,5,6,7], "Duration": 0.1 }
  ]
}
```

## Áudio

Coloque os arquivos em `Assets/sounds/`. Formatos suportados: **WAV** (PCM 8/16 bits) e **OGG Vorbis**.

```csharp
// Em OnLoad():
Audio.Preload("sounds/bgm.ogg");          // opcional: pré-carrega sem tocar

// Em OnUpdate() ou Behavior:
Audio.Play("sounds/coin.wav");            // SFX (one-shot, pool de 16 fontes)
Audio.Play("sounds/hit.wav", volume: 0.6f, pitch: 1.2f);
Audio.PlayMusic("sounds/bgm.ogg");        // canal de música com loop
Audio.StopMusic();
Audio.MasterVolume = 0.8f;               // volume global (0..1)
```

### Nos eventos visuais (JSON de cena)

```json
{ "Action": "PlaySound", "Name": "sounds/coin.wav", "Value": 1.0 }
{ "Action": "PlayMusic", "Name": "sounds/bgm.ogg", "On": true, "Value": 0.7 }
{ "Action": "StopMusic" }
```

`Value` = volume (0..1, padrão 1.0). `On` no PlayMusic = loop (padrão true).
Se não houver dispositivo de áudio, `Audio.IsAvailable` é false e todas as chamadas são no-op.

## Roadmap

1. **Fase 1 — Runtime básico** ✅
2. **Fase 1.5 — Prova de conceito Android** (sprite rodando em APK) — próximo
3. **Fase 2 — Editor** (Avalonia): hierarquia, inspector, scene view, asset browser ✅ (parcial)
4. **Fase 3 — Ferramentas RPG**: tiles ✅, eventos visuais ✅, diálogos ✅, **áudio** ✅, inventário, quests, save
5. **Fase 4 — Avançado**: animação de sprites, partículas, luzes 2D, física 2D, A*
6. **Fase 5 — Exportação**: Android (APK/AAB), Windows, Linux, Web, plugins

## Requisitos

- .NET 10 SDK
- GPU com OpenGL 3.3+
