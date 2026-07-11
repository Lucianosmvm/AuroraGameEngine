using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Uma sequência de frames de um sprite sheet. Frames são índices na grade:
/// 0 = canto superior esquerdo, 1 = próximo à direita, etc.
/// </summary>
public sealed class AnimationClip
{
    public string Name = "";
    public int[] Frames = [];

    /// <summary>Duração de cada frame em segundos.</summary>
    public float FrameDuration = 0.1f;

    public bool Loop = true;
}

/// <summary>
/// Behavior que anima um <see cref="SpriteRenderer"/> percorrendo frames de um sprite sheet.
/// Chame <see cref="Play"/> para trocar de clipe. O primeiro clipe da lista é tocado no Start.
/// </summary>
public sealed class Animator : Behavior
{
    /// <summary>Largura de cada frame em pixels no sprite sheet.</summary>
    public int FrameWidth;

    /// <summary>Altura de cada frame em pixels no sprite sheet.</summary>
    public int FrameHeight;

    /// <summary>Quantas colunas de frames tem o sprite sheet.</summary>
    public int SheetColumns = 1;

    public List<AnimationClip> Clips = [];

    private AnimationClip? _current;
    private int _framePos;
    private float _elapsed;
    private bool _finished;

    public string? CurrentClip => _current?.Name;

    /// <summary>True quando um clipe não-loop chegou ao último frame.</summary>
    public bool IsFinished => _finished;

    /// <summary>Congela a animação no frame atual sem limpar o clipe corrente.</summary>
    public void Stop() => _finished = true;

    /// <summary>Troca para o clipe com o nome dado. Ignorado se já está tocando (a menos que restart=true).</summary>
    public void Play(string clipName, bool restart = false)
    {
        if (!restart && _current?.Name == clipName)
            return;

        var clip = Clips.Find(c => c.Name == clipName);
        if (clip is null || clip.Frames.Length == 0)
            return;

        _current = clip;
        _framePos = 0;
        _elapsed = 0f;
        _finished = false;
        ApplyFrame();
    }

    public override void Start()
    {
        if (_current is null && Clips.Count > 0)
            Play(Clips[0].Name);
    }

    public override void Update(float deltaTime)
    {
        if (_current is null || _finished || _current.FrameDuration <= 0f)
            return;

        _elapsed += deltaTime;

        while (_elapsed >= _current.FrameDuration)
        {
            _elapsed -= _current.FrameDuration;
            _framePos++;

            if (_framePos >= _current.Frames.Length)
            {
                if (_current.Loop)
                {
                    _framePos = 0;
                }
                else
                {
                    _framePos = _current.Frames.Length - 1;
                    _finished = true;
                    break;
                }
            }
        }

        ApplyFrame();
    }

    private void ApplyFrame()
    {
        if (_current is null || SheetColumns <= 0 || FrameWidth <= 0 || FrameHeight <= 0)
            return;

        var sprite = Get<SpriteRenderer>();
        if (sprite is null)
            return;

        int index = _current.Frames[_framePos];
        int col = index % SheetColumns;
        int row = index / SheetColumns;
        sprite.SourceRect = new RectF(col * FrameWidth, row * FrameHeight, FrameWidth, FrameHeight);
    }
}
