using Aurora.Runtime.Assets;
using Aurora.Runtime.Audio;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Events;
using Aurora.Runtime.Input;
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

    public Camera2D Camera { get; } = new();
    public World World { get; } = new();
    public SceneSerializer Scenes { get; } = new();
    public GameState State { get; } = new();
    public DialogueSystem Dialogue { get; } = new();
    public EventSystem Events { get; }

    protected Game()
    {
        Events = new EventSystem(World, State) { Dialogue = Dialogue };
    }

    public Color ClearColor { get; set; } = Color.CornflowerBlue;

    /// <summary>Origem dos assets. Defina antes de Run (Android: AndroidAssetSource). Null = pasta "Assets".</summary>
    public IAssetSource? AssetSource { get; set; }

    /// <summary>Desktop: cria uma janela e bloqueia até o jogo fechar.</summary>
    public void Run(string title = "Aurora Game", int width = 1280, int height = 720, bool vsync = true)
    {
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

        SceneManager = new SceneManager(World, Scenes, Events, Dialogue, Assets);
        Events.SceneChangeRequested += path => SceneManager.LoadWithFade(path);

        Gl.Enable(EnableCap.Blend);
        Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        Camera.SetViewport(View.FramebufferSize.X, View.FramebufferSize.Y);

        OnLoad();
    }

    private void HandleUpdate(double deltaTime)
    {
        float dt = (float)deltaTime;
        SceneManager.Update(dt);
        Dialogue.Update();
        OnUpdate(dt);
        World.Update(dt);
        Events.Update(dt);
        Input.EndFrame();
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
        OnRenderUI((float)deltaTime);
        SceneManager.DrawOverlay(SpriteBatch, View.FramebufferSize.X, View.FramebufferSize.Y);
        SpriteBatch.End();
    }

    /// <summary>Projeção em pixels de tela: (0,0) no canto superior esquerdo.</summary>
    public System.Numerics.Matrix4x4 GetScreenProjection()
        => System.Numerics.Matrix4x4.CreateOrthographicOffCenter(
            0f, View.FramebufferSize.X, View.FramebufferSize.Y, 0f, -1f, 1f);

    private void HandleResize(Vector2D<int> size)
    {
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
