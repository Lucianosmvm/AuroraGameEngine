using System.Numerics;
using Aurora.Runtime.Assets;
using Aurora.Runtime.Ecs;
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
    private float _fadeDuration;
    private float _fadeAlpha;
    private float _fadeTimer;
    private Phase _phase = Phase.None;

    private enum Phase { None, FadingOut, FadingIn }

    /// <summary>Caminho da cena carregada mais recentemente.</summary>
    public string? CurrentScene { get; private set; }

    /// <summary>True durante o fade de transição; behaviors continuam rodando.</summary>
    public bool IsTransitioning => _phase != Phase.None;

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
    public void Load(string scenePath, bool additive = false)
    {
        ExecuteLoad(scenePath, additive);
    }

    /// <summary>
    /// Faz fade para preto, carrega a cena e faz fade de volta.
    /// Ignora a chamada se uma transição já está em andamento.
    /// </summary>
    public void LoadWithFade(string scenePath, float duration = 0.3f, bool additive = false)
    {
        if (IsTransitioning)
            return;

        _pendingScene = scenePath;
        _pendingAdditive = additive;
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
                    ExecuteLoad(_pendingScene!, _pendingAdditive);
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

    private void ExecuteLoad(string path, bool additive)
    {
        if (!additive)
        {
            _world.Clear();
            _dialogue.Clear();
            _events.Reset();
        }

        _serializer.Load(_assets.LoadText(path),
            new SceneContext { World = _world, Assets = _assets });

        CurrentScene = path;
    }
}
