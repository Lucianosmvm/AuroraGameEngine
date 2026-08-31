using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Aurora.Editor.Models;
using Aurora.Runtime.Graphics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Aurora.Editor.ViewModels;

/// <summary>Um clipe dentro do editor de sprite sheet: nome, os índices de frame na ordem em
/// que tocam, quanto dura cada um e se repete.</summary>
public sealed class SheetClipViewModel : ViewModelBase
{
    private readonly Action _onEdited;
    private string _name;
    private string _frames;
    private float _duration;
    private bool _loop;

    public SheetClipViewModel(SpriteSheetClip clip, Action onEdited, Action<SheetClipViewModel> onRemove)
    {
        _name = clip.Name;
        _frames = string.Join(", ", clip.Frames);
        _duration = clip.Duration;
        _loop = clip.Loop;
        _onEdited = onEdited;
        RemoveCommand = new RelayCommand(() => onRemove(this));
    }

    public ICommand RemoveCommand { get; }

    public string ClipName
    {
        get => _name;
        set { if (Set(ref _name, value)) { Raise(nameof(Summary)); _onEdited(); } }
    }

    /// <summary>Os índices como o autor digita: "0, 1, 2". O editor também escreve aqui quando
    /// o autor manda a seleção da grade pro clipe.</summary>
    public string FramesText
    {
        get => _frames;
        set
        {
            if (!Set(ref _frames, value))
                return;
            Raise(nameof(Summary));
            Raise(nameof(FrameCount));
            _onEdited();
        }
    }

    public float Duration
    {
        get => _duration;
        set
        {
            if (!Set(ref _duration, value))
                return;
            Raise(nameof(DurationText));
            Raise(nameof(FpsText));
            Raise(nameof(Summary));
            _onEdited();
        }
    }

    public string DurationText
    {
        get => _duration.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && f > 0f)
                Duration = f;
        }
    }

    /// <summary>Frames por segundo — a mesma duração dita do jeito que animador pensa.
    /// Editar um recalcula o outro; o arquivo continua guardando só a duração.</summary>
    public string FpsText
    {
        get => _duration <= 0f ? "" : Math.Round(1f / _duration, 2).ToString(CultureInfo.InvariantCulture);
        set
        {
            if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float fps) && fps > 0f)
                Duration = 1f / fps;
        }
    }

    public bool Loop
    {
        get => _loop;
        set { if (Set(ref _loop, value)) _onEdited(); }
    }

    public int[] Frames => ParseFrames(_frames);

    public int FrameCount => Frames.Length;

    public string Summary
    {
        get
        {
            int n = FrameCount;
            return n == 0
                ? "sem frames"
                : $"{n} frame{(n == 1 ? "" : "s")} · {Math.Round(1f / Math.Max(_duration, 0.0001f))} fps";
        }
    }

    public SpriteSheetClip ToClip() => new()
    {
        Name = _name,
        Frames = Frames,
        Duration = _duration,
        Loop = _loop,
    };

    public static int[] ParseFrames(string text)
    {
        var list = new List<int>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(part, out int i))
                list.Add(i);
        return [.. list];
    }
}

/// <summary>
/// Editor de sprite sheet: escolhe a imagem, recorta em frames (grade regular ou recorte livre
/// desenhado à mão), monta os clipes de animação e grava um <c>.sheet.json</c> em
/// <c>Assets/spritesheets/</c>.
///
/// <para>Por que um editor e não campos no inspector: o recorte é uma decisão sobre a IMAGEM
/// (onde cada frame começa) e digitar "0, 1, 2, 3" sem ver a folha é adivinhação — o erro só
/// aparece no Play, com o personagem piscando meio frame. Aqui a grade fica desenhada por cima
/// da imagem, o índice de cada célula é visível, e o preview toca o clipe na velocidade real
/// antes de gravar.</para>
///
/// <para>O arquivo gerado é lido pelo <c>Animator</c> em runtime (campo <c>Sheet</c>), então a
/// mesma folha serve dez entidades e corrigir o recorte conserta as dez.</para>
/// </summary>
public sealed class SpriteSheetViewModel : ViewModelBase
{
    /// <summary>Subpasta dos assets onde as folhas vivem.</summary>
    public const string Folder = "spritesheets";

    private const string Extension = ".sheet.json";

    private readonly MainViewModel _owner;
    private readonly DispatcherTimer _previewTimer;

    private string _texturePath = "";
    private Bitmap? _image;
    private int _imageWidth;
    private int _imageHeight;

