using System.Numerics;
using Aurora.Runtime;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace Aurora.Platformer;

/// <summary>
/// Demo de plataforma 2D: duas fases em JSON (<c>Assets/scenes/level1.json</c> e
/// <c>level2.json</c>), colisão do jogador com o tilemap, pulo e movimentação.
///
/// <para>Controles: A/D ou setas movem, Espaço/W/seta-cima pulam (também funciona no controle:
/// analógico esquerdo + botão A), R reinicia a fase, ESC sai.</para>
///
/// <para>Toda a lógica de jogo mora nos scripts de <c>Scripts/</c> — esta classe só carrega a
/// cena inicial, desenha o HUD e trata pausa/diálogo. É de propósito: o mesmo jogo abre no
/// editor (<c>--scene</c>) sem depender de nada escrito aqui.</para>
/// </summary>
public sealed class PlatformerGame : Game
{
    private readonly bool _smokeTest;
    private Font _font = null!;
    private float _elapsed;
    private float _titleTimer;
    private int _smokeStep;
    private float _smokeTimer;
    private float _jumpStartY;

    public PlatformerGame(bool smokeTest = false)
    {
        _smokeTest = smokeTest;
        GameName = "AuroraPlatformer";
    }

    protected override void OnLoad()
    {
        ClearColor = Color.FromBytes(92, 148, 214);          // céu

        // Enquadramento fixo: a câmera e o HUD sempre enxergam 1280x720 de "design",
        // independente do tamanho real da janela (o resto vira barra preta). Sem isso, o
        // ClampBounds das cenas teria que ser recalculado por resolução.
        DesignResolution = new Vector2D<int>(1280, 720);

        // Teto de dt menor que o padrão (0,05): a colisão testa sobreposição na posição já
        // atualizada, então um frame muito longo faria a queda pular um tile inteiro. Com
        // 1/45 s e MaxFallSpeed 600 o passo máximo é ~13 px, menos que o tile de 16.
        MaxDeltaTime = 1f / 45f;

        _font = Assets.LoadFont("fonts/DejaVuSans.ttf", 20f);

        Events.MessageShown += message => Console.WriteLine($"[msg] {message}");

        // Os scripts de Scripts/ são registrados sozinhos pelo atributo [SceneScript] —
        // nada de Scenes.Register na mão.
        LoadScene(BootScene ?? "scenes/level1.json");
    }

    protected override void OnUpdate(float deltaTime)
    {
        _elapsed += deltaTime;

        var controller = World.TryFind("Player", out var player)
            ? player.Get<PlatformerController>()
            : null;

        if (_smokeTest)
            RunSmokeScript(controller);

        if (Input.IsKeyDown(Key.Escape))
        {
            Exit();
            return;
        }

        // Diálogo aberto (mensagem da bandeira): jogador congela até dispensar.
        if (controller is not null)
            controller.Enabled = !Dialogue.IsActive;

        if (Dialogue.IsActive)
        {
            if (Input.WasKeyPressed(Key.Space) || Input.WasKeyPressed(Key.Enter)
                || Input.WasKeyPressed(Key.Z) || Input.WasMouseClicked())
                Dialogue.Advance();
        }
        else if (Input.WasKeyPressed(Key.R) && SceneManager.CurrentScene is { } current)
        {
            LoadScene(current);                               // reinicia a fase atual
        }

        if (Window is { } window)
        {
            _titleTimer += deltaTime;
            if (_titleTimer >= 0.25f)
            {
                _titleTimer = 0f;
                int fps = deltaTime > 0f ? (int)MathF.Round(1f / deltaTime) : 0;
                window.Title = $"Aurora Platformer — {fps} FPS | {SceneManager.CurrentScene} | " +
                               $"{World.EntityCount} entidades | {SpriteBatch.DrawCallsLastFrame} draw calls";
            }
        }
    }

    protected override void OnRenderUI(float deltaTime)
    {
        DrawLabel($"Moedas: {(int)State.GetVariable("Coins")}", new Vector2(16f, 14f),
            Color.FromBytes(255, 226, 96));
        DrawLabel($"Mortes: {(int)State.GetVariable("Deaths")}", new Vector2(16f, 46f),
            Color.FromBytes(255, 140, 140));

        string level = SceneManager.CurrentScene?.Contains("level2") == true ? "Fase 2" : "Fase 1";
        DrawLabel(level, new Vector2(ScreenSize.X - 120f, 14f), Color.White);

        DrawLabel("A/D ou setas: mover   ·   Espaço/W: pular   ·   R: reiniciar   ·   ESC: sair",
            new Vector2(16f, ScreenSize.Y - 42f), Color.FromBytes(230, 230, 235));

        Dialogue.Draw(SpriteBatch, _font, ScreenSize.X, ScreenSize.Y);
    }

