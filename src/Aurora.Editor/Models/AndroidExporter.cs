using System.Text.RegularExpressions;

namespace Aurora.Editor.Models;

/// <summary>
/// Gera um segundo projeto (.csproj net10.0-android + MainActivity + AndroidAssetSource)
/// que compila o mesmo código-fonte do jogo desktop pra empacotar num APK sideload.
/// Não referencia o .csproj do jogo direto (NETSDK1150: Exe não-self-contained não pode
/// ser referenciado por Exe self-contained) — compila os mesmos .cs via glob, excluindo
/// o Program.cs (entry point desktop; Android usa MainActivity).
/// </summary>
public static class AndroidExporter
{
    /// <summary>Resultado da exportação: caminho do .csproj Android gerado e avisos não-fatais.</summary>
    public sealed record Result(string CsprojPath, IReadOnlyList<string> Warnings);

    private static readonly HashSet<string> ValidOrientations =
        ["Landscape", "Portrait", "SensorLandscape", "SensorPortrait", "Sensor"];

    public static Result Export(string gameCsprojPath, string androidProjectDir, string applicationId, string displayName,
        string orientation = "Landscape")
    {
        if (!ValidOrientations.Contains(orientation))
            orientation = "Landscape";

        string gameDir = Path.GetDirectoryName(Path.GetFullPath(gameCsprojPath))
            ?? throw new InvalidOperationException("Caminho de projeto inválido.");

        var (gameNamespace, gameClassName, programFile) = FindGameClass(gameDir)
            ?? throw new InvalidOperationException(
                "Não encontrei uma classe que herda de Game (Aurora.Runtime.Game) nesse projeto.");

        string runtimeCsproj = FindRuntimeCsproj(gameDir)
            ?? throw new InvalidOperationException(
                "Não encontrei Aurora.Runtime.csproj a partir da pasta do jogo (rode o editor a partir do repositório).");

        Directory.CreateDirectory(androidProjectDir);

        string relativeRuntimePath = Path.GetRelativePath(androidProjectDir, runtimeCsproj);
        string relativeGameDir = Path.GetRelativePath(androidProjectDir, gameDir);
        string relativeProgramFile = programFile is not null
            ? Path.GetRelativePath(androidProjectDir, programFile)
            : Path.Combine(relativeGameDir, "Program.cs");

        string androidProjectName = Path.GetFileName(androidProjectDir);
        string droidNamespace = $"{gameNamespace}.Droid";

        File.WriteAllText(Path.Combine(androidProjectDir, $"{androidProjectName}.csproj"),
            BuildCsproj(relativeRuntimePath, relativeGameDir, relativeProgramFile, applicationId, droidNamespace));
        File.WriteAllText(Path.Combine(androidProjectDir, "MainActivity.cs"),
            BuildMainActivity(droidNamespace, gameNamespace, gameClassName, displayName, orientation));
        File.WriteAllText(Path.Combine(androidProjectDir, "AndroidAssetSource.cs"),
            BuildAssetSource(droidNamespace));

        var warnings = new List<string>();
        if (HasUnqualifiedGameState(gameDir))
            warnings.Add(
                "Achei 'GameState' sem qualificar em algum script - Android tem Android.App.GameState " +
                "que colide (CS0104). Se o build falhar por isso, troca pra Aurora.Runtime.GameState nesse arquivo.");
        if (!Directory.Exists(Path.Combine(gameDir, "Assets", "fonts")))
            warnings.Add(
                "Não achei Assets/fonts/ - se o jogo usa Dialogue.Draw ou texto na tela, " +
                "precisa de um .ttf ali (entra no AndroidAsset automaticamente).");

        return new Result(Path.Combine(androidProjectDir, $"{androidProjectName}.csproj"), warnings);
    }

