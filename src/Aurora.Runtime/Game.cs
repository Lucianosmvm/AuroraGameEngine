using Aurora.Runtime.Assets;
using Aurora.Runtime.Audio;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Events;
using Aurora.Runtime.Input;
using Aurora.Runtime.Saves;
using Aurora.Runtime.Scenes;
using Aurora.Runtime.UI;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Aurora.Runtime;

/// <summary>
/// Classe base de um jogo Aurora: roda o loop update/render sobre um <see cref="IView"/> —
/// janela no desktop, view SDL no Android. Herde e implemente <see cref="OnLoad"/>.
/// </summary>
public abstract class Game : IDisposable
{
    /// <summary>Superfície onde o jogo roda. No desktop também é um <see cref="IWindow"/>.</summary>
    public IView View { get; private set; } = null!;

    /// <summary>Janela desktop, ou null quando rodando em view mobile.</summary>
    public IWindow? Window => View as IWindow;

    public GL Gl { get; private set; } = null!;
    public InputManager Input { get; private set; } = null!;
    public SpriteBatch SpriteBatch { get; private set; } = null!;
    public AssetManager Assets { get; private set; } = null!;
    public AudioManager Audio { get; private set; } = null!;
    public SceneManager SceneManager { get; private set; } = null!;
    public SaveManager Save { get; private set; } = null!;

    /// <summary>
    /// Nome do jogo — define a pasta de save em %LocalAppData%/[GameName]/saves/.
    /// Defina antes de <see cref="Run"/> se quiser um nome personalizado.
    /// </summary>
    public string GameName { get; set; } = "AuroraGame";

    /// <summary>Tamanho do framebuffer em cache — atualizado só no load e no evento de resize
    /// (View.FramebufferResize), NUNCA lido direto no meio do loop de update/render. No Android,
    /// ler View.FramebufferSize repetidamente por frame (visto: 1x virou 3x quando UI.Update/Draw
    /// passaram a precisar do tamanho de tela pro sistema de Anchor) parece interagir mal com o
    /// resize handling interno do Silk.NET/SDL — crash real em device: "You cannot call `Reset`
    /// inside of the render loop!" logo na abertura. Ler o cache em vez da propriedade evita isso.</summary>
    public Vector2D<int> ScreenSize { get; private set; }

    public Camera2D Camera { get; } = new();
    public World World { get; } = new();
    public SceneSerializer Scenes { get; } = new();
    public GameState State { get; } = new();
    public InventoryManager Inventory { get; } = new();
    public QuestManager Quests { get; } = new();
    public DialogueSystem Dialogue { get; } = new();
    public UIManager UI { get; } = new();
    public EventSystem Events { get; }

    protected Game()
    {
        Events = new EventSystem(World, State)
        {
            Dialogue = Dialogue, Inventory = Inventory, Quests = Quests, UI = UI,
        };
    }

    public Color ClearColor { get; set; } = Color.CornflowerBlue;

    /// <summary>
    /// Cena passada pelo editor via --scene. Use em <see cref="OnLoad"/>:
    /// <c>LoadScene(BootScene ?? "scenes/inicio.json");</c>
    /// </summary>
    protected string? BootScene { get; private set; }

    private string? _describeScriptsOutputPath;