    private void DrawLabel(string text, Vector2 position, Color color)
    {
        SpriteBatch.DrawRect(position - new Vector2(8f, 5f),
            _font.MeasureText(text) + new Vector2(16f, 10f), new Color(0f, 0f, 0f, 0.45f));
        _font.Draw(SpriteBatch, text, position, color);
    }

    /// <summary>
    /// Roteiro do <c>--smoke</c>: confere, sem ninguém no teclado, que o jogador para no chão,
    /// que o pulo sobe de verdade e volta, que moeda e espinho reagem ao toque e que a bandeira
    /// leva para a fase 2. Qualquer falha derruba o processo com exceção (o CI vê o exit code).
    /// </summary>
    private void RunSmokeScript(PlatformerController? controller)
    {
        // Diálogo da bandeira é avançado sozinho.
        _smokeTimer += 0.016f;
        if (Dialogue.IsActive && _smokeTimer > 0.15f)
        {
            _smokeTimer = 0f;
            Dialogue.Advance();
        }

        var transform = controller?.Entity.Get<Transform>();

        switch (_smokeStep)
        {
            case 0 when _elapsed > 0.35f:
                Require(transform is not null, "entidade Player com PlatformerController não existe na fase 1.");
                Require(MathF.Abs(transform!.Position.Y - 260f) < 3f,
                    $"jogador devia estar parado sobre o chão em Y≈260, está em {transform.Position.Y:0.0}.");
                _smokeStep++;
                break;

            case 1 when _elapsed > 0.50f:
                _jumpStartY = transform!.Position.Y;
                controller!.RequestJump();
                _smokeStep++;
                break;

            case 2 when _elapsed > 0.75f:
                Require(transform!.Position.Y < _jumpStartY - 30f,
                    $"pulo não subiu: saiu de {_jumpStartY:0.0} e está em {transform.Position.Y:0.0}.");
                _smokeStep++;
                break;

            case 3 when _elapsed > 1.35f:
                Require(MathF.Abs(transform!.Position.Y - 260f) < 3f,
                    $"jogador devia ter caído de volta no chão (Y≈260), está em {transform.Position.Y:0.0}.");
                _smokeStep++;
                break;

            case 4 when _elapsed > 1.50f:
                transform!.Position = new Vector2(88f, 264f);   // em cima da Coin1
                _smokeStep++;
                break;

            case 5 when _elapsed > 1.70f:
                Require((int)State.GetVariable("Coins") == 1,
                    $"moeda não foi coletada: Coins = {(int)State.GetVariable("Coins")}.");
                Require(!World.TryFind("Coin1", out _), "Coin1 devia ter sido destruída ao ser coletada.");
                _smokeStep++;
                break;

            case 6 when _elapsed > 1.85f:
                transform!.Position = new Vector2(392f, 264f);  // em cima do Spike1
                _smokeStep++;
                break;

            case 7 when _elapsed > 2.05f:
                Require((int)State.GetVariable("Deaths") == 1,
                    $"espinho não matou: Deaths = {(int)State.GetVariable("Deaths")}.");
                Require(Vector2.Distance(transform!.Position, controller!.SpawnPoint) < 8f,
                    $"jogador devia ter voltado ao spawn {controller.SpawnPoint}, está em {transform.Position}.");
                _smokeStep++;
                break;

            case 8 when _elapsed > 2.20f:
                transform!.Position = new Vector2(920f, 224f);  // em cima da bandeira
                _smokeStep++;
                break;

            case 9 when _elapsed > 3.10f:
                Require(SceneManager.CurrentScene == "scenes/level2.json",
                    $"bandeira devia ter trocado para a fase 2, cena atual: {SceneManager.CurrentScene}.");
                Require(World.TryFind("Player", out _), "fase 2 carregou sem entidade Player.");
                Require(World.Query<Transform, Tilemap>().Any(), "fase 2 carregou sem tilemap.");
                Console.WriteLine("[smoke] ok: chão segura o jogador, pulo sobe e volta, moeda, " +
                                  "espinho/respawn e troca de fase pela bandeira.");
                _smokeStep++;
                break;

            case 10 when _elapsed > 3.25f:
                Exit();
                break;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"[smoke] {message}");
    }
}
