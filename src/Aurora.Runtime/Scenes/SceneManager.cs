using System.Numerics;
using Aurora.Runtime.Assets;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Events;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.UI;

namespace Aurora.Runtime.Scenes;

/// <summary>
/// Carrega e troca cenas, limpando o <see cref="World"/> entre elas.
/// Suporta transição com fade preto. A ação ChangeScene nos eventos visuais
/// invoca <see cref="LoadWithFade"/> automaticamente.
/// </summary>
public sealed class SceneManager
{
    private readonly World _world;
    private readonly SceneSerializer _serializer;
    private readonly EventSystem _events;
    private readonly DialogueSystem _dialogue;
    private readonly AssetManager _assets;

    private string? _pendingScene;
    private bool _pendingAdditive;
    private string? _pendingSpawnPoint;
    private float _fadeDuration;
    private float _fadeAlpha;
    private float _fadeTimer;
    private Phase _phase = Phase.None;

    private enum Phase { None, FadingOut, FadingIn }

    /// <summary>Caminho da cena carregada mais recentemente.</summary>
    public string? CurrentScene { get; private set; }

    /// <summary>True durante o fade de transição; behaviors continuam rodando.</summary>
    public bool IsTransitioning => _phase != Phase.None;

    /// <summary>
    /// Disparado depois que a cena terminou de ser montada no <see cref="World"/>, com o
    /// caminho carregado. É o gancho pra criar entidades por código: carregar uma cena chama
    /// <see cref="World.Clear"/>, então tudo que não está no .json morre na troca — o que
    /// nasce no <c>OnLoad</c> do jogo só sobrevive até a primeira transição. Não dispara se o
    /// load falhou.
    /// </summary>
    public event Action<string>? SceneLoaded;

    internal SceneManager(World world, SceneSerializer serializer,
        EventSystem events, DialogueSystem dialogue, AssetManager assets)
    {
        _world = world;
        _serializer = serializer;
        _events = events;
        _dialogue = dialogue;
        _assets = assets;
    }

    /// <summary>
    /// Carrega imediatamente, sem transição.
    /// Se <paramref name="additive"/> for false (padrão), limpa o mundo antes de carregar.
    /// </summary>
    public void Load(string scenePath, bool additive = false, string? spawnPoint = null)
    {
        ExecuteLoad(scenePath, additive, spawnPoint);
    }

    /// <summary>
    /// Nome da entidade movida pro marcador de <c>spawnPoint</c> na troca de cena. É o mesmo
    /// "quem é o jogador" do EventSystem — o Game mantém os dois iguais.
    /// </summary>
    public string PlayerEntityName { get; set; } = "Player";

    /// <summary>
    /// Faz fade para preto, carrega a cena e faz fade de volta.
    /// Ignora a chamada se uma transição já está em andamento.
    /// </summary>
    public void LoadWithFade(string scenePath, float duration = 0.3f, bool additive = false,
        string? spawnPoint = null)
    {
        if (IsTransitioning)
            return;

        _pendingScene = scenePath;
        _pendingAdditive = additive;
        _pendingSpawnPoint = spawnPoint;
        _fadeDuration = Math.Max(0.05f, duration);
        _fadeAlpha = 0f;
        _fadeTimer = 0f;
        _phase = Phase.FadingOut;
    }

    internal void Update(float dt)
    {
        switch (_phase)
        {
            case Phase.FadingOut:
                _fadeTimer += dt;
                _fadeAlpha = Math.Min(1f, _fadeTimer / _fadeDuration);
                if (_fadeAlpha >= 1f)
                {
                    ExecuteLoad(_pendingScene!, _pendingAdditive, _pendingSpawnPoint);
                    _phase = Phase.FadingIn;
                    _fadeTimer = 0f;
                }
                break;

            case Phase.FadingIn:
                _fadeTimer += dt;
                _fadeAlpha = Math.Max(0f, 1f - _fadeTimer / _fadeDuration);
                if (_fadeAlpha <= 0f)
                    _phase = Phase.None;
                break;
        }
    }

    internal void DrawOverlay(SpriteBatch batch, float screenWidth, float screenHeight)
    {
        if (_fadeAlpha <= 0f)
            return;

        batch.DrawRect(Vector2.Zero, new Vector2(screenWidth, screenHeight),
            new Color(0f, 0f, 0f, _fadeAlpha));
    }

    private void ExecuteLoad(string path, bool additive, string? spawnPoint = null)
    {
        if (!additive)
        {
            _world.Clear();
            _dialogue.Clear();
            _events.Reset();
        }

        // Cena referenciando arquivo inexistente/inválido não deve derrubar o jogo inteiro
        // (ex.: ação ChangeScene com nome de cena digitado errado no editor).
        try
        {
            _serializer.Load(_assets.LoadText(path),
                new SceneContext { World = _world, Assets = _assets });
            CurrentScene = path;

            // Reaplica o que ja aconteceu nesta cena (inimigo morto, bau aberto) ANTES de
            // qualquer update: um frame sequer com o chefe de volta em pe ja apareceria na tela.
            // A cena atual tambem e anotada aqui porque o World, que registra as mortes, nao
            // conhece o SceneManager.
            if (_world.SceneState is { } sceneState)
            {
                sceneState.CurrentScene = path;
                sceneState.ApplyTo(path, _world);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SceneManager] Falha ao carregar cena '{path}': {ex.Message}");
            return;
        }

        MoveToSpawnPoint(spawnPoint);

        // Fora do try de propósito: exceção de um assinante é bug do jogo, não falha de load.
        // Deixar cair no catch acima esconderia o stack real atrás da mensagem errada — e
        // ainda marcaria como "cena carregada com erro" uma cena que subiu inteira.
        SceneLoaded?.Invoke(path);
    }

    /// <summary>
    /// Move o jogador pro marcador de mesmo nome na cena recém-carregada. Marcador é uma
    /// entidade qualquer com Transform — no editor, uma entidade vazia com o nome da porta.
    ///
    /// <para>É o que faz um mapa ligado por portas funcionar: sem isso o jogador sempre reaparece
    /// na posição gravada no arquivo da cena, então voltar por uma porta diferente cai no mesmo
    /// canto. Marcador inexistente só loga — a cena já está de pé, e derrubar o jogo porque a
    /// porta tem nome errado seria pior que o jogador nascer no lugar padrão.</para>
    /// </summary>
    private void MoveToSpawnPoint(string? spawnPoint)
    {
        if (string.IsNullOrEmpty(spawnPoint))
            return;

        if (!_world.TryFind(spawnPoint, out var marker) || marker.Get<Transform>() is not { } markerTransform)
        {
            Console.Error.WriteLine(
                $"[SceneManager] Marcador de spawn '{spawnPoint}' não existe na cena '{CurrentScene}' — " +
                $"'{PlayerEntityName}' ficou onde a cena manda.");
            return;
        }

        // Leva os filhos junto: sem isso, atravessar uma porta deixa a arma do jogador na
        // sala anterior (ver World.TeleportWithChildren).
        if (_world.TryFind(PlayerEntityName, out var player))
            _world.TeleportWithChildren(player, markerTransform.Position);
    }
}
