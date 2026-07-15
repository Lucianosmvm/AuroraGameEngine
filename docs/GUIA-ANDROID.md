# Build Android (APK) pro jogo

O Editor hoje só faz scaffold de projeto desktop (`New Project`). Pra empacotar
esse mesmo jogo num APK sideload, cria um segundo `.csproj` ao lado que reusa
o mesmo código-fonte e assets. Exemplo real funcionando: `samples/Aurora.Sandbox.Android`.

## Pré-requisito

```
dotnet workload install android
```

(Neste ambiente já vem instalado via Visual Studio - `dotnet workload list` confirma.)

## 1. Csproj do Android

Não referencia o `.csproj` do jogo desktop direto — ele é `OutputType Exe`
não self-contained, e um app Android (`OutputType Exe`, self-contained por
natureza) não pode referenciar outro Exe não-self-contained (erro `NETSDK1150`).
Solução: compila os mesmos `.cs` do jogo direto no projeto Android, excluindo
o `Program.cs` do jogo (que tem o entry point desktop — o Android usa
`MainActivity` como entry point, não `Main`).

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-android</TargetFramework>
    <SupportedOSPlatformVersion>24</SupportedOSPlatformVersion>
    <OutputType>Exe</OutputType>
    <ApplicationId>com.suaempresa.seujogo</ApplicationId>
    <ApplicationVersion>1</ApplicationVersion>
    <ApplicationDisplayVersion>0.1.0</ApplicationDisplayVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>SeuJogo.Droid</RootNamespace>
    <!-- APK (sideload) em vez de AAB (Play Store) -->
    <AndroidPackageFormats>apk</AndroidPackageFormats>
  </PropertyGroup>

  <ItemGroup>
    <!-- SilkActivity vive no asset net7.0-android do Windowing.Sdl; Input.Sdl é o
         backend de input no Android (o meta-pacote Silk.NET.Input só traz GLFW). -->
    <PackageReference Include="Silk.NET.Windowing.Sdl" Version="2.23.0" />
    <PackageReference Include="Silk.NET.Input.Sdl" Version="2.23.0" />
    <ProjectReference Include="..\caminho\para\Aurora.Runtime\Aurora.Runtime.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Compile Include="..\SeuJogo\*.cs" Exclude="..\SeuJogo\Program.cs"
             Link="GameSource\%(Filename)%(Extension)" />
    <AndroidAsset Include="..\SeuJogo\Assets\**"
                  Link="Assets\%(RecursiveDir)%(Filename)%(Extension)" />
  </ItemGroup>
</Project>
```

## 2. MainActivity.cs

```csharp
using Android.App;
using Android.Content.PM;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl.Android;

namespace SeuJogo.Droid;

[Activity(Label = "Seu Jogo", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
    ScreenOrientation = ScreenOrientation.Landscape)] // Portrait pra vertical, Sensor* pra girar — ver pegadinha abaixo
public class MainActivity : SilkActivity
{
    protected override void OnRun()
    {
        var options = ViewOptions.Default with
        {
            API = new GraphicsAPI(ContextAPI.OpenGLES, ContextProfile.Compatability,
                ContextFlags.Default, new APIVersion(3, 0)),
        };

        using var view = Silk.NET.Windowing.Window.GetView(options);
        using var game = new SeuJogoGame();
        game.AssetSource = new AndroidAssetSource();
        game.Run(view);
    }
}
```

## 3. AndroidAssetSource.cs

Assets no APK não são pasta de disco — lê via `AssetManager` do Android:

```csharp
using Android.App;
using Aurora.Runtime.Assets;

namespace SeuJogo.Droid;

public sealed class AndroidAssetSource : IAssetSource
{
    private readonly Android.Content.Res.AssetManager _assets =
        Application.Context.Assets ?? throw new InvalidOperationException("AssetManager Android indisponível.");

    public Stream Open(string path) => _assets.Open(Normalize(path));

