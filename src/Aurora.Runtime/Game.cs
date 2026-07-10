using Aurora.Runtime.Assets;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Input;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Aurora.Runtime;

/// <summary>
/// Classe base de um jogo Aurora: cria a janela, inicializa OpenGL e roda o loop
/// update/render. Herde e implemente <see cref="OnLoad"/>.
/// </summary>
public abstract class Game : IDisposable
{
    public IWindow Window { get; private set; } = null!;
    public GL Gl { get; private set; } = null!;
    public InputManager Input { get; private set; } = null!;
    public SpriteBatch SpriteBatch { get; private set; } = null!;
    public AssetManager Assets { get; private set; } = null!;

    public Camera2D Camera { get; } = new();
    public World World { get; } = new();

    public Color ClearColor { get; set; } = Color.CornflowerBlue;

    /// <summary>Cria a janela e bloqueia até o jogo fechar.</summary>
    public void Run(string title = "Aurora Game", int width = 1280, int height = 720, bool vsync = true)
    {
        var options = WindowOptions.Default with
        {
            Title = title,
            Size = new Vector2D<int>(width, height),
            VSync = vsync,
        };

        Window = Silk.NET.Windowing.Window.Create(options);
        Window.Load += HandleLoad;
        Window.Update += HandleUpdate;
        Window.Render += HandleRender;
        Window.FramebufferResize += HandleResize;
        Window.Closing += HandleClosing;

        Window.Run();
        Window.Dispose();
    }

    /// <summary>Fecha a janela e encerra o loop.</summary>
    public void Exit() => Window.Close();

    private void HandleLoad()
    {
        Gl = GL.GetApi(Window);
        Input = new InputManager(Window.CreateInput());
        SpriteBatch = new SpriteBatch(Gl);
        Assets = new AssetManager(Gl);

        Gl.Enable(EnableCap.Blend);
        Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        Camera.SetViewport(Window.FramebufferSize.X, Window.FramebufferSize.Y);

        OnLoad();
    }

    private void HandleUpdate(double deltaTime)
    {
        float dt = (float)deltaTime;
        OnUpdate(dt);
        World.Update(dt);
        Input.EndFrame();
    }

    private void HandleRender(double deltaTime)
    {
        Gl.ClearColor(ClearColor.R, ClearColor.G, ClearColor.B, ClearColor.A);
        Gl.Clear(ClearBufferMask.ColorBufferBit);

        SpriteBatch.Begin(Camera.GetViewProjection());
        World.Render(SpriteBatch);
        OnRender((float)deltaTime);
        SpriteBatch.End();
    }

    private void HandleResize(Vector2D<int> size)
    {
        Gl.Viewport(size);
        Camera.SetViewport(size.X, size.Y);
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

    // Recursos GL precisam ser liberados com o contexto ainda vivo, por isso no Closing
    // da janela e não em Dispose (que roda depois de Window.Run retornar).
    private void HandleClosing()
    {
        OnUnload();
        Assets?.Dispose();
        SpriteBatch?.Dispose();
    }

    /// <summary>Chamado ao fechar a janela, antes da engine liberar os recursos gráficos.</summary>
    protected virtual void OnUnload()
    {
    }

    public virtual void Dispose() => GC.SuppressFinalize(this);
}