    /// <summary>Acha a classe que herda de Game e o arquivo do entry point (Program.cs
    /// gerado pelo scaffolder: sem namespace, com "new XGame()" e ".Run(").</summary>
    private static (string Namespace, string ClassName, string? ProgramFile)? FindGameClass(string gameDir)
    {
        string? ns = null, className = null, programFile = null;

        foreach (string file in Directory.EnumerateFiles(gameDir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            string text = File.ReadAllText(file);

            if (className is null)
            {
                var classMatch = Regex.Match(text, @"class\s+(\w+)\s*:\s*(?:Aurora\.Runtime\.)?Game\b");
                if (classMatch.Success)
                {
                    className = classMatch.Groups[1].Value;
                    var nsMatch = Regex.Match(text, @"namespace\s+([\w.]+)\s*[;{]");
                    if (nsMatch.Success)
                        ns = nsMatch.Groups[1].Value;
                }
            }

            if (programFile is null && !Regex.IsMatch(text, @"namespace\s+[\w.]+")
                && text.Contains(".Run(") && text.Contains("new "))
            {
                programFile = file;
            }
        }

        return className is null || ns is null ? null : (ns, className, programFile);
    }

    /// <summary>Sobe a partir da pasta do jogo até achar src/Aurora.Runtime/Aurora.Runtime.csproj
    /// (mesma lógica do GameProjectScaffolder, agora a partir do projeto em vez do editor).</summary>
    private static string? FindRuntimeCsproj(string startDir)
    {
        for (var dir = new DirectoryInfo(startDir); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "src", "Aurora.Runtime", "Aurora.Runtime.csproj");
            if (File.Exists(candidate))
                return candidate;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "src", "Aurora.Runtime", "Aurora.Runtime.csproj");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static bool HasUnqualifiedGameState(string gameDir)
    {
        foreach (string file in Directory.EnumerateFiles(gameDir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            string text = File.ReadAllText(file);
            if (Regex.IsMatch(text, @"(?<!Aurora\.Runtime\.)\bGameState\b"))
                return true;
        }
        return false;
    }

    private static string BuildCsproj(string relativeRuntimePath, string relativeGameDir,
        string relativeProgramFile, string applicationId, string droidNamespace) => $"""
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <TargetFramework>net10.0-android</TargetFramework>
            <SupportedOSPlatformVersion>24</SupportedOSPlatformVersion>
            <OutputType>Exe</OutputType>
            <ApplicationId>{applicationId}</ApplicationId>
            <ApplicationVersion>1</ApplicationVersion>
            <ApplicationDisplayVersion>0.1.0</ApplicationDisplayVersion>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <RootNamespace>{droidNamespace}</RootNamespace>
            <!-- APK (sideload) em vez de AAB (Play Store) -->
            <AndroidPackageFormats>apk</AndroidPackageFormats>
          </PropertyGroup>

          <ItemGroup>
            <!-- SilkActivity vive no asset net7.0-android do Windowing.Sdl; Input.Sdl é o
                 backend de input no Android (o meta-pacote Silk.NET.Input só traz GLFW). -->
            <PackageReference Include="Silk.NET.Windowing.Sdl" Version="2.23.0" />
            <PackageReference Include="Silk.NET.Input.Sdl" Version="2.23.0" />
            <ProjectReference Include="{relativeRuntimePath}" />
          </ItemGroup>

          <ItemGroup>
            <!-- Não referencia o .csproj do jogo direto (NETSDK1150) - compila os mesmos
                 .cs aqui, exceto o Program.cs (entry point desktop). -->
            <Compile Include="{relativeGameDir}\*.cs" Exclude="{relativeProgramFile}"
                     Link="GameSource\%(Filename)%(Extension)" />
            <AndroidAsset Include="{relativeGameDir}\Assets\**"
                          Link="Assets\%(RecursiveDir)%(Filename)%(Extension)" />
          </ItemGroup>

        </Project>
        """;

    private static string BuildMainActivity(string droidNamespace, string gameNamespace, string gameClassName,
        string displayName, string orientation) => $$"""
        using System.Numerics;
        using Android.App;
        using Android.Content.PM;
        using Android.Views;
        using Silk.NET.Windowing;
        using Silk.NET.Windowing.Sdl.Android;

        namespace {{droidNamespace}};

        // Orientação escolhida no export (Inspector → Orientação Android). Landscape/Portrait
        // são fixos (nunca giram). SensorLandscape/SensorPortrait/Sensor giram com o aparelho —
        // um bug antigo do Silk.NET/SDL no Android ("You cannot call Reset inside of the render
        // loop!", Silk.NET.Windowing.Internals.ViewImplementationBase.Reset/Dispose) já causou
        // crash real ao girar; testado de novo manualmente em device Android 14 real (rotação
        // completa incluindo retrato) sem reproduzir o crash, mas isso pode variar por
        // aparelho/versão de Android/driver — se crashar no seu device, volte pra Landscape fixo.
        [Activity(Label = "{{displayName}}", MainLauncher = true,
            ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
            ScreenOrientation = ScreenOrientation.{{orientation}})]
        public class MainActivity : SilkActivity
        {
            private volatile {{gameNamespace}}.{{gameClassName}}? _game;

            protected override void OnRun()
            {
                var options = ViewOptions.Default with
                {
                    API = new GraphicsAPI(ContextAPI.OpenGLES, ContextProfile.Compatability,
                        ContextFlags.Default, new APIVersion(3, 0)),
                };

                using var view = Silk.NET.Windowing.Window.GetView(options);
                using var game = new {{gameNamespace}}.{{gameClassName}}();
                _game = game;
                game.AssetSource = new AndroidAssetSource();
                game.Run(view);
                _game = null;
            }

            // Toque não vira evento de mouse sozinho nesse binding Silk.NET/SDL Android
            // (testado em device real). OnTouchEvent não funciona - SilkActivity estende
            // SDLActivity (Java), cuja SurfaceView já consome o toque antes. DispatchTouchEvent
            // roda ANTES de qualquer view filha, sempre - intercepta aqui e injeta manual.
            public override bool DispatchTouchEvent(MotionEvent? e)
            {
                if (e is not null && _game is not null)
                {
                    // Caminho antigo (1 ponto só) - é o que UIManager usa pro clique de
                    // UiButton (menu/HUD só olha um toque, não precisa de mais que isso).
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

                    // Multi-toque de verdade (joystick + botão ao mesmo tempo) - cada dedo
                    // com seu id (MotionEvent.GetPointerId), independente do caminho acima.
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
        }
        """;

    private static string BuildAssetSource(string droidNamespace) => $$"""
        using Android.App;
        using Aurora.Runtime.Assets;

        namespace {{droidNamespace}};

        /// <summary>Lê assets empacotados no APK (itens AndroidAsset do csproj).</summary>
        public sealed class AndroidAssetSource : IAssetSource
        {
            private readonly Android.Content.Res.AssetManager _assets =
                Application.Context.Assets ?? throw new InvalidOperationException("AssetManager Android indisponível.");

            public Stream Open(string path) => _assets.Open(Normalize(path));

            public bool Exists(string path)
            {
                try
                {
                    using var stream = _assets.Open(Normalize(path));
                    return true;
                }
                catch (Java.IO.IOException)
                {
                    return false;
                }
            }

            private static string Normalize(string path) => path.Replace('\\', '/');
        }
        """;
}
