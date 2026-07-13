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
    ScreenOrientation = ScreenOrientation.SensorLandscape)]
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
- **Sem teclado**: `PlayerController` precisa de um fallback de toque, senão o
  jogo roda mas ninguém anda no celular. Padrão usado (mouse = toque no Android
  via SDL, então `IsMouseDown`/`MousePosition` já funcionam sem código extra):
  ```csharp
  if (move == Vector2.Zero && Camera is not null && Input.IsMouseDown())
  {
      var target = Camera.ScreenToWorld(Input.MousePosition);
      var delta = target - transform.Position;
      if (delta.Length() > 12f) move = delta;
  }
  ```
  `Camera` (tipo `Camera2D`) precisa ser injetado no Behavior igual `Input` —
  não é um dos tipos escalares que `[SceneScript]` injeta sozinho via JSON.
- **HUD/diálogo sem fonte**: se o jogo usa `Dialogue.Draw`/texto na tela, o
  `.ttf` tem que estar em `Assets/fonts/` e entrar no `AndroidAsset` glob junto
  (reusa a mesma fonte do sample, `DejaVuSans.ttf`, ou qualquer outra).
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