    public bool Exists(string path)
    {
        try { using var s = _assets.Open(Normalize(path)); return true; }
        catch (Java.IO.IOException) { return false; }
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
```

## 4. Pegadinhas testadas

- **`GameState` ambíguo**: `Android.App.GameState` existe e colide com
  `Aurora.Runtime.GameState`. Se algum script seu declara `GameState State`,
  qualifica pra `Aurora.Runtime.GameState State` só nesse arquivo (só dá erro
  quando compilado pro alvo Android, desktop não sente).
- **Sem teclado, e o toque não vira mouse sozinho (testado em device real)**:
  a suposição óbvia — "toque = evento de mouse via SDL, `IsMouseDown`/
  `MousePosition` funcionam de graça" — **não funciona** nesse binding
  Silk.NET/SDL Android (mouse sintético do toque é frágil/incompleto aqui).
  O jogo roda, renderiza, HUD atualiza, mas nada anda e nenhum toque é
  detectado. Solução que funciona: captura o toque direto na `MainActivity`
  e empurra pro `InputManager` via `SetPointer(pos, down)`.

  **Não use `OnTouchEvent`** — `SilkActivity` estende o `SDLActivity` (Java)
  que já tem sua própria `SurfaceView` consumindo o toque antes;
  `Activity.OnTouchEvent` só roda pra toque que ninguém consumiu (nunca,
  nesse caso). Use `DispatchTouchEvent`, que roda ANTES de qualquer view
  filha, sempre:

  ```csharp
  // MainActivity.cs
  private volatile SeuJogoGame? _game;

  protected override void OnRun()
  {
      ...
      using var game = new SeuJogoGame();
      _game = game;
      game.AssetSource = new AndroidAssetSource();
      game.Run(view);
      _game = null;
  }

  public override bool DispatchTouchEvent(MotionEvent? e)
  {
      if (e is not null && _game is not null)
      {
          // Caminho de 1 ponto só — o que UIManager usa pro clique de UiButton
          // (menu/HUD só olha um toque, não precisa de mais que isso).
          switch (e.Action)
          {
              case MotionEventActions.Down:
              case MotionEventActions.Move:
                  _game.Input.SetPointer(new Vector2(e.GetX(), e.GetY()), true);
                  break;
              case MotionEventActions.Up:
              case MotionEventActions.Cancel:
                  _game.Input.SetPointer(null, false);
                  break;
          }

          // Multi-toque de verdade — usado por UiJoystick/UiButton em gameplay
          // (segurar joystick com um dedo e apertar botão com outro).
          switch (e.ActionMasked)
          {
              case MotionEventActions.Down:
              case MotionEventActions.PointerDown:
              {
                  int idx = e.ActionIndex;
                  _game.Input.SetTouch(e.GetPointerId(idx), new Vector2(e.GetX(idx), e.GetY(idx)), true);
                  break;
              }
              case MotionEventActions.Move:
                  for (int i = 0; i < e.PointerCount; i++)
                      _game.Input.SetTouch(e.GetPointerId(i), new Vector2(e.GetX(i), e.GetY(i)), true);
                  break;
              case MotionEventActions.Up:
              case MotionEventActions.PointerUp:
              case MotionEventActions.Cancel:
              {
                  int idx = e.ActionIndex;
                  _game.Input.SetTouch(e.GetPointerId(idx), Vector2.Zero, false);
                  break;
              }
          }
      }
      return base.DispatchTouchEvent(e);
  }
  ```

  Isso já sai pronto quando você exporta pelo editor (Arquivo → Exportar Android) —
  o gerador (`AndroidExporter.cs`) escreve esse `MainActivity.cs` automático.

  **Movimento/ataque em tela (recomendado): `UiJoystick`/`UiButton`, não drag-to-move
  manual.** A engine tem um elemento `UiJoystick` (mesma família de `UiButton`,
  autorável na tela de UI): base fixa (X/Y/Anchor/Radius), `Value` dá a direção+
  intensidade (0..1) a cada frame, e convive com `UiButton` tocado por outro dedo
  ao mesmo tempo (`UIManager.Update` dá dono por id de toque). Exemplo (`hud.json`):
  ```json
  { "Type": "UiJoystick", "X": 70, "Y": 70, "AnchorX": "Left", "AnchorY": "Bottom", "Radius": 70 }
  ```
  No jogo, leia o vetor e o clique direto — sem precisar de `Camera`/drag manual:
  ```csharp
  var stick = UI.Find<UiJoystick>("hud", "MoveStick");
  var atk = UI.Find<UiButton>("hud", "BotaoAtk");
  controller.ExternalMove = stick?.Value ?? default;
  if (atk?.Clicked == true) controller.TriggerMelee();
  ```
  `AnchorY: "Bottom"` com margem generosa (`Y` grande o bastante) é importante —
  toque muito perto da borda inferior costuma ser engolido pelo gesto de
  navegação do Android (voltar/home por swipe), mesmo em tela cheia.
- **Crash na abertura, `FATAL UNHANDLED EXCEPTION: ... You cannot call 'Reset' inside of the
  render loop!` (`Silk.NET.Windowing.Internals.ViewImplementationBase.Reset/Dispose`)**:
  bug antigo relatado no Silk.NET/SDL no Android — evento de rotação de tela bem durante
  o boot reentrando no loop de render numa hora ruim. Por causa disso o exportador gerava
  só orientação fixa (`Landscape`), sem opção de girar. **Retestado manualmente** (device
  Android 14 real, Xiaomi/MIUI) com `Sensor` completo — incluindo troca pra retrato —
  sem reproduzir o crash. O exportador agora deixa escolher orientação (Inspector →
  "Orientação Android": `Landscape`/`Portrait` fixos, ou `SensorLandscape`/`SensorPortrait`/
  `Sensor` girando com o aparelho — esse é o jeito de fazer jogo vertical). Se seu device
  crashar ao girar mesmo assim, volta pra `Landscape`/`Portrait` fixo — o bug pode variar
  por aparelho/versão de Android/driver, o reteste cobriu só um device.
- **HUD/diálogo sem fonte**: se o jogo usa `Dialogue.Draw`/texto na tela, o
  `.ttf` tem que estar em `Assets/fonts/` e entrar no `AndroidAsset` glob junto
  (reusa a mesma fonte do sample, `DejaVuSans.ttf`, ou qualquer outra).
- **Gamepad Bluetooth/USB no Android — não testado em device real.** `InputManager`
  (`IsGamepadButtonDown`, `LeftStick`, etc.) lê `IInputContext.Gamepads`, que no Android
  vem do mesmo `Silk.NET.Input.Sdl` — teoricamente já funciona sem código extra, já que
  controle físico usa a API `SDL_GameController` (caminho diferente do toque, que
  teve bug real confirmado nesse binding, ver acima). Mas "teoricamente" não é
  "testado": ninguém validou num device real ainda. Se você testar e não funcionar,
  abra uma issue com o modelo do controle e o log (`adb logcat`).
- **`obj`/`bin` sujo entre Debug e Release**: se o projeto desktop (`Aurora.Sandbox.Core`
  e similares) nunca buildou em Release antes, pode falhar com atributos de
  assembly duplicados (`CS0579`) se alguma pasta de assets acidentalmente
  contiver um projeto `.cs` solto (glob padrão do SDK é `**/*.cs` recursivo,
  não pula subpastas com projetos de teste dentro de `Assets/`). Se acontecer,
  exclui essa subpasta do `Compile` do projeto que tem os assets:
  ```xml
  <Compile Remove="Assets\**\*.cs" />
  ```

## 5. Build

```
cd SeuJogo.Android
dotnet build -c Release
```

Gera `bin/Release/net10.0-android/<ApplicationId>-Signed.apk` — já assinado
com a debug keystore, pronto pra sideload (não serve pra Play Store, só teste).

## 6. Instalar no celular

- **USB + debug**: `adb install "caminho\para\<id>-Signed.apk"` (celular com
  "Depuração USB" ligada nas Opções do Desenvolvedor).
- **Sem cabo**: copia o `.apk` pro celular (Drive, cabo de arquivo, etc.) e
  abre direto — precisa permitir "instalar de fontes desconhecidas" na primeira vez.