    private int _frameWidth = 16;
    private int _frameHeight = 16;
    private int _columns = 1;
    private int _rows = 1;
    private int _marginX;
    private int _marginY;
    private int _spacingX;
    private int _spacingY;

    private bool _sliceByCount;
    private bool _freeCut;
    private int _zoom = 2;

    private string _sheetName = "";
    private string? _selectedSheetFile;
    private SheetClipViewModel? _selectedClip;
    private string _status = "";
    private bool _isPlaying = true;
    private int _previewStep;
    private bool _suppressReload;

    public SpriteSheetViewModel(MainViewModel owner)
    {
        _owner = owner;

        NewSheetCommand = new RelayCommand(NewSheet);
        SaveCommand = new RelayCommand(Save);
        DeleteSheetCommand = new RelayCommand(DeleteSheet);
        BrowseImageCommand = new RelayCommand(() => _ = BrowseImageAsync());
        PickImageCommand = new RelayCommand(p => { if (p is AssetViewModel a) TexturePath = a.RelativePath; });

        AddClipCommand = new RelayCommand(AddClip);
        FramesFromSelectionCommand = new RelayCommand(FramesFromSelection);
        AppendSelectionCommand = new RelayCommand(AppendSelection);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        SelectAllCommand = new RelayCommand(SelectAll);
        SelectRowCommand = new RelayCommand(SelectPreviewRow);
        ClearFreeCutCommand = new RelayCommand(ClearFreeCut);
        UndoFreeCutCommand = new RelayCommand(UndoFreeCut);
        ApplyToEntityCommand = new RelayCommand(ApplyToEntity);
        ZoomInCommand = new RelayCommand(() => Zoom = Math.Min(16, Zoom + 1));
        ZoomOutCommand = new RelayCommand(() => Zoom = Math.Max(1, Zoom - 1));

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _previewTimer.Tick += (_, _) => AdvancePreview();
        _previewTimer.Start();

        ReloadImageAssets();
        ReloadSheetFiles();
    }

    // ---------------------------------------------------------------- listas

    /// <summary>Imagens do projeto — a origem normal de uma folha.</summary>
    public ObservableCollection<AssetViewModel> ImageAssets { get; } = [];

    /// <summary>Folhas já gravadas, pelo caminho relativo ("spritesheets/player.sheet.json").</summary>
    public ObservableCollection<string> SheetFiles { get; } = [];

    public ObservableCollection<SheetClipViewModel> Clips { get; } = [];

    /// <summary>Índices marcados na grade, NA ORDEM em que foram clicados — é essa ordem que
    /// vira a sequência do clipe. Um Set perderia justamente a informação que importa.</summary>
    public List<int> Selection { get; } = [];

    /// <summary>Recortes livres desenhados à mão. Vazio = a folha é uma grade regular.</summary>
    public List<SpriteSheetFrame> FreeFrames { get; } = [];

    // ------------------------------------------------------------- comandos