    /// <summary>
    /// Processa argumentos de linha de comando. Chame antes de <see cref="Run"/>.
    /// <para>Argumentos reconhecidos: <c>--scene &lt;caminho&gt;</c>,
    /// <c>--describe-scripts &lt;arquivo&gt;</c> (usado pelo editor pra descobrir
    /// scripts [SceneScript] sem abrir janela).</para>
    /// </summary>
    public void ParseArgs(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--scene")
                BootScene = args[i + 1];
            else if (args[i] == "--describe-scripts")
                _describeScriptsOutputPath = args[i + 1];
        }
    }

    /// <summary>Origem dos assets. Defina antes de Run (Android: AndroidAssetSource). Null = pasta "Assets".</summary>
    public IAssetSource? AssetSource { get; set; }

    /// <summary>Desktop: cria uma janela e bloqueia até o jogo fechar.</summary>
    public void Run(string title = "Aurora Game", int width = 1280, int height = 720, bool vsync = true)
    {
        if (_describeScriptsOutputPath is { } outputPath)
        {
            DescribeScriptsAndWrite(outputPath);
            return;
        }

        var options = WindowOptions.Default with
        {
            Title = title,
            Size = new Vector2D<int>(width, height),
            VSync = vsync,
        };

        var window = Silk.NET.Windowing.Window.Create(options);
        Run(window);
        window.Dispose();
    }

    /// <summary>
    /// Varre o assembly do jogo por [SceneScript] e escreve nome+campos em JSON no arquivo
    /// indicado, sem criar janela. Não passa por HandleLoad — não precisa de GL/janela pra
    /// só ler reflection.
    /// </summary>
    private void DescribeScriptsAndWrite(string outputPath)
    {
        var scripts = Scenes.DescribeScripts(GetType().Assembly);
        File.WriteAllText(outputPath, System.Text.Json.JsonSerializer.Serialize(scripts));
    }

    /// <summary>Roda sobre uma view já criada (Android: obtida via Window.GetView na Activity).</summary>
    public void Run(IView view)
    {
        View = view;
        View.Load += HandleLoad;
        View.Update += HandleUpdate;
        View.Render += HandleRender;
        View.FramebufferResize += HandleResize;
        View.Closing += HandleClosing;

        View.Run();
    }

    /// <summary>Fecha a view e encerra o loop.</summary>
    public void Exit() => View.Close();

    /// <summary>
    /// Carrega uma cena, limpando o mundo atual. Para transição com fade use
    /// <see cref="SceneManager.LoadWithFade"/>.
    /// </summary>
    public void LoadScene(string scenePath) => SceneManager.Load(scenePath);

    /// <summary>Fade para preto, troca de cena e fade de volta.</summary>
    public void LoadSceneWithFade(string scenePath, float duration = 0.3f)
        => SceneManager.LoadWithFade(scenePath, duration);

    private void HandleLoad()
    {
        Gl = GL.GetApi(View);
        Input = new InputManager(View.CreateInput());

        bool isGles = View.API.API == ContextAPI.OpenGLES;
        SpriteBatch = new SpriteBatch(Gl, isGles);

        var source = AssetSource ?? new FileAssetSource();
        Assets = new AssetManager(Gl, source);
        Audio = new AudioManager(source);
        Events.Audio = Audio;
        Events.Input = Input;

        SceneManager = new SceneManager(World, Scenes, Events, Dialogue, Assets);
        Events.SceneChangeRequested += path => SceneManager.LoadWithFade(path);
        Events.QuitRequested += Exit;

        Save = new SaveManager(State, SceneManager, GameName, Inventory, Quests);
        Events.Save = Save;

        Gl.Enable(EnableCap.Blend);
        Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        ScreenSize = View.FramebufferSize;
        Camera.SetViewport(ScreenSize.X, ScreenSize.Y);

        AutoRegisterScripts();
        OnLoad();
    }

    /// <summary>
    /// Varre o assembly do jogo em busca de classes marcadas com <c>[SceneScript]</c> e
    /// registra cada uma automaticamente no serializador de cena — sem precisar chamar
    /// <c>Scenes.Register</c> na mão nem escrever leitura/escrita de JSON campo a campo.
    /// GetType().Assembly (não Assembly.GetEntryAssembly()) — no Android a Activity não tem
    /// entry point tradicional e GetEntryAssembly() pode voltar null, silenciando TODOS os
    /// scripts custom sem erro nenhum (só os componentes nativos continuam funcionando).
    /// </summary>
    private void AutoRegisterScripts()
    {
        Scenes.RegisterScripts(GetType().Assembly);
    }

    private void HandleUpdate(double deltaTime)
    {
        float dt = (float)deltaTime;
        Input.BeginFrame();
        SceneManager.Update(dt);
        Dialogue.Update();
        OnUpdate(dt);
        World.Update(dt);
        Events.Update(dt);
        UI.Update(Input, Events, ScreenSize.X, ScreenSize.Y);
        UpdateCamera(dt);
    }

    private void UpdateCamera(float dt)
    {
        foreach (var (_, transform, ctrl) in World.Query<Transform, CameraController>())
        {
            var target = ctrl.Follow is not null
                && World.TryFind(ctrl.Follow, out var followEntity)
                && followEntity.Get<Transform>() is { } ft
                    ? ft.Position
                    : transform.Position;

            target += ctrl.Offset;

            if (ctrl.FollowSpeed > 0f)
                Camera.Follow(target, ctrl.FollowSpeed, dt);
            else
                Camera.Position = target;

            Camera.Zoom = ctrl.Zoom;

            if (ctrl.ClampBounds)
            {
                float hw = Camera.ViewportWidth  / (2f * MathF.Max(Camera.Zoom, 0.001f));
                float hh = Camera.ViewportHeight / (2f * MathF.Max(Camera.Zoom, 0.001f));
                Camera.Position = new System.Numerics.Vector2(
                    Math.Clamp(Camera.Position.X, ctrl.BoundsX + hw, ctrl.BoundsX + ctrl.BoundsWidth  - hw),
                    Math.Clamp(Camera.Position.Y, ctrl.BoundsY + hh, ctrl.BoundsY + ctrl.BoundsHeight - hh));
            }

            break; // apenas a primeira câmera ativa conta
        }
    }

    private void HandleRender(double deltaTime)
    {
        Gl.ClearColor(ClearColor.R, ClearColor.G, ClearColor.B, ClearColor.A);
        Gl.Clear(ClearBufferMask.ColorBufferBit);

        SpriteBatch.Begin(Camera.GetViewProjection());
        World.Render(SpriteBatch, Camera);
        OnRender((float)deltaTime);
        SpriteBatch.End();

        // Passe de UI em coordenadas de tela (HUD, diálogos) — não segue a câmera.
        SpriteBatch.Begin(GetScreenProjection());
        World.DrawGlobalTint(SpriteBatch, ScreenSize.X, ScreenSize.Y);
        OnRenderUI((float)deltaTime);
        SceneManager.DrawOverlay(SpriteBatch, ScreenSize.X, ScreenSize.Y);
        SpriteBatch.End();
    }

    /// <summary>Projeção em pixels de tela: (0,0) no canto superior esquerdo.</summary>
    public System.Numerics.Matrix4x4 GetScreenProjection()
        => System.Numerics.Matrix4x4.CreateOrthographicOffCenter(
            0f, ScreenSize.X, ScreenSize.Y, 0f, -1f, 1f);

    private void HandleResize(Vector2D<int> size)
    {
        ScreenSize = size;
        Gl.Viewport(size);
        Camera.SetViewport(size.X, size.Y);
    }

    // Recursos GL precisam ser liberados com o contexto ainda vivo, por isso no Closing
    // da view e não em Dispose (que roda depois de View.Run retornar).
    private void HandleClosing()
    {
        OnUnload();
        Audio?.Dispose();
        Assets?.Dispose();
        SpriteBatch?.Dispose();
    }

    /// <summary>Chamado uma vez com o contexto gráfico pronto. Crie entidades e carregue assets aqui.</summary>
    protected abstract void OnLoad();

    /// <summary>Chamado a cada frame antes dos behaviors do mundo.</summary>
    protected virtual void OnUpdate(float deltaTime)
    {
    }

    /// <summary>Chamado a cada frame com o SpriteBatch já aberto, após os sprites do mundo.</summary>
    protected virtual void OnRender(float deltaTime)
    {
    }

    /// <summary>Chamado a cada frame no passe de UI (coordenadas de tela). HUD e diálogos aqui.</summary>
    protected virtual void OnRenderUI(float deltaTime)
    {
    }

    /// <summary>Chamado ao fechar, antes da engine liberar os recursos gráficos.</summary>
    protected virtual void OnUnload()
    {
    }

    public virtual void Dispose() => GC.SuppressFinalize(this);
}
