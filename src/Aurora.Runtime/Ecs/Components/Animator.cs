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
/// Transição automática entre clipes — checada todo frame. "Any" em From casa com qualquer
/// clipe atual. Parâmetros (Set/GetFloat, Set/GetBool) são locais deste Animator, não do
/// GameState global — sete-os de um Behavior próprio (ex.: SetFloat("Speed", velocidade)).
/// </summary>
public sealed class AnimatorTransition
{
    public string From = "Any";
    public string To = "";

    public string Parameter = "";

    /// <summary>true = Parameter é bool (compara com BoolValue); false = float (compara com CompareOp/CompareValue).</summary>
    public bool IsBool;

    public string CompareOp = ">=";
    public float CompareValue;
    public bool BoolValue = true;
}

/// <summary>
/// Behavior que anima um <see cref="SpriteRenderer"/> percorrendo frames de um sprite sheet.
/// Chame <see cref="Play"/> para trocar de clipe manualmente, ou defina <see cref="Transitions"/>
/// pra trocar sozinho quando um parâmetro (SetFloat/SetBool) atinge a condição — um "state
/// machine" simples, no estilo Animator Controller da Unity, mas com parâmetros locais.
/// O primeiro clipe da lista é tocado no Start.
///
/// <para>O recorte da folha vem de uma de duas fontes: preenchido campo a campo aqui (FrameWidth,
/// FrameHeight, SheetColumns) ou carregado de um <c>.sheet.json</c> pelo campo <see cref="Sheet"/>
/// — o arquivo que o editor de sprite sheet grava. Ver <see cref="ApplySheet"/>.</para>
/// </summary>
public sealed class Animator : Behavior
{
    /// <summary>Caminho do <c>.sheet.json</c> com o recorte e os clipes desta folha, relativo à
    /// raiz de assets. Vazio = o recorte está nos campos abaixo. Quem carrega o arquivo é o
    /// serializador de cena, que já tem o AssetManager — o componente só guarda o caminho.</summary>
    public string Sheet = "";

    /// <summary>Largura de cada frame em pixels no sprite sheet.</summary>
    public int FrameWidth;

    /// <summary>Altura de cada frame em pixels no sprite sheet.</summary>
    public int FrameHeight;

    /// <summary>Quantas colunas de frames tem o sprite sheet.</summary>
    public int SheetColumns = 1;

    /// <summary>Borda vazia antes do primeiro frame, em pixels.</summary>
    public int MarginX;
    public int MarginY;

    /// <summary>Vão entre frames vizinhos, em pixels — comum em folha exportada por atlas.</summary>
    public int SpacingX;
    public int SpacingY;

    /// <summary>Recortes livres em pixels da imagem, pra folha que não é grade regular.
    /// Não-vazio: o índice do frame indexa ESTA lista e a grade é ignorada.</summary>
    public List<RectF> FrameRects = [];

    public List<AnimationClip> Clips = [];

    /// <summary>Transições automáticas — checadas a cada frame antes de avançar o clipe atual.</summary>
    public List<AnimatorTransition> Transitions = [];

    private readonly Dictionary<string, float> _floatParams = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _boolParams = new(StringComparer.OrdinalIgnoreCase);

    private AnimationClip? _current;
    private int _framePos;
    private float _elapsed;
    private bool _finished;

    public string? CurrentClip => _current?.Name;

    /// <summary>True quando um clipe não-loop chegou ao último frame.</summary>
    public bool IsFinished => _finished;

    /// <summary>
    /// Copia o recorte de uma folha <c>.sheet.json</c> pra este Animator. Os clipes da folha só
    /// entram nos nomes que a cena ainda NÃO definiu: quem autorou um clipe na entidade quis
    /// aquele, e a folha não pode desfazer isso pelas costas.
    /// </summary>
    public void ApplySheet(SpriteSheetAsset sheet)
    {
        FrameWidth = sheet.FrameWidth;
        FrameHeight = sheet.FrameHeight;
        SheetColumns = Math.Max(1, sheet.Columns);
        MarginX = sheet.MarginX;
        MarginY = sheet.MarginY;
        SpacingX = sheet.SpacingX;
        SpacingY = sheet.SpacingY;

        FrameRects.Clear();
        foreach (var f in sheet.Frames)
            FrameRects.Add(new RectF(f.X, f.Y, f.Width, f.Height));

        foreach (var clip in sheet.Clips)
        {
            if (Clips.Exists(c => c.Name == clip.Name))
                continue;

            Clips.Add(new AnimationClip
            {
                Name = clip.Name,
                Frames = (int[])clip.Frames.Clone(),
                FrameDuration = clip.Duration,
                Loop = clip.Loop,
            });
        }
    }

    // ---- Parâmetros locais (state machine) ----

    public void SetFloat(string name, float value) => _floatParams[name] = value;
    public float GetFloat(string name, float fallback = 0f) => _floatParams.TryGetValue(name, out float v) ? v : fallback;

    public void SetBool(string name, bool value) => _boolParams[name] = value;
    public bool GetBool(string name) => _boolParams.TryGetValue(name, out bool v) && v;

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
        EvaluateTransitions();

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

    /// <summary>Testa as transições em ordem e troca de clipe na primeira que casar — uma
    /// troca por frame, pra não pular dois clipes no mesmo tick.</summary>
    private void EvaluateTransitions()
    {
        foreach (var t in Transitions)
        {
            if (t.From != "Any" && t.From != CurrentClip)
                continue;
            if (t.To == CurrentClip)
                continue;

            bool met = t.IsBool
                ? GetBool(t.Parameter) == t.BoolValue
                : Compare(GetFloat(t.Parameter), t.CompareOp, t.CompareValue);

            if (met)
            {
                Play(t.To);
                return;
            }
        }
    }

    private static bool Compare(float actual, string op, float value) => op switch
    {
        ">=" => actual >= value,
        "<=" => actual <= value,
        ">"  => actual > value,
        "<"  => actual < value,
        "!=" => MathF.Abs(actual - value) > 1e-6f,
        _    => MathF.Abs(actual - value) < 1e-6f,   // "==" default
    };

    /// <summary>Retângulo do índice na folha: um recorte livre quando a lista existe, senão a
    /// posição na grade (já contando margem e vão). Null = índice fora do recorte.</summary>
    public RectF? RectOf(int index)
    {
        if (FrameRects.Count > 0)
            return index >= 0 && index < FrameRects.Count ? FrameRects[index] : null;

        return SpriteSheetAsset.GridRect(index, SheetColumns, FrameWidth, FrameHeight,
            MarginX, MarginY, SpacingX, SpacingY);
    }

    private void ApplyFrame()
    {
        if (_current is null || RectOf(_current.Frames[_framePos]) is not { } rect)
            return;

        var sprite = Get<SpriteRenderer>();
        if (sprite is null)
            return;

        sprite.SourceRect = rect;
    }
}
