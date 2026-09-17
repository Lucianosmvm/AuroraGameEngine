using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;
using Aurora.Runtime.UI;
using Silk.NET.Input;

namespace CaminhosDaFe;

/// <summary>
/// A arma do Davi: segura pra girar a funda (a carga sobe), solta pra arremessar. Quanto mais
/// carga, mais longe, mais rápido e mais dano — o arremesso fraco serve pra afastar, o cheio pra
/// derrubar.
///
/// <para>Mira: com o mouse, a pedra vai na direção do cursor; com J (ou gatilho direito do
/// controle), vai pra onde o Davi está virado — ou pro analógico direito, se inclinado. No
/// celular é o joystick da funda: arrastar a partir dele mira e carrega, soltar arremessa.</para>
///
/// <para>O desenho da mira (pontilhado + anel de carga) é feito no <c>CaminhosGame.OnRender</c>,
/// que lê <see cref="Mirando"/>, <see cref="Carga"/> e <see cref="Direcao"/>: script não tem
/// passe de desenho próprio.</para>
/// </summary>
[SceneScript]
public sealed class Funda : Behavior
{
    public string Prefab = "prefabs/pedra.json";

    /// <summary>Segundos segurando até a carga encher.</summary>
    public float TempoCarga = 0.8f;

    public float VelocidadeMin = 140f;
    public float VelocidadeMax = 300f;
    public float DanoMin = 6f;
    public float DanoMax = 20f;

    /// <summary>Segundos que a pedra voa — junto com a velocidade, define o alcance.</summary>
    public float DuracaoPedra = 0.7f;

    /// <summary>Tempo mínimo entre dois arremessos.</summary>
    public float Recarga = 0.3f;

    /// <summary>Multiplicador da velocidade de andar enquanto gira a funda.</summary>
    public float LentidaoMirando = 0.45f;

    /// <summary>Tela e nome do UiJoystick da funda (modo toque). Com essa tela visível o mouse
    /// é ignorado: no celular todo toque também aparece como "mouse apertado", e o dedo no
    /// joystick de andar dispararia a funda.</summary>
    public string JoystickTela = "Toque";
    public string JoystickNome = "Funda";

    /// <summary>Arrasto mínimo (0 a 1 do raio) pra contar como mirando.</summary>
    public float ZonaMorta = 0.25f;

    public bool Mirando { get; private set; }

    /// <summary>0 a 1.</summary>
    public float Carga { get; private set; }

    public Vector2 Direcao { get; private set; } = new(1f, 0f);

    /// <summary>Segundos desde o último arremesso — o Davi segura a pose de arremesso por um
    /// instante.</summary>
    public float DesdeArremesso { get; private set; } = 99f;

    /// <summary>De onde a pedra sai: a mão, um pouco acima do centro do sprite.</summary>
    public Vector2 Origem => (Get<Transform>()?.Position ?? Vector2.Zero) + new Vector2(0f, -2f);

    /// <summary>Até onde a pedra iria se fosse solta agora, em pixels de mundo.</summary>
    public float Alcance => VelocidadeAtual * DuracaoPedra;

    private float VelocidadeAtual => VelocidadeMin + (VelocidadeMax - VelocidadeMin) * Carga;

    private float _recarga;

    public override void Update(float deltaTime)
    {
        DesdeArremesso += deltaTime;
        _recarga -= deltaTime;

        var input = World?.Input;
        if (input is null || World!.Dialogue?.IsActive == true || Get<Health>() is { IsDead: true })
        {
            Mirando = false;
            Carga = 0f;
            return;
        }

        var toque = LerJoystick(out bool modoToque);
        bool arrastando = toque.Length() > ZonaMorta;
        bool mouse = !modoToque && input.IsMouseDown(MouseButton.Left);
        bool teclado = input.IsKeyDown(Key.J) || input.RightTrigger > 0.5f;
        bool segurando = arrastando || mouse || teclado;

        // Só mira enquanto segura: no frame de soltar o mouse já está "solto", e recalcular ali
        // trocaria o cursor pela direção do corpo — a pedra sairia pro lado errado.
        if (arrastando)
            Direcao = Vector2.Normalize(toque);
        else if (segurando)
            AtualizarDirecao(input, mouse);

        if (segurando && (Mirando || _recarga <= 0f))
        {
            Mirando = true;
            Carga = MathF.Min(1f, Carga + deltaTime / MathF.Max(0.05f, TempoCarga));
        }
        else if (Mirando)
        {
            Arremessar();
            Mirando = false;
            Carga = 0f;
        }
    }

    private Vector2 LerJoystick(out bool modoToque)
    {
        modoToque = JoystickNome.Length > 0 && World!.UI?.IsVisible(JoystickTela) == true;
        return modoToque ? World!.UI!.Find<UiJoystick>(JoystickTela, JoystickNome)?.Value ?? Vector2.Zero : Vector2.Zero;
    }

    private void AtualizarDirecao(Aurora.Runtime.Input.InputManager input, bool mouse)
    {
        Vector2 alvo;
        if (mouse && World!.Camera is { } camera)
            alvo = camera.ScreenToWorld(input.MousePosition) - Origem;
        else if (input.RightStick.LengthSquared() > 0.2f)
            alvo = input.RightStick;
        else if (Get<TopDownController>() is { } controle)
            alvo = controle.Facing;
        else
            return;

        if (alvo.LengthSquared() > 1f || (!mouse && alvo.LengthSquared() > 0.01f))
            Direcao = Vector2.Normalize(alvo);
    }

    private void Arremessar()
    {
        if (World is null)
            return;

        _recarga = Recarga;
        DesdeArremesso = 0f;

        if (World.Spawn(Prefab, Origem + Direcao * 6f) is not { } pedra || pedra.Get<Pedra>() is not { } script)
            return;

        script.Velocidade = Direcao * VelocidadeAtual;
        script.Duracao = DuracaoPedra;
        script.Dano = DanoMin + (DanoMax - DanoMin) * Carga;
        script.Fonte = Entity;
    }
}
