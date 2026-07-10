# Aurora Engine

Game engine 2D em C# focada em jogos mobile, com editor visual (futuro), ECS próprio e exportação para Android (futuro).

## Estado atual — Fase 1 (Runtime básico)

- ✅ Janela e game loop (Silk.NET / GLFW)
- ✅ Renderização de sprites com batching automático (OpenGL 3.3)
- ✅ ECS mínimo: `World`, `Entity`, `Transform`, `SpriteRenderer`, `Behavior` (scripts)
- ✅ Câmera 2D com zoom e follow suave
- ✅ Input de teclado, mouse e toque
- ✅ Carregamento de texturas (PNG/JPG via StbImageSharp) com cache
- ✅ Assets abstraídos por `IAssetSource`: pasta no desktop, APK no Android
- ✅ Cenas em JSON (`scenes/*.json`) com registro extensível de componentes

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

## Roadmap

1. **Fase 1 — Runtime básico** ✅
2. **Fase 1.5 — Prova de conceito Android** (sprite rodando em APK) — próximo
3. **Fase 2 — Editor** (Avalonia): hierarquia, inspector, scene view, asset browser
4. **Fase 3 — Ferramentas RPG**: tiles, eventos visuais, diálogos, inventário, quests, save
5. **Fase 4 — Avançado**: state machine de animação, partículas, luzes 2D, behavior trees, A*
6. **Fase 5 — Exportação**: Android (APK/AAB), Windows, Linux, Web, plugins

## Requisitos

- .NET 10 SDK
- GPU com OpenGL 3.3+