    public ICommand NewSheetCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteSheetCommand { get; }
    public ICommand BrowseImageCommand { get; }
    public ICommand PickImageCommand { get; }
    public ICommand AddClipCommand { get; }
    public ICommand FramesFromSelectionCommand { get; }
    public ICommand AppendSelectionCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectRowCommand { get; }
    public ICommand ClearFreeCutCommand { get; }
    public ICommand UndoFreeCutCommand { get; }
    public ICommand ApplyToEntityCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }

    /// <summary>Disparado quando algo que a tela desenha mudou (grade, seleção, zoom, imagem).
    /// O canvas escuta e redesenha — não dá pra ligar isso por binding porque o desenho não é
    /// uma propriedade, é um Render.</summary>
    public event Action? VisualChanged;

    // ---------------------------------------------------------------- imagem

    public string TexturePath
    {
        get => _texturePath;
        set
        {
            if (!Set(ref _texturePath, value))
                return;

            LoadImage();
            if (_sheetName.Length == 0 && value.Length > 0)
                SheetName = Path.GetFileNameWithoutExtension(value);
            Raise(nameof(ImageLabel));
            Raise(nameof(HasImage));
        }
    }

    public Bitmap? Image => _image;

    public int ImageWidth => _imageWidth;
    public int ImageHeight => _imageHeight;

    public bool HasImage => _image is not null;

    public string ImageLabel => _image is null
        ? "nenhuma imagem escolhida"
        : $"{_texturePath} — {_imageWidth}×{_imageHeight}px";

    /// <summary>Troca a imagem em memória. A anterior NÃO é descartada aqui de propósito: o
    /// preview entrega CroppedBitmap que aponta pra ela, e liberar por baixo de um bitmap que a
    /// tela ainda pode desenhar é crash em código nativo. O GC dá conta.</summary>
    private void LoadImage()
    {
        _image = null;
        _imageWidth = _imageHeight = 0;

        string root = _owner.AssetsRootDisplay;
        if (_texturePath.Length > 0 && root.Length > 0)
        {
            string full = Path.Combine(root, _texturePath);
            try
            {
                if (File.Exists(full))
                {
                    _image = new Bitmap(full);
                    _imageWidth = _image.PixelSize.Width;
                    _imageHeight = _image.PixelSize.Height;
                }
                else
                {
                    Status = $"Imagem não encontrada: {_texturePath}";
                }
            }
            catch (Exception ex)
            {
                Status = $"Erro ao abrir a imagem: {ex.Message}";
            }
        }

        Selection.Clear();
        RecomputeGrid();
        Raise(nameof(Image));
        Raise(nameof(ImageWidth));
        Raise(nameof(ImageHeight));
        RaiseVisual();
    }

    private async Task BrowseImageAsync()
    {
        if (_owner.PickTextureFromDisk is not { } pick)
            return;

        if (await pick() is { } relative)
        {
            ReloadImageAssets();
            TexturePath = relative;
        }
    }

    public void ReloadImageAssets()
    {
        ImageAssets.Clear();
        foreach (var asset in _owner.Assets)
        {
            string ext = Path.GetExtension(asset.RelativePath).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg")
                ImageAssets.Add(asset);
        }
    }

    // ----------------------------------------------------------------- grade

    /// <summary>false: o autor diz o TAMANHO do frame e o editor conta quantos cabem (o caso
    /// comum — 32×32 é o que a arte tem). true: o autor diz QUANTAS colunas e linhas a folha
    /// tem e o editor divide a imagem (o caso de folha exportada já dividida).</summary>
    public bool SliceByCount
    {
        get => _sliceByCount;
        set { if (Set(ref _sliceByCount, value)) { RecomputeGrid(); Raise(nameof(SliceBySize)); } }
    }

    public bool SliceBySize
    {
        get => !_sliceByCount;
        set => SliceByCount = !value;
    }

    public int FrameWidth
    {
        get => _frameWidth;
        set { if (Set(ref _frameWidth, Math.Max(1, value))) RecomputeGrid(); }
    }

    public int FrameHeight
    {
        get => _frameHeight;
        set { if (Set(ref _frameHeight, Math.Max(1, value))) RecomputeGrid(); }
    }

    public int Columns
    {
        get => _columns;
        set { if (Set(ref _columns, Math.Max(1, value))) RecomputeGrid(); }
    }

    public int Rows
    {
        get => _rows;
        set { if (Set(ref _rows, Math.Max(1, value))) RecomputeGrid(); }
    }

    public int MarginX
    {
        get => _marginX;
        set { if (Set(ref _marginX, Math.Max(0, value))) RecomputeGrid(); }
    }

    public int MarginY
    {
        get => _marginY;
        set { if (Set(ref _marginY, Math.Max(0, value))) RecomputeGrid(); }
    }

    public int SpacingX
    {
        get => _spacingX;
        set { if (Set(ref _spacingX, Math.Max(0, value))) RecomputeGrid(); }
    }

    public int SpacingY
    {
        get => _spacingY;
        set { if (Set(ref _spacingY, Math.Max(0, value))) RecomputeGrid(); }
    }

    /// <summary>Recorte livre: cada frame é um retângulo desenhado com o mouse, em vez de uma
    /// célula da grade. Pra folha que não é regular — sprite de largura variável, tileset
    /// montado à mão. A numeração segue a ordem em que os retângulos foram desenhados.</summary>
    public bool FreeCut
    {
        get => _freeCut;
        set
        {
            if (!Set(ref _freeCut, value))
                return;
            Selection.Clear();
            Raise(nameof(GridCut));
            Raise(nameof(FrameCount));
            Raise(nameof(GridLabel));
            RaiseVisual();
        }
    }

    public bool GridCut
    {
        get => !_freeCut;
        set => FreeCut = !value;
    }

    public int Zoom
    {
        get => _zoom;
        set { if (Set(ref _zoom, Math.Clamp(value, 1, 16))) { Raise(nameof(ZoomLabel)); RaiseVisual(); } }
    }

    public string ZoomLabel => $"{_zoom}×";

    public int FrameCount => _freeCut ? FreeFrames.Count : _columns * _rows;

    public string GridLabel => _freeCut
        ? $"{FreeFrames.Count} recorte(s) livre(s)"
        : $"{_columns}×{_rows} = {_columns * _rows} frames de {_frameWidth}×{_frameHeight}px";

    /// <summary>
    /// Fecha a conta entre tamanho do frame e número de células. Qual dos dois é entrada e qual
    /// é resultado depende de <see cref="SliceByCount"/> — o outro é sempre recalculado, então
    /// os campos na tela nunca mostram uma grade que não existe na imagem.
    /// </summary>
    private void RecomputeGrid()
    {
        if (_imageWidth > 0 && _imageHeight > 0)
        {
            if (_sliceByCount)
            {
                _frameWidth = SpriteSheetAsset.FitSize(_imageWidth, _columns, _marginX, _spacingX);
                _frameHeight = SpriteSheetAsset.FitSize(_imageHeight, _rows, _marginY, _spacingY);
            }
            else
            {
                _columns = SpriteSheetAsset.FitCount(_imageWidth, _frameWidth, _marginX, _spacingX);
                _rows = SpriteSheetAsset.FitCount(_imageHeight, _frameHeight, _marginY, _spacingY);
            }
        }

        Raise(nameof(FrameWidth));
        Raise(nameof(FrameHeight));
        Raise(nameof(Columns));
        Raise(nameof(Rows));
        Raise(nameof(FrameCount));
        Raise(nameof(GridLabel));
        RaiseVisual();
    }

    // ------------------------------------------------------------- seleção

    /// <summary>Retângulo do frame na imagem, ou null se o índice não existe. A conta da grade
    /// é a mesma do runtime de propósito — ver <see cref="SpriteSheetAsset.GridRect"/>.</summary>
    public RectF? RectOf(int index)
    {
        if (_freeCut)
        {
            if (index < 0 || index >= FreeFrames.Count)
                return null;
            var f = FreeFrames[index];
            return new RectF(f.X, f.Y, f.Width, f.Height);
        }

        if (index >= _columns * _rows)
            return null;

        return SpriteSheetAsset.GridRect(index, _columns, _frameWidth, _frameHeight,
            _marginX, _marginY, _spacingX, _spacingY);
    }

    /// <summary>Índice do frame sob um pixel da imagem. -1 quando o ponto caiu na margem ou no
    /// vão entre frames — clicar ali não deve marcar o vizinho por arredondamento.</summary>
    public int FrameAt(int px, int py)
    {
        if (!_freeCut)
        {
            return SpriteSheetAsset.GridIndexAt(px, py, _columns, _rows, _frameWidth, _frameHeight,
                _marginX, _marginY, _spacingX, _spacingY);
        }

        // De trás pra frente: o retângulo desenhado por último fica por cima.
        for (int i = FreeFrames.Count - 1; i >= 0; i--)
        {
            var f = FreeFrames[i];
            if (px >= f.X && px < f.X + f.Width && py >= f.Y && py < f.Y + f.Height)
                return i;
        }
        return -1;
    }

    /// <summary>Marca (ou desmarca) um frame. <paramref name="additive"/> = clique com Ctrl:
    /// soma à seleção em vez de recomeçar, que é como se monta uma sequência fora de ordem.</summary>
    public void ToggleFrame(int index, bool additive)
    {
        if (index < 0 || index >= FrameCount)
            return;

        if (!additive)
        {
            bool onlyThis = Selection.Count == 1 && Selection[0] == index;
            Selection.Clear();
            if (!onlyThis)
                Selection.Add(index);
        }
        else if (!Selection.Remove(index))
        {
            Selection.Add(index);
        }

        RaiseSelection();
    }

    /// <summary>Seleciona todos os frames que a área arrastada tocou, em ordem de leitura.</summary>
    public void SelectArea(int x0, int y0, int x1, int y1, bool additive)
    {
        if (!additive)
            Selection.Clear();

        int left = Math.Min(x0, x1), right = Math.Max(x0, x1);
        int top = Math.Min(y0, y1), bottom = Math.Max(y0, y1);

        for (int i = 0; i < FrameCount; i++)
        {
            if (RectOf(i) is not { } r)
                continue;
            bool hits = r.X < right && r.X + r.Width > left && r.Y < bottom && r.Y + r.Height > top;
            if (hits && !Selection.Contains(i))
                Selection.Add(i);
        }

        RaiseSelection();
    }

    private void SelectAll()
    {
        Selection.Clear();
        for (int i = 0; i < FrameCount; i++)
            Selection.Add(i);
        RaiseSelection();
    }

    /// <summary>Seleciona a linha inteira do primeiro frame marcado — folha de personagem quase
    /// sempre põe uma animação por linha, então isto é o atalho de um clique pro caso comum.</summary>
    private void SelectPreviewRow()
    {
        if (_freeCut || Selection.Count == 0 || _columns <= 0)
            return;

        int row = Selection[0] / _columns;
        Selection.Clear();
        for (int col = 0; col < _columns; col++)
            Selection.Add(row * _columns + col);
        RaiseSelection();
    }

    private void ClearSelection()
    {
        Selection.Clear();
        RaiseSelection();
    }

    private void RaiseSelection()
    {
        Raise(nameof(SelectionLabel));
        Raise(nameof(HasSelection));
        RaiseVisual();
    }

    public bool HasSelection => Selection.Count > 0;

    public string SelectionLabel => Selection.Count == 0
        ? "nenhum frame marcado"
        : $"marcados: {string.Join(", ", Selection)}";

    // -------------------------------------------------------- recorte livre

    /// <summary>Guarda um retângulo desenhado à mão, já grudado nos pixels da imagem.</summary>
    public void AddFreeRect(int x, int y, int width, int height)
    {
        if (width < 1 || height < 1)
            return;

        // Só apara contra a imagem quando ela é conhecida: aparar contra tamanho 0 devolveria
        // um recorte de lado zero, que é pior que um recorte que passa da borda.
        if (_imageWidth > 0 && _imageHeight > 0)
        {
            x = Math.Clamp(x, 0, _imageWidth - 1);
            y = Math.Clamp(y, 0, _imageHeight - 1);
            width = Math.Min(width, _imageWidth - x);
            height = Math.Min(height, _imageHeight - y);
        }
        else
        {
            x = Math.Max(0, x);
            y = Math.Max(0, y);
        }

        FreeFrames.Add(new SpriteSheetFrame { X = x, Y = y, Width = width, Height = height });
        Raise(nameof(FrameCount));
        Raise(nameof(GridLabel));
        RaiseVisual();
    }

    private void UndoFreeCut()
    {
        if (FreeFrames.Count == 0)
            return;
        FreeFrames.RemoveAt(FreeFrames.Count - 1);
        Selection.RemoveAll(i => i >= FreeFrames.Count);
        Raise(nameof(FrameCount));
        Raise(nameof(GridLabel));
        RaiseSelection();
    }

    private void ClearFreeCut()
    {
        FreeFrames.Clear();
        Selection.Clear();
        Raise(nameof(FrameCount));
        Raise(nameof(GridLabel));
        RaiseSelection();
    }

    // ---------------------------------------------------------------- clipes

    public SheetClipViewModel? SelectedClip
    {
        get => _selectedClip;
        set
        {
            if (!Set(ref _selectedClip, value))
                return;
            _previewStep = 0;
            Raise(nameof(HasSelectedClip));
            RaisePreview();
        }
    }

    public bool HasSelectedClip => _selectedClip is not null;

    private void AddClip()
    {
        var clip = new SheetClipViewModel(
            new SpriteSheetClip
            {
                Name = NextClipName(),
                Duration = 0.1f,
                Loop = true,
                Frames = [.. Selection],
            },
            OnClipEdited,
            RemoveClip);

        Clips.Add(clip);
        SelectedClip = clip;
        OnClipEdited();
    }

    /// <summary>"Idle" no primeiro clipe e depois "Clipe 2", "Clipe 3"… Nome repetido quebraria
    /// o Play(nome) do Animator em silêncio, então o padrão nunca repete.</summary>
    private string NextClipName()
    {
        if (Clips.Count == 0)
            return "Idle";

        for (int i = Clips.Count + 1; ; i++)
        {
            string candidate = $"Clipe {i}";
            if (!Clips.Any(c => c.ClipName == candidate))
                return candidate;
        }
    }

    private void RemoveClip(SheetClipViewModel clip)
    {
        Clips.Remove(clip);
        if (ReferenceEquals(_selectedClip, clip))
            SelectedClip = Clips.FirstOrDefault();
        OnClipEdited();
    }

    /// <summary>Troca os frames do clipe selecionado pelos marcados na grade.</summary>
    private void FramesFromSelection()
    {
        if (_selectedClip is null)
        {
            AddClip();
            return;
        }

        _selectedClip.FramesText = string.Join(", ", Selection);
        _previewStep = 0;
        RaisePreview();
    }

    /// <summary>Acrescenta os marcados ao fim do clipe — pra montar ida-e-volta (0,1,2,1) sem
    /// precisar digitar a sequência.</summary>
    private void AppendSelection()
    {
        if (_selectedClip is null || Selection.Count == 0)
            return;

        var frames = _selectedClip.Frames.ToList();
        frames.AddRange(Selection);
        _selectedClip.FramesText = string.Join(", ", frames);
        RaisePreview();
    }

    private void OnClipEdited()
    {
        Raise(nameof(HasClips));
        RaisePreview();
    }

    public bool HasClips => Clips.Count > 0;

    // --------------------------------------------------------------- preview

    public bool IsPlaying
    {
        get => _isPlaying;
        set { if (Set(ref _isPlaying, value)) _previewStep = 0; }
    }

    /// <summary>Índice do frame que o preview está mostrando — o canvas destaca essa célula, pra
    /// dar pra ver QUAL pedaço da folha está tocando, não só o resultado.</summary>
    public int PreviewFrame
    {
        get
        {
            var frames = _selectedClip?.Frames;
            if (frames is null || frames.Length == 0)
                return Selection.Count > 0 ? Selection[0] : -1;
            return frames[_previewStep % frames.Length];
        }
    }

    /// <summary>O frame atual já recortado da imagem, pronto pro Image do preview.</summary>
    public CroppedBitmap? PreviewImage
    {
        get
        {
            if (_image is null || RectOf(PreviewFrame) is not { } r)
                return null;

            int x = (int)Math.Clamp(r.X, 0, _imageWidth - 1);
            int y = (int)Math.Clamp(r.Y, 0, _imageHeight - 1);
            int w = (int)Math.Clamp(r.Width, 1, _imageWidth - x);
            int h = (int)Math.Clamp(r.Height, 1, _imageHeight - y);

            try
            {
                return new CroppedBitmap(_image, new Avalonia.PixelRect(x, y, w, h));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public string PreviewLabel
    {
        get
        {
            if (_selectedClip is null)
                return "escolha um clipe";
            int n = _selectedClip.FrameCount;
            return n == 0 ? "clipe sem frames" : $"{_selectedClip.ClipName} — frame {PreviewFrame}";
        }
    }

    private double _previewElapsed;

    /// <summary>Avança o preview no relógio de verdade: o timer bate a 30Hz e o clipe pode durar
    /// 0,04s por frame, então quem manda no passo é o tempo acumulado, não o tick.</summary>
    private void AdvancePreview()
    {
        if (!_isPlaying || _selectedClip is null)
            return;

        var frames = _selectedClip.Frames;
        if (frames.Length == 0)
            return;

        _previewElapsed += _previewTimer.Interval.TotalSeconds;
        double duration = Math.Max(0.016, _selectedClip.Duration);
        if (_previewElapsed < duration)
            return;

        _previewElapsed = 0;

        if (!_selectedClip.Loop && _previewStep >= frames.Length - 1)
            return;

        _previewStep = (_previewStep + 1) % frames.Length;
        RaisePreview();
    }

    private void RaisePreview()
    {
        Raise(nameof(PreviewImage));
        Raise(nameof(PreviewFrame));
        Raise(nameof(PreviewLabel));
        RaiseVisual();
    }

    private void RaiseVisual() => VisualChanged?.Invoke();

    // ------------------------------------------------------------ arquivo

    public string SheetName
    {
        get => _sheetName;
        set { if (Set(ref _sheetName, value)) Raise(nameof(TargetPath)); }
    }

    /// <summary>Onde o Salvar vai gravar — mostrado na tela porque o caminho é o que a cena vai
    /// referenciar depois.</summary>
    public string TargetPath => _sheetName.Length == 0 ? "" : $"{Folder}/{Sanitize(_sheetName)}{Extension}";

    public string? SelectedSheetFile
    {
        get => _selectedSheetFile;
        set
        {
            if (!Set(ref _selectedSheetFile, value) || _suppressReload)
                return;
            if (value is not null)
                LoadSheet(value);
        }
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    private string SheetsDir => _owner.AssetsRootDisplay.Length == 0
        ? ""
        : Path.Combine(_owner.AssetsRootDisplay, Folder);

    public void ReloadSheetFiles()
    {
        _suppressReload = true;
        string current = _selectedSheetFile ?? "";
        SheetFiles.Clear();

        if (SheetsDir.Length > 0 && Directory.Exists(SheetsDir))
        {
            foreach (var file in Directory.EnumerateFiles(SheetsDir, "*" + Extension).Order(StringComparer.OrdinalIgnoreCase))
                SheetFiles.Add($"{Folder}/{Path.GetFileName(file)}");
        }

        _selectedSheetFile = SheetFiles.Contains(current) ? current : null;
        Raise(nameof(SelectedSheetFile));
        _suppressReload = false;
    }

    private void NewSheet()
    {
        Clips.Clear();
        FreeFrames.Clear();
        Selection.Clear();
        SelectedClip = null;
        FreeCut = false;
        SheetName = "";
        TexturePath = "";
        _suppressReload = true;
        SelectedSheetFile = null;
        _suppressReload = false;
        Status = "Folha nova — escolha a imagem e o tamanho do frame.";
        OnClipEdited();
        RaiseSelection();
    }

    public void LoadSheet(string relativePath)
    {
        string root = _owner.AssetsRootDisplay;
        if (root.Length == 0)
            return;

        string full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
        {
            Status = $"Folha não encontrada: {relativePath}";
            return;
        }

        try
        {
            var sheet = SpriteSheetAsset.FromJson(File.ReadAllText(full));

            FreeFrames.Clear();
            FreeFrames.AddRange(sheet.Frames);
            _freeCut = sheet.IsFreeCut;

            _marginX = sheet.MarginX;
            _marginY = sheet.MarginY;
            _spacingX = sheet.SpacingX;
            _spacingY = sheet.SpacingY;
            _frameWidth = Math.Max(1, sheet.FrameWidth);
            _frameHeight = Math.Max(1, sheet.FrameHeight);
            _columns = Math.Max(1, sheet.Columns);
            _rows = Math.Max(1, sheet.Rows);
            _sliceByCount = false;

            _sheetName = Path.GetFileName(full);
            if (_sheetName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                _sheetName = _sheetName[..^Extension.Length];

            Selection.Clear();
            Clips.Clear();
            foreach (var clip in sheet.Clips)
                Clips.Add(new SheetClipViewModel(clip, OnClipEdited, RemoveClip));

            // A imagem por último: TexturePath recalcula a grade, e recalcular ANTES de ler os
            // números do arquivo sobrescreveria o recorte que se acabou de carregar.
            _texturePath = sheet.Texture;
            LoadImage();

            Raise(nameof(SheetName));
            Raise(nameof(TargetPath));
            Raise(nameof(TexturePath));
            Raise(nameof(ImageLabel));
            Raise(nameof(HasImage));
            Raise(nameof(FreeCut));
            Raise(nameof(GridCut));
            Raise(nameof(SliceByCount));
            Raise(nameof(SliceBySize));
            Raise(nameof(MarginX));
            Raise(nameof(MarginY));
            Raise(nameof(SpacingX));
            Raise(nameof(SpacingY));
            SelectedClip = Clips.FirstOrDefault();
            OnClipEdited();
            RaiseSelection();

            Status = $"{relativePath} — {sheet.Clips.Count} clipe(s).";
        }
        catch (Exception ex)
        {
            Status = $"Erro ao ler a folha: {ex.Message}";
        }
    }

    public SpriteSheetAsset ToAsset()
    {
        var sheet = new SpriteSheetAsset
        {
            Texture = _texturePath,
            FrameWidth = _frameWidth,
            FrameHeight = _frameHeight,
            Columns = _columns,
            Rows = _rows,
            MarginX = _marginX,
            MarginY = _marginY,
            SpacingX = _spacingX,
            SpacingY = _spacingY,
        };

        if (_freeCut)
            sheet.Frames.AddRange(FreeFrames);

        foreach (var clip in Clips)
            sheet.Clips.Add(clip.ToClip());

        return sheet;
    }

    public void Save()
    {
        if (SheetsDir.Length == 0)
        {
            Status = "Abra um projeto antes de gravar a folha.";
            return;
        }

        if (_texturePath.Length == 0)
        {
            Status = "Escolha a imagem da folha antes de gravar.";
            return;
        }

        if (Sanitize(_sheetName).Length == 0)
        {
            Status = "Dê um nome à folha.";
            return;
        }

        var duplicated = Clips.GroupBy(c => c.ClipName).FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
        {
            Status = $"Dois clipes chamados '{duplicated.Key}' — o Play(nome) não saberia qual tocar.";
            return;
        }

        try
        {
            Directory.CreateDirectory(SheetsDir);
            string full = Path.Combine(SheetsDir, Sanitize(_sheetName) + Extension);
            File.WriteAllText(full, ToAsset().ToJson());

            ReloadSheetFiles();
            _suppressReload = true;
            SelectedSheetFile = TargetPath;
            _suppressReload = false;

            _owner.ReloadAssets();
            Status = $"Gravado em {TargetPath}.";
        }
        catch (Exception ex)
        {
            Status = $"Erro ao gravar: {ex.Message}";
        }
    }

    private void DeleteSheet()
    {
        if (_selectedSheetFile is not { } relative || SheetsDir.Length == 0)
            return;

        try
        {
            string full = Path.Combine(_owner.AssetsRootDisplay, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full))
                File.Delete(full);
            ReloadSheetFiles();
            _owner.ReloadAssets();
            Status = $"{relative} excluída.";
        }
        catch (Exception ex)
        {
            Status = $"Erro ao excluir: {ex.Message}";
        }
    }

    // ------------------------------------------------------- usar na cena

    public string ApplyLabel => _owner.SelectedEntity is { } e
        ? $"Aplicar em '{e.Name}'"
        : "Aplicar na entidade selecionada";

    public void RaiseApplyLabel() => Raise(nameof(ApplyLabel));

    /// <summary>
    /// Liga a folha na entidade selecionada da cena: aponta o Animator pro arquivo e, se o
    /// SpriteRenderer ainda não tem textura, usa a imagem da folha. Grava o caminho e NÃO os
    /// clipes — é isso que faz corrigir a folha corrigir todas as entidades que a usam.
    /// </summary>
    private void ApplyToEntity()
    {
        if (_owner.SelectedEntity is not { } entity)
        {
            Status = "Selecione uma entidade na cena primeiro.";
            return;
        }

        if (TargetPath.Length == 0 || _texturePath.Length == 0)
        {
            Status = "Grave a folha antes de aplicar.";
            return;
        }

        string full = Path.Combine(_owner.AssetsRootDisplay, Folder, Sanitize(_sheetName) + Extension);
        if (!File.Exists(full))
            Save();
        if (!File.Exists(full))
            return;

        if (entity.Node["Components"] is not JsonArray components)
            return;

        var sprite = components.OfType<JsonObject>()
            .FirstOrDefault(c => c["Type"]?.GetValue<string>() == "SpriteRenderer");
        if (sprite is null)
        {
            sprite = new JsonObject { ["Type"] = "SpriteRenderer", ["Texture"] = _texturePath };
            components.Add(sprite);
        }
        else if (string.IsNullOrWhiteSpace(sprite["Texture"]?.GetValue<string>()))
        {
            sprite["Texture"] = _texturePath;
        }

        var animator = components.OfType<JsonObject>()
            .FirstOrDefault(c => c["Type"]?.GetValue<string>() == "Animator");
        if (animator is null)
        {
            animator = new JsonObject { ["Type"] = "Animator" };
            components.Add(animator);
        }

        animator["Sheet"] = TargetPath;
        animator["FrameWidth"] = _frameWidth;
        animator["FrameHeight"] = _frameHeight;
        animator["SheetColumns"] = _columns;
        SetOrRemove(animator, "MarginX", _marginX);
        SetOrRemove(animator, "MarginY", _marginY);
        SetOrRemove(animator, "SpacingX", _spacingX);
        SetOrRemove(animator, "SpacingY", _spacingY);
        animator.Remove("Clips");   // os clipes moram na folha; deixar cópia velha aqui venceria ela

        entity.RefreshComponents();
        Status = $"'{entity.Name}' agora usa {TargetPath}.";
    }

    private static void SetOrRemove(JsonObject node, string key, int value)
    {
        if (value == 0)
            node.Remove(key);
        else
            node[key] = value;
    }

    /// <summary>Tira do nome o que não pode virar nome de arquivo — o autor digita "Herói 01" e
    /// não devia receber uma exceção de caminho inválido por isso.</summary>
    private static string Sanitize(string name)
    {
        var clean = new string([.. name.Trim().Where(c => !Path.GetInvalidFileNameChars().Contains(c))]);
        return clean.EndsWith(".sheet", StringComparison.OrdinalIgnoreCase) ? clean[..^6] : clean;
    }

    /// <summary>Para o preview. Chamado no fechamento da janela — sem isto o timer continuaria
    /// batendo e segurando o view model vivo depois que ninguém mais o vê.</summary>
    public void Dispose() => _previewTimer.Stop();
}
