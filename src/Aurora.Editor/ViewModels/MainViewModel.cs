using System.Collections.ObjectModel;
using System.Diagnostics;
using Aurora.Editor.Models;

namespace Aurora.Editor.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    /// <summary>Edições com a mesma tag dentro desta janela colapsam num só passo de undo.</summary>
    private const double CoalesceWindowMs = 900;

    private SceneDocument? _document;
    private ProjectSettings? _settings;
    private EntityViewModel? _selectedEntity;
    private bool _isDirty;
    private string _status = "Nenhuma cena aberta. Arquivo → Abrir Cena…";

    // Undo por snapshot: cada passo guarda o JSON completo da cena (cenas são pequenas).
    // _lastSnapshot é sempre o estado atual serializado — no undo ele vai para o redo.
    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private string _lastSnapshot = "";
    private string? _lastEditTag;
    private DateTime _lastEditAt;
    private bool _restoring;

    public ObservableCollection<EntityViewModel> Entities { get; } = [];
    public ObservableCollection<AssetViewModel> Assets { get; } = [];
    public ObservableCollection<EntityViewModel> EventEntities { get; } = [];
    public ObservableCollection<SceneFileViewModel> SceneFiles { get; } = [];
    public ObservableCollection<SceneFileViewModel> UiScreens { get; } = [];
    public ObservableCollection<PrefabFileViewModel> Prefabs { get; } = [];
    public ObservableCollection<ScriptFileViewModel> Scripts { get; } = [];
    public bool HasEventEntities => EventEntities.Count > 0;

    /// <summary>Teto de linhas guardadas no painel de saída. Um jogo que loga por frame enche
    /// isso em segundos; sem teto a lista cresce até o editor engasgar.</summary>
    private const int MaxOutputLines = 800;

    /// <summary>Saída ao vivo do jogo (stdout+stderr) e do próprio editor. Antes disso um
    /// Console.WriteLine dentro de um script do usuário não aparecia em lugar nenhum: o
    /// processo era largado com UseShellExecute e o console dele morria junto.</summary>
    public ObservableCollection<string> GameOutput { get; } = [];

    private bool _isOutputVisible;

    /// <summary>Painel aberto. Abre sozinho no Play e em qualquer coisa que escreva log —
    /// log que ninguém vê não serve pra nada.</summary>
    public bool IsOutputVisible
    {
        get => _isOutputVisible;
        set => Set(ref _isOutputVisible, value);
    }

    /// <summary>Escreve uma linha no painel (e abre o painel). Sempre chamada da thread de UI:
    /// quem lê o processo do jogo faz await num contexto capturado da UI.</summary>
    public void Log(string line)
    {
        GameOutput.Add(line);
        while (GameOutput.Count > MaxOutputLines)
            GameOutput.RemoveAt(0);

        IsOutputVisible = true;
    }

    public void ClearOutput() => GameOutput.Clear();

    /// <summary>Templates prontos oferecidos pelo "+ Novo…" do painel SCRIPTS (ver ScriptTemplates).</summary>
    public IReadOnlyList<ScriptTemplates.Template> ScriptTemplateOptions { get; } = ScriptTemplates.All;

    private ScriptTemplates.Template _selectedScriptTemplate = ScriptTemplates.All[0];

    public ScriptTemplates.Template SelectedScriptTemplate
    {
        get => _selectedScriptTemplate;
        set => Set(ref _selectedScriptTemplate, value);
    }

    /// <summary>Scripts [SceneScript] descobertos no projeto do jogo — alimenta o dropdown
    /// "+Add Componente" das entidades. Atualizado em background ao abrir cena/projeto.</summary>
    public ObservableCollection<GameScriptDiscovery.ScriptInfo> CustomScripts { get; } = [];

    private int _scriptCatalogVersion;
    private bool _isRefreshingScripts;

    /// <summary>True enquanto <see cref="RefreshScriptCatalog"/> builda o jogo pra descobrir
    /// scripts — usado pra desabilitar o botão "↻" e evitar cliques concorrentes.</summary>
    public bool IsRefreshingScripts
    {
        get => _isRefreshingScripts;
        private set => Set(ref _isRefreshingScripts, value);
    }

    /// <summary>Roda em background e substitui <see cref="CustomScripts"/> quando terminar.
    /// Versão incremental evita corrida entre chamadas concorrentes sobrescrevendo com resultado velho.
    /// Chamado automaticamente ao abrir cena/projeto, e manualmente pelo botão "↻" ao lado de
    /// "+Add Componente" (scripts novos/editados não aparecem sozinhos — precisa reroda isto).</summary>
    public async void RefreshScriptCatalog()
    {
        if (string.IsNullOrWhiteSpace(_settings?.GameProject))
            return;

        int version = ++_scriptCatalogVersion;
        IsRefreshingScripts = true;
        Status = "Procurando scripts [SceneScript] no projeto do jogo...";
        try
        {
            var result = await GameScriptDiscovery.DiscoverAsync(_settings.GameProject);
            if (version != _scriptCatalogVersion)
                return; // outra chamada mais nova já está em andamento/terminou

            if (result.Error is not null)
            {
                // Mantém o catálogo antigo (não zera o dropdown) — só avisa que este
                // refresh falhou, tipicamente porque o script novo/editado não compila.
                Status = $"Scripts não atualizados: {result.Error}";
                StatusDetail = result.Detail ?? result.Error;
                return;
            }

            CustomScripts.Clear();
            foreach (var script in result.Scripts)
                CustomScripts.Add(script);
            Status = $"{CustomScripts.Count} script(s) encontrado(s).";
            StatusDetail = null;
        }
        finally
        {
            if (version == _scriptCatalogVersion)
                IsRefreshingScripts = false;
        }
    }

    public SceneDocument? Document => _document;

    /// <summary>True quando o documento aberto é uma tela de UI (marca "UI":true no JSON,
    /// ver NewUiScreen) — filtra o "+Add Componente" pra não deixar misturar UiButton/UiText/…
    /// numa entidade de gameplay comum (eles não têm sistema de render nesse contexto e
    /// travam o jogo no load: SceneSerializer não conhece esses tipos fora de TELAS UI).</summary>
    public bool IsUiScreenDocument =>
        _document?.Root["UI"]?.GetValue<bool>() == true;

    private bool _showColliders = true;

    /// <summary>Liga o desenho dos colisores no viewport (checkbox da toolbar). Ligado por padrão:
    /// Collider não tem representação visual nenhuma no jogo, então sem isso a única forma de saber
    /// onde a hitbox está é rodar e bater nela.</summary>
    public bool ShowColliders
    {
        get => _showColliders;
        set => Set(ref _showColliders, value);
    }

    private bool _debugOverlayOnPlay;

    /// <summary>Passa <c>--debug</c> pro jogo no Play. Sem isto o jogo rodando é caixa preta:
    /// dá pra ver a hitbox no editor, mas não a hitbox de verdade, com a posição de verdade,
    /// depois que os controladores mexeram em tudo.</summary>
    public bool DebugOverlayOnPlay
    {
        get => _debugOverlayOnPlay;
        set => Set(ref _debugOverlayOnPlay, value);
    }

    private bool _snapToGrid;

    /// <summary>Prende o arrasto de entidade a uma grade de <see cref="SnapSize"/> px. Alinhar
    /// plataforma no olho deixa buraco de 1px que o jogador atravessa; com snap, dois objetos
    /// arrastados pro mesmo lugar encostam de verdade. Alt segurado inverte durante o arrasto.</summary>
    public bool SnapToGrid
    {
        get => _snapToGrid;
        set => Set(ref _snapToGrid, value);
    }

    private decimal _snapSize = 16;

    /// <summary>Passo da grade em pixels de mundo. 16 por padrão: é o tile que o
    /// GameProjectScaffolder usa no Tilemap novo, então cenário e objeto caem na mesma grade.
    /// decimal, não double: é o tipo de NumericUpDown.Value, e binding com tipo exato não
    /// depende de conversão implícita (que falharia calada, deixando o campo vazio).</summary>
    public decimal SnapSize
    {
        get => _snapSize;
        set => Set(ref _snapSize, Math.Clamp(value, 1m, 512m));
    }

    private string _entityFilter = "";

    /// <summary>Filtro da hierarquia por nome (case-insensitive). Cena com dezenas de objetos
    /// vira rolagem sem isto.</summary>
    public string EntityFilter
    {
        get => _entityFilter;
        set
        {
            if (Set(ref _entityFilter, value))
                ApplyEntityFilter();
        }
    }

    /// <summary>Marca cada entidade como visível ou não conforme <see cref="EntityFilter"/>.
    /// Filtra por visibilidade em vez de remover da coleção: remover perderia a seleção e
    /// bagunçaria o índice que Excluir usa pra escolher quem fica selecionado depois.</summary>
    private void ApplyEntityFilter()
    {
        string filter = _entityFilter.Trim();
        foreach (var entity in Entities)
            entity.MatchesFilter = filter.Length == 0
                || entity.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Resolução de referência da UI vinda do aurora.project.json (padrão 1280x720).
    /// O SceneCanvas desenha a moldura do jogo com esse tamanho e resolve os Anchor contra ela,
    /// pro preview bater com o jogo em vez de acompanhar o tamanho do painel do editor. Precisa
    /// ser igual ao <c>Game.DesignResolution</c> do código do jogo.</summary>
    public int DesignWidth
    {
        get => _settings?.EffectiveDesignWidth ?? 1280;
        set => SetDesign(width: value);
    }

    /// <summary>Ver <see cref="DesignWidth"/>.</summary>
    public int DesignHeight
    {
        get => _settings?.EffectiveDesignHeight ?? 720;
        set => SetDesign(height: value);
    }

    /// <summary>Tamanho em pixels da fonte que o jogo passa pro UI.Draw — usado pra medir UiText
    /// no preview. Precisa bater com o <c>Assets.LoadFont(..., 22f)</c> do código do jogo.</summary>
    public float UiFontSize
    {
        get => _settings?.EffectiveUiFontSize ?? 22f;
        set => SetDesign(fontSize: value);
    }

    /// <summary>
    /// Tamanhos de tela comuns, pro campo de resolução de referência não ser um par de números que
    /// ninguém sabe de cor. Escolher aqui grava no projeto — é a resolução do JOGO, não um modo de
    /// visualização.
    /// </summary>
    public IReadOnlyList<ScreenPreset> ScreenPresets { get; } =
    [
        new("PC / paisagem — 1280x720", 1280, 720),
        new("PC / paisagem — 1920x1080", 1920, 1080),
        new("Tablet 4:3 — 1024x768", 1024, 768),
        new("Celular retrato — 720x1280", 720, 1280),
        new("Celular retrato alto — 1080x2400", 1080, 2400),
        new("Celular deitado alto — 2400x1080", 2400, 1080),
    ];

    /// <summary>Preset que bate com a resolução atual, ou null quando ela foi digitada à mão.</summary>
    public ScreenPreset? SelectedScreenPreset
    {
        get => ScreenPresets.FirstOrDefault(p => p.Width == DesignWidth && p.Height == DesignHeight);
        set
        {
            if (value is { Width: > 0, Height: > 0 })
                SetDesign(value.Width, value.Height);
        }
    }

    /// <summary>
    /// Aparelho contra o qual comparar no viewport. É SÓ visualização — não grava nada e não muda
    /// o jogo: desenha a tela do aparelho em volta da moldura pra mostrar onde caem as barras
    /// pretas quando a proporção dele é diferente da do seu jogo.
    /// </summary>
    public IReadOnlyList<ScreenPreset> ComparePresets { get; } =
    [
        ScreenPreset.None,
        new("Celular 20:9 (retrato)", 1080, 2400),
        new("Celular 20:9 (deitado)", 2400, 1080),
        new("Celular 16:9 (deitado)", 1280, 720),
        new("Tablet 4:3", 1024, 768),
        new("Monitor 16:10", 1920, 1200),
        new("Monitor ultrawide 21:9", 2560, 1080),
    ];

    private ScreenPreset _compareDevice = ScreenPreset.None;

    /// <summary>Aparelho da comparação. <see cref="ScreenPreset.None"/> = desligada.</summary>
    public ScreenPreset CompareDevice
    {
        get => _compareDevice;
        set
        {
            _compareDevice = value ?? ScreenPreset.None;
            Raise();
            Raise(nameof(CompareHint));
            SceneEdited?.Invoke();
        }
    }

    /// <summary>
    /// Quanto da tela do aparelho sobra em barra preta, na proporção atual. É a resposta curta pra
    /// "como fica no celular": com resolução de referência, a engine encaixa o jogo inteiro na
    /// tela (letterbox) — nada é cortado, nada se move de lugar, só sobra borda.
    /// </summary>
    public string CompareHint
    {
        get
        {
            if (_compareDevice.Width <= 0 || _compareDevice.Height <= 0)
                return "";

            double gameAspect = DesignWidth / (double)DesignHeight;
            double deviceAspect = _compareDevice.Width / (double)_compareDevice.Height;

            if (Math.Abs(gameAspect - deviceAspect) < 0.001)
                return "Mesma proporção: o jogo ocupa a tela inteira do aparelho.";

            double used = deviceAspect > gameAspect
                ? gameAspect / deviceAspect       // sobra nas laterais
                : deviceAspect / gameAspect;      // sobra em cima e embaixo

            string onde = deviceAspect > gameAspect ? "nas laterais" : "em cima e embaixo";
            return $"Barras {onde}: {(1 - used) * 100:0}% da tela do aparelho. " +
                   $"Nada é cortado nem muda de lugar — a tela inteira do jogo encolhe junto.";
        }
    }

    /// <summary>Proporção da resolução atual, em texto ("16:9"). Não existe conta exata pra todo
    /// tamanho, então cai pra "W:H" reduzido pelo MDC — 2400x1080 vira 20:9.</summary>
    public string ScreenAspectLabel
    {
        get
        {
            int w = DesignWidth, h = DesignHeight;
            int divisor = Mdc(w, h);
            return $"{w / divisor}:{h / divisor}";
        }
    }

    private static int Mdc(int a, int b) => b == 0 ? Math.Max(1, a) : Mdc(b, a % b);

    /// <summary>Troca largura por altura. Montar o menu deitado e depois descobrir que o jogo é
    /// retrato é o erro que isto evita em um clique.</summary>
    public void SwapScreenOrientation() => SetDesign(DesignHeight, DesignWidth);

    private void SetDesign(int? width = null, int? height = null, float? fontSize = null)
    {
        if (_settings is null)
            return;

        // Valor inválido (campo apagado, zero, negativo) volta pro padrão em vez de gravar lixo
        // que faria a moldura sumir ou o texto ser medido com escala zero.
        if (width is not null) _settings.DesignWidth = width > 0 ? width : null;
        if (height is not null) _settings.DesignHeight = height > 0 ? height : null;
        if (fontSize is not null) _settings.UiFontSize = fontSize > 0 ? fontSize : null;

        try { _settings.Save(); } catch { /* sem permissão de escrita — ignora */ }

        _uiFontPath = null;
        _uiFont = null;
        Raise(nameof(DesignWidth));
        Raise(nameof(DesignHeight));
        Raise(nameof(UiFontSize));
        Raise(nameof(SelectedScreenPreset));
        Raise(nameof(ScreenAspectLabel));
        Raise(nameof(CompareHint));
        Raise(nameof(AndroidOrientationWarning));
        Raise(nameof(DesignResolutionMismatch));
        Raise(nameof(HasDesignResolutionMismatch));
        SceneEdited?.Invoke();
    }

    private string? _uiFontPath;
    private TrueTypeMetrics? _uiFont;

    /// <summary>Métricas da fonte da UI, lidas do TTF do próprio projeto — é o que deixa o editor
    /// medir UiText igual ao runtime. Null quando o arquivo não existe (projeto sem a fonte ainda,
    /// ou caminho errado no aurora.project.json): quem desenha cai numa estimativa e segue.
    /// Cacheado por caminho — o TTF é lido uma vez, não a cada frame de render.</summary>
    public TrueTypeMetrics? UiFont
    {
        get
        {
            if (_document is null)
                return null;

            string path = Path.Combine(_document.AssetsRoot,
                (_settings?.EffectiveUiFont ?? "fonts/DejaVuSans.ttf").Replace('/', Path.DirectorySeparatorChar));

            if (path != _uiFontPath)
            {
                _uiFontPath = path;
                _uiFont = TrueTypeMetrics.FromFile(path);
            }
            return _uiFont;
        }
    }

    /// <summary>Disparado em qualquer edição — o canvas usa para redesenhar.</summary>
    public event Action? SceneEdited;

    public EntityViewModel? SelectedEntity
    {
        get => _selectedEntity;
        set
        {
            if (!Set(ref _selectedEntity, value))
                return;

            RebuildTilePalette();
            Raise(nameof(HasSelectedEntity));
        }
    }

    /// <summary>Habilita Duplicar/Copiar no menu Editar.</summary>
    public bool HasSelectedEntity => _selectedEntity is not null;

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (Set(ref _isDirty, value))
            {
                Raise(nameof(Title));
                Raise(nameof(DocumentName));
            }
        }
    }

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    private string? _statusDetail;

    /// <summary>Log completo (stdout+stderr) do último build/discover que falhou — não existe
    /// console nenhum pra olhar (os processos rodam com CreateNoWindow=true), então isso é
    /// mostrado como tooltip da status bar. Null quando não há detalhe extra (some o tooltip).</summary>
    public string? StatusDetail
    {
        get => _statusDetail;
        set => Set(ref _statusDetail, value);
    }

    public string Title => _document is null
        ? "Aurora Editor"
        : $"Aurora Editor — {Path.GetFileName(_document.FilePath)}{(IsDirty ? " *" : "")}";

    /// <summary>Nome do arquivo da cena aberta (sem pasta nem extensão), com "*" enquanto houver
    /// alteração não salva. É o rótulo da aba do viewport — o <see cref="Title"/> não serve ali
    /// porque carrega o nome do programa junto.</summary>
    public string DocumentName => _document is null
        ? ""
        : $"{Path.GetFileNameWithoutExtension(_document.FilePath)}{(IsDirty ? " *" : "")}";

    public bool HasDocument => _document is not null;
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public bool CanPlay => _document is not null && !string.IsNullOrWhiteSpace(_settings?.GameProject) && !IsPreparingPlay;

    public string AssetsRootDisplay => _document?.AssetsRoot ?? "";

    /// <summary>Caminho do .csproj, diretório ou .exe do jogo. Salvo em aurora.project.json.</summary>
    public string GameProjectPath
    {
        get => _settings?.GameProject ?? "";
        set
        {
            if (_settings is null || _document is null)
                return;
            _settings.GameProject = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            try { _settings.Save(); } catch { /* sem permissão de escrita — ignora */ }
            Raise();
            Raise(nameof(CanPlay));
            Raise(nameof(CanPlayEmbedded));
            Raise(nameof(CanBuild));
            RefreshScriptCatalog();
        }
    }

    /// <summary>Opções de orientação pro APK gerado por "Exportar Android…" — as duas primeiras
    /// (fixas) não giram nunca; as três últimas (Sensor*) giram com o aparelho.</summary>
    /// <summary>
    /// Avisa quando a orientação do APK briga com a proporção da tela de referência: exportar em
    /// retrato um jogo desenhado em 1280x720 dá uma faixa no meio do celular com barra preta em
    /// cima e embaixo, e o autor só descobre instalando.
    /// </summary>
    public string AndroidOrientationWarning
    {
        get
        {
            bool telaDeitada = DesignWidth >= DesignHeight;
            bool apkRetrato = AndroidOrientation.Contains("Portrait", StringComparison.OrdinalIgnoreCase);
            bool apkDeitado = AndroidOrientation.Contains("Landscape", StringComparison.OrdinalIgnoreCase);

            if (telaDeitada && apkRetrato)
                return $"A tela de referência é deitada ({DesignWidth}x{DesignHeight}) e o APK abre em retrato — "
                     + "o jogo vira uma faixa no meio do celular. Gire a tela de referência ou a orientação.";

            if (!telaDeitada && apkDeitado)
                return $"A tela de referência é em pé ({DesignWidth}x{DesignHeight}) e o APK abre deitado — "
                     + "o jogo vira uma coluna no meio do celular. Gire a tela de referência ou a orientação.";

            return "";
        }
    }

    public bool HasAndroidOrientationWarning => AndroidOrientationWarning.Length > 0;

    /// <summary>
    /// Confere a resolução de referência daqui contra o <c>DesignResolution</c> escrito no código
    /// do jogo. Vazio = batem, ou não deu pra saber (sem projeto apontado, .exe em vez de fonte,
    /// resolução calculada em runtime).
    ///
    /// <para>É o ÚNICO jeito do preview mentir: a moldura do editor sai de aurora.project.json e a
    /// tela do jogo sai do código. Enquanto os dois não eram comparados, trocar a resolução aqui
    /// deixava o menu montado pra uma tela e rodando noutra — e o sintoma (elemento ancorado em
    /// Center fora do lugar) não aponta pra causa nenhuma.</para>
    /// </summary>
    public string DesignResolutionMismatch
    {
        get
        {
            if (FindDesignResolutionInCode() is not { } found)
                return "";

            if (found.Width == DesignWidth && found.Height == DesignHeight)
                return "";

            return $"O código do jogo usa DesignResolution {found.Width}x{found.Height}, "
                 + $"mas aqui a tela de referência é {DesignWidth}x{DesignHeight}. "
                 + "Enquanto forem diferentes, o preview do menu não bate com o jogo.";
        }
    }

    public bool HasDesignResolutionMismatch => DesignResolutionMismatch.Length > 0;

    private static readonly System.Text.RegularExpressions.Regex DesignResolutionPattern =
        new(@"DesignResolution\s*=\s*new\s+Vector2D<int>\s*\(\s*(\d+)\s*,\s*(\d+)\s*\)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Procura a atribuição literal de DesignResolution nos .cs do projeto do jogo.
    /// Só entende a forma que o próprio editor gera (literal com dois números); qualquer coisa
    /// calculada devolve null, e null aqui significa "não sei", não "está certo".</summary>
    private (int Width, int Height)? FindDesignResolutionInCode()
    {
        string project = _settings?.GameProject ?? "";
        if (project.Length == 0)
            return null;

        string? directory = Directory.Exists(project) ? project : Path.GetDirectoryName(project);
        if (directory is null || !Directory.Exists(directory))
            return null;

        try
        {
            // Teto de arquivos: a varredura roda em property get (o binding do painel de
            // configurações), e projeto grande com bin/obj dentro viraria centenas de arquivos.
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                         .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                         .Take(200))
            {
                var match = DesignResolutionPattern.Match(File.ReadAllText(file));
                if (match.Success
                    && int.TryParse(match.Groups[1].Value, out int width)
                    && int.TryParse(match.Groups[2].Value, out int height))
                    return (width, height);
            }
        }
        catch
        {
            // Sem permissão, arquivo sumindo no meio da varredura: "não sei" é a resposta certa.
        }

        return null;
    }

    public string[] AndroidOrientations { get; } =
        ["Landscape", "Portrait", "SensorLandscape", "SensorPortrait", "Sensor"];

    /// <summary>Orientação salva em aurora.project.json. Landscape (fixo) é o padrão histórico
    /// mais seguro; Sensor* já foi validado sem crash em teste manual de device real (Android 14),
    /// mas o bug antigo documentado no AndroidExporter pode variar por aparelho/versão — escolha
    /// consciente do dev, não padrão automático.</summary>
    public string AndroidOrientation
    {
        get => _settings?.AndroidOrientation ?? "Landscape";
        set
        {
            if (_settings is null)
                return;
            _settings.AndroidOrientation = value;
            Raise(nameof(AndroidOrientationWarning));
            try { _settings.Save(); } catch { /* sem permissão de escrita — ignora */ }
            Raise();
        }
    }

    private bool _isPreparingPlay;
    public bool IsPreparingPlay
    {
        get => _isPreparingPlay;
        private set
        {
            if (Set(ref _isPreparingPlay, value))
                Raise(nameof(CanPlay));
        }
    }

    /// <summary>Salva a cena, builda o projeto (pra pegar erro de compilação antes de
    /// tentar rodar — <c>dotnet run</c> com UseShellExecute não deixa ler stderr) e só
    /// então lança o executável ou dotnet run com --scene.</summary>
    public async void Play()
    {
        if (_document is null || string.IsNullOrWhiteSpace(_settings?.GameProject))
        {
            Status = "Configure o caminho do projeto (Inspector → PROJETO) antes de usar Play.";
            return;
        }

        SaveScene();

        string project   = _settings!.GameProject!.Trim();
        string scenePath = _document.FilePath;
        bool isExe = project.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        if (!isExe)
        {
            IsPreparingPlay = true;
            Status = "Buildando projeto do jogo...";
            try
            {
                var buildPsi = new ProcessStartInfo("dotnet", $"build \"{project}\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var buildProcess = Process.Start(buildPsi)
                    ?? throw new InvalidOperationException("Não consegui iniciar o dotnet build.");
                string stdout = await buildProcess.StandardOutput.ReadToEndAsync();
                string stderr = await buildProcess.StandardError.ReadToEndAsync();
                await buildProcess.WaitForExitAsync();

                if (buildProcess.ExitCode != 0)
                {
                    Status = $"Play cancelado — build falhou (código {buildProcess.ExitCode}): {GameScriptDiscovery.FirstErrorLine(stdout, stderr)}";
                    StatusDetail = GameScriptDiscovery.CombineLog(stdout, stderr);
                    return;
                }

                StatusDetail = null;
            }
            catch (Exception ex)
            {
                Status = $"Erro ao buildar antes do Play: {ex.Message}";
                StatusDetail = ex.ToString();
                return;
            }
            finally
            {
                IsPreparingPlay = false;
            }
        }

        try
        {
            // UseShellExecute=false + redirect: sem isto o processo do jogo era largado sem
            // ninguém olhando, e QUALQUER falha depois do build (asset faltando, cena inválida,
            // driver de GL) virava só "a janela abriu e fechou" — a exceção morria junto com o
            // processo. O jogo cria a própria janela de GL, então CreateNoWindow só esconde o
            // console, não o jogo.
            var psi = new ProcessStartInfo(isExe ? project : "dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            // Com UseShellExecute=false o processo herda o diretório atual do EDITOR. Pro exe,
            // roda a partir da pasta dele — é o que acontece quando o jogador abre o jogo pelo
            // Explorer, e é o que o Play deve reproduzir.
            if (isExe)
                psi.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(project)) ?? "";

            if (!isExe)
            {
                psi.ArgumentList.Add("run");
                psi.ArgumentList.Add("--project");
                psi.ArgumentList.Add(project);
                psi.ArgumentList.Add("--");
            }

            psi.ArgumentList.Add("--scene");
            psi.ArgumentList.Add(scenePath);

            if (DebugOverlayOnPlay)
            {
                psi.ArgumentList.Add("--debug");

                // O runtime não lê aurora.project.json e não tem fonte embutida — quem sabe qual
                // TTF o projeto usa é o editor, então o caminho vai junto. Sem isso o overlay
                // desenha as hitboxes e fica sem o bloco de números.
                psi.ArgumentList.Add("--debug-font");
                psi.ArgumentList.Add(_settings?.EffectiveUiFont ?? "fonts/DejaVuSans.ttf");
            }

            var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Não consegui iniciar o processo do jogo.");

            Status = $"Jogo iniciado — cena: {Path.GetFileName(scenePath)}";
            StatusDetail = null;

            // Painel zerado a cada Play: misturar a saída de duas execuções é pior que não ter.
            ClearOutput();
            Log($"--- Play: {Path.GetFileName(scenePath)} ---");

            // Sem await: o editor continua usável enquanto o jogo roda. Como Play() está na
            // thread de UI, as continuações de WatchGameAsync voltam pra ela pelo
            // SynchronizationContext do Avalonia — Status/StatusDetail seguem sendo escritos
            // da thread certa.
            _ = WatchGameAsync(process, Path.GetFileName(scenePath));
        }
        catch (Exception ex)
        {
            Status = $"Erro ao iniciar jogo: {ex.Message}";
            StatusDetail = ex.ToString();
        }
    }

    /// <summary>Acompanha o processo do jogo até ele morrer e, se morreu com código != 0,
    /// joga o motivo na status bar (resumo) e no tooltip (stdout+stderr inteiro). É o que
    /// transforma "fechou sozinho" numa mensagem acionável.</summary>
    private async Task WatchGameAsync(Process process, string sceneName)
    {
        using (process)
        {
            var stdoutLines = new List<string>();
            var stderrLines = new List<string>();
            string stdout, stderr;

            try
            {
                // Linha a linha (não ReadToEnd) pra saída aparecer no painel ENQUANTO o jogo
                // roda — é o que faz Console.WriteLine servir de debug. Os dois streams lidos
                // em paralelo: consumir um até o fim antes do outro trava se o processo encher
                // o buffer do que ficou esperando.
                await Task.WhenAll(
                    PumpAsync(process.StandardOutput, stdoutLines, isError: false),
                    PumpAsync(process.StandardError, stderrLines, isError: true));
                await process.WaitForExitAsync();

                stdout = string.Join("\n", stdoutLines);
                stderr = string.Join("\n", stderrLines);
            }
            catch (Exception ex)
            {
                Status = $"Perdi o acompanhamento do jogo: {ex.Message}";
                StatusDetail = ex.ToString();
                return;
            }

            if (process.ExitCode == 0)
            {
                Status = $"Jogo encerrado — cena: {sceneName}";
                StatusDetail = null;
                Log("--- jogo encerrou normalmente ---");
                return;
            }

            // Exceção sem handler no .NET sai como 0xE0434352, que em int vira um negativo
            // ilegível — hex pra esses, decimal pros códigos que o jogo escolheu (Exit(1)).
            string code = process.ExitCode is < 0 or > 0xFFFF
                ? $"0x{process.ExitCode:X8}"
                : process.ExitCode.ToString();

            string reason = GameScriptDiscovery.FirstCrashLine(stdout, stderr);
            Status = $"Jogo fechou sozinho (código {code}): {reason}";
            StatusDetail = GameScriptDiscovery.CombineLog(stdout, stderr);
            Log($"--- jogo fechou sozinho (código {code}): {reason} ---");
        }
    }

    /// <summary>Drena um stream do jogo linha a linha: cada linha vai pro painel na hora e
    /// fica guardada pro resumo de crash no fim. Como WatchGameAsync começou na thread de UI,
    /// as continuações voltam pra ela — Log toca ObservableCollection da thread certa.</summary>
    private async Task PumpAsync(StreamReader reader, List<string> sink, bool isError)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            sink.Add(line);
            // stderr marcado: numa lista só, saber de onde veio a linha é metade do diagnóstico.
            Log(isError ? "! " + line : line);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Play embutido
    //
    // Roda o jogo DENTRO do editor, no contexto de GL do próprio viewport, em vez de lançar um
    // processo. A diferença que importa é o ciclo de iteração: sem processo novo e sem janela
    // nova, e — porque o assembly do jogo é carregado num contexto que delega Aurora.Runtime ao
    // do editor — o inspector escreve na entidade VIVA, sem serializar.
    //
    // O Play() de processo separado continua existindo e não muda: é ele que roda o jogo como o
    // jogador vai receber (janela própria, código de saída, crash isolado do editor). Este aqui
    // troca esse isolamento por velocidade.
    // ---------------------------------------------------------------------------------------

    private GameHost? _gameHost;

    /// <summary>
    /// O host do jogo em execução embutida, pro viewport dirigir. Internal porque
    /// <see cref="GameHost"/> é detalhe do editor — o resto do app fala com o modo Play por
    /// <see cref="IsPlayingEmbedded"/> e pelos comandos.
    /// </summary>
    internal GameHost? ActiveGameHost => _gameHost;

    private bool _isPlayingEmbedded;

    /// <summary>Se há jogo rodando dentro do editor agora.</summary>
    public bool IsPlayingEmbedded
    {
        get => _isPlayingEmbedded;
        private set
        {
            if (Set(ref _isPlayingEmbedded, value))
            {
                Raise(nameof(CanPlayEmbedded));
                Raise(nameof(CanStopEmbedded));
            }
        }
    }

    private bool _isPreparingEmbeddedPlay;

    /// <summary>Compilando/carregando: o botão fica desabilitado, senão dois cliques abrem duas
    /// sessões e a segunda encontra a primeira ainda carregada.</summary>
    public bool IsPreparingEmbeddedPlay
    {
        get => _isPreparingEmbeddedPlay;
        private set
        {
            if (Set(ref _isPreparingEmbeddedPlay, value))
                Raise(nameof(CanPlayEmbedded));
        }
    }

    public bool CanPlayEmbedded => _document is not null
        && !string.IsNullOrWhiteSpace(_settings?.GameProject)
        && !IsPlayingEmbedded
        && !IsPreparingEmbeddedPlay;

    public bool CanStopEmbedded => IsPlayingEmbedded;

    /// <summary>
    /// Pedido pro viewport encerrar o jogo. Quem chama <see cref="GameHost.Stop"/> tem que ser
    /// o controle, não este ViewModel: <see cref="Aurora.Runtime.Game.Shutdown"/> libera textura
    /// e buffer, e isso exige o contexto de GL corrente — coisa que só o controle sabe garantir.
    /// </summary>
    internal event Action? EmbeddedStopRequested;

    /// <summary>
    /// Compila o projeto, carrega o assembly e deixa o jogo pronto pro viewport inicializar no
    /// próximo frame. Não inicializa aqui: sem contexto de GL, não há o que inicializar.
    /// </summary>
    public async Task PlayEmbeddedAsync()
    {
        if (_document is null || string.IsNullOrWhiteSpace(_settings?.GameProject))
        {
            Status = "Configure o caminho do projeto (Inspector → PROJETO) antes de usar Play.";
            return;
        }

        if (IsPlayingEmbedded)
            StopEmbedded();

        SaveScene();

        string project = _settings!.GameProject!.Trim();
        string scenePath = _document.FilePath;

        IsPreparingEmbeddedPlay = true;
        Status = "Compilando o jogo...";

        try
        {
            var (build, assemblyPath) = await GameHost.BuildAsync(project);
            if (!build.Ok)
            {
                Status = build.Message;
                StatusDetail = build.Detail;
                Log($"--- Play embutido cancelado: {build.Message} ---");
                return;
            }

            var host = new GameHost();
            var load = host.Load(assemblyPath!);
            if (!load.Ok)
            {
                host.Dispose();
                Status = load.Message;
                StatusDetail = load.Detail;
                Log($"--- Play embutido cancelado: {load.Message} ---");
                return;
            }

            ConfigureGame(host, assemblyPath!, scenePath);

            _gameHost = host;
            StatusDetail = null;

            // Painel zerado a cada Play, igual ao de processo: misturar a saída de duas
            // execuções é pior que não ter.
            ClearOutput();
            Log($"--- Play embutido: {Path.GetFileName(scenePath)} ---");

            // Por último: assim que isto vira true o viewport começa a chamar Initialize/Tick,
            // e tudo acima já tem que estar no lugar.
            IsPlayingEmbedded = true;
            Status = $"Rodando no editor — cena: {Path.GetFileName(scenePath)}";
        }
        catch (Exception ex)
        {
            Status = $"Erro ao iniciar o Play embutido: {ex.Message}";
            StatusDetail = ex.ToString();
        }
        finally
        {
            IsPreparingEmbeddedPlay = false;
        }
    }

    /// <summary>
    /// Ajusta o jogo carregado pra rodar hospedado: cena inicial, overlay e — o detalhe que
    /// quebra tudo se faltar — de onde vêm os assets.
    /// </summary>
    private void ConfigureGame(GameHost host, string assemblyPath, string scenePath)
    {
        var game = host.Game!;

        var args = new List<string> { "--scene", scenePath };

        if (DebugOverlayOnPlay)
        {
            args.Add("--debug");
            args.Add("--debug-font");
            args.Add(_settings?.EffectiveUiFont ?? "fonts/DejaVuSans.ttf");
        }

        game.ParseArgs([.. args]);

        // Sem isto o jogo procuraria os assets a partir do executável do EDITOR: rodando como
        // processo próprio o padrão do FileAssetSource (AppContext.BaseDirectory) cai na pasta
        // de saída do jogo, mas hospedado essa pasta é a do editor, e nada seria encontrado.
        // A raiz certa é onde o build do jogo colocou o assembly.
        string outputDir = Path.GetDirectoryName(assemblyPath) ?? "";
        game.AssetSource = new Aurora.Runtime.Assets.FileAssetSource(Path.Combine(outputDir, "Assets"));

        // Sair, pro jogo hospedado, é encerrar o modo Play — não fechar o editor.
        game.ExitHandler = () => StopEmbedded("O jogo pediu pra sair.");
    }

    /// <summary>Encerra o Play embutido. Idempotente.</summary>
    public void StopEmbedded(string? reason = null)
    {
        if (_gameHost is null)
        {
            IsPlayingEmbedded = false;
            return;
        }

        // O viewport chama GameHost.Stop com o contexto de GL vivo; aqui só se descarta o que
        // sobrou. Se ninguém estiver ouvindo (viewport ainda não montado), o Stop abaixo cobre.
        EmbeddedStopRequested?.Invoke();

        _gameHost.Stop(shutdownGame: false);
        _gameHost.Dispose();
        _gameHost = null;

        IsPlayingEmbedded = false;
        Status = reason ?? "Play encerrado.";
        Log($"--- Play embutido encerrado{(reason is null ? "" : $": {reason}")} ---");
    }

    /// <summary>O viewport avisa que o jogo estourou. Uma exceção que escapa do código do
    /// usuário derruba o modo Play, nunca o editor.</summary>
    internal void OnEmbeddedGameFaulted(string message)
    {
        StopEmbedded();
        Status = $"O jogo parou: {message}";
        Log($"--- jogo parou: {message} ---");
    }

    private bool _isBuilding;
    public bool IsBuilding
    {
        get => _isBuilding;
        private set
        {
            if (Set(ref _isBuilding, value))
                Raise(nameof(CanBuild));
        }
    }

    public bool CanBuild => _document is not null && !string.IsNullOrWhiteSpace(_settings?.GameProject) && !IsBuilding;

    /// <summary>
    /// Publica o jogo self-contained (Release) pra pasta escolhida — dotnet publish por
    /// trás, mesma engrenagem que Play() já usa pra achar o projeto. Não builda plataforma
    /// diferente da que está rodando o editor (self-contained pro RID atual).
    /// </summary>
    public async Task<bool> BuildGameAsync(string outputDir)
    {
        if (_document is null || string.IsNullOrWhiteSpace(_settings?.GameProject))
        {
            Status = "Configure o caminho do projeto (Inspector → PROJETO) antes de buildar.";
            return false;
        }

        string project = _settings!.GameProject!.Trim();
        if (project.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            Status = "PROJETO aponta pra um .exe — build precisa do .csproj (ou pasta) do jogo.";
            return false;
        }

        SaveScene();
        IsBuilding = true;
        string rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        Status = $"Buildando ({rid}, Release)... pode levar um tempo na primeira vez.";

        try
        {
            var psi = new ProcessStartInfo("dotnet",
                $"publish \"{project}\" -c Release -r {rid} --self-contained true -o \"{outputDir}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Não consegui iniciar o dotnet publish.");

            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                Status = $"Build falhou (código {process.ExitCode}): {GameScriptDiscovery.FirstErrorLine(stdout, stderr)}";
                StatusDetail = GameScriptDiscovery.CombineLog(stdout, stderr);
                return false;
            }

            Status = $"Build concluído: {outputDir}";
            StatusDetail = null;
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Erro ao buildar: {ex.Message}";
            StatusDetail = ex.ToString();
            return false;
        }
        finally
        {
            IsBuilding = false;
        }
    }

    private bool _isExportingAndroid;
    public bool IsExportingAndroid
    {
        get => _isExportingAndroid;
        private set
        {
            if (Set(ref _isExportingAndroid, value))
                Raise(nameof(CanExportAndroid));
        }
    }

    public bool CanExportAndroid => _document is not null && !string.IsNullOrWhiteSpace(_settings?.GameProject) && !IsExportingAndroid;

    /// <summary>
    /// Gera um segundo projeto (.csproj net10.0-android + MainActivity + AndroidAssetSource)
    /// a partir do jogo desktop atual e builda em Release — mesmo padrão testado manualmente
    /// em <c>docs/GUIA-ANDROID.md</c>, agora automático. Retorna o .apk gerado (ou null se falhou).
    /// </summary>
    public async Task<string?> ExportAndroidAsync(string androidProjectDir, string applicationId, string displayName)
    {
        if (_document is null || string.IsNullOrWhiteSpace(_settings?.GameProject))
        {
            Status = "Configure o caminho do projeto (Inspector → PROJETO) antes de exportar.";
            return null;
        }

        string project = _settings!.GameProject!.Trim();
        if (project.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            Status = "PROJETO aponta pra um .exe — exportar Android precisa do .csproj (ou pasta) do jogo.";
            return null;
        }

        string? gameCsproj = Directory.Exists(project)
            ? Directory.EnumerateFiles(project, "*.csproj").FirstOrDefault()
            : project;
        if (gameCsproj is null)
        {
            Status = $"Não achei nenhum .csproj em '{project}'.";
            return null;
        }

        // A pasta Android tem que ser separada da pasta do jogo: exportar em cima dela
        // sobrescreveria o .csproj/Program.cs do desktop com o gerado pro Android.
        string gameDirFull = Path.GetFullPath(Path.GetDirectoryName(gameCsproj)!)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string androidDirFull = Path.GetFullPath(androidProjectDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(gameDirFull, androidDirFull, StringComparison.OrdinalIgnoreCase)
            || androidDirFull.StartsWith(gameDirFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            Status = "A pasta do projeto Android não pode ser a mesma (ou dentro) da pasta do jogo desktop — escolha uma pasta separada (ex: ao lado, com sufixo .Android).";
            return null;
        }

        SaveScene();
        IsExportingAndroid = true;
        Status = "Gerando projeto Android...";

        try
        {
            Models.AndroidExporter.Result result;
            try
            {
                result = Models.AndroidExporter.Export(gameCsproj, androidProjectDir, applicationId, displayName, AndroidOrientation);
            }
            catch (Exception ex)
            {
                Status = $"Falha ao gerar projeto Android: {ex.Message}";
                StatusDetail = ex.ToString();
                return null;
            }

            string warningSuffix = result.Warnings.Count > 0 ? $" ({result.Warnings.Count} aviso(s), veja o log)" : "";
            Status = $"Projeto Android gerado. Buildando (pode levar minutos na 1ª vez){warningSuffix}...";
            foreach (string warning in result.Warnings)
                Console.WriteLine($"[export-android] aviso: {warning}");

            var psi = new ProcessStartInfo("dotnet", $"build \"{result.CsprojPath}\" -c Release")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Não consegui iniciar o dotnet build.");

            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                Status = $"Build Android falhou (código {process.ExitCode}): {GameScriptDiscovery.FirstErrorLine(stdout, stderr)}";
                StatusDetail = GameScriptDiscovery.CombineLog(stdout, stderr);
                return null;
            }

            string? apk = Directory.Exists(androidProjectDir)
                ? Directory.EnumerateFiles(androidProjectDir, "*-Signed.apk", SearchOption.AllDirectories).FirstOrDefault()
                : null;

            Status = apk is not null
                ? $"APK gerado: {apk}"
                : $"Build concluído mas não achei o .apk em {androidProjectDir}.";
            StatusDetail = null;
            return apk;
        }
        catch (Exception ex)
        {
            Status = $"Erro ao exportar Android: {ex.Message}";
            StatusDetail = ex.ToString();
            return null;
        }
        finally
        {
            IsExportingAndroid = false;
        }
    }

    public void ChangeAssetsRoot(string absolutePath)
    {
        if (_document is null)
            return;

        _document.SetAssetsRoot(absolutePath);
        Raise(nameof(AssetsRootDisplay));
        ReloadAssets();
        OnEdited("assetsroot");
        Status = $"Assets root: {_document.AssetsRoot}";
    }

    public void NewScene(string filePath)
    {
        _document = SceneDocument.New(filePath);
        _settings = ProjectSettings.Find(filePath);
        RememberLastScene(filePath);

        RebuildEntities();
        SelectedEntity = null;

        _undoStack.Clear();
        _redoStack.Clear();
        _lastSnapshot = _document.Root.ToJsonString();
        _lastEditTag = null;

        IsDirty = false;
        Status = $"Nova cena: {_document.SceneName} | assets: {_document.AssetsRoot}";
        Raise(nameof(Title));
        Raise(nameof(DocumentName));
        Raise(nameof(HasDocument));
        Raise(nameof(IsUiScreenDocument));
        Raise(nameof(AssetsRootDisplay));
        Raise(nameof(CanPlay));
        Raise(nameof(CanPlayEmbedded));
        Raise(nameof(CanBuild));
        Raise(nameof(GameProjectPath));
        Raise(nameof(CanPaste));
        Raise(nameof(HasSelectedEntity));
        RaiseUndoState();
        ReloadAssets();
        ReloadSceneFiles();
        ReloadPrefabs();
        ReloadUiScreens();
        ReloadScripts();
        RefreshScriptCatalog();
        SceneEdited?.Invoke();
    }

    public void OpenScene(string path)
    {
        _document = SceneDocument.Load(path);
        _settings = ProjectSettings.Find(path);
        RememberLastScene(path);

        RebuildEntities();
        SelectedEntity = Entities.FirstOrDefault();

        _undoStack.Clear();
        _redoStack.Clear();
        _lastSnapshot = _document.Root.ToJsonString();
        _lastEditTag = null;

        IsDirty = false;
        Status = $"{_document.SceneName} — {Entities.Count} entidades | assets: {_document.AssetsRoot}";
        Raise(nameof(Title));
        Raise(nameof(DocumentName));
        Raise(nameof(HasDocument));
        Raise(nameof(IsUiScreenDocument));
        Raise(nameof(AssetsRootDisplay));
        Raise(nameof(CanPlay));
        Raise(nameof(CanPlayEmbedded));
        Raise(nameof(CanBuild));
        Raise(nameof(GameProjectPath));
        Raise(nameof(CanPaste));
        Raise(nameof(HasSelectedEntity));
        RaiseUndoState();
        ReloadAssets();
        ReloadSceneFiles();
        ReloadPrefabs();
        ReloadUiScreens();
        ReloadScripts();
        RefreshScriptCatalog();
        SceneEdited?.Invoke();
    }

    /// <summary>Grava a cena atual em aurora.project.json (só se o arquivo já existir de
    /// verdade — evita criar aurora.project.json solto ao abrir uma cena avulsa fora de
    /// projeto nenhum). "Abrir Projeto…" usa isso pra reabrir de onde parou.</summary>
    private void RememberLastScene(string scenePath)
    {
        if (_settings is null || !File.Exists(_settings.FilePath))
            return;

        string projectDir = Path.GetDirectoryName(_settings.FilePath)!;
        _settings.LastScene = Path.GetRelativePath(projectDir, scenePath).Replace('\\', '/');
        try { _settings.Save(); } catch { /* sem permissão de escrita — ignora */ }
    }

    /// <summary>Abre um projeto pela pasta raiz (a que contém aurora.project.json): reabre a
    /// última cena editada, ou cai para Assets/scenes/main.json, ou a primeira cena que achar.</summary>
    public void OpenProject(string projectDir)
    {
        string settingsPath = Path.Combine(projectDir, "aurora.project.json");
        if (!File.Exists(settingsPath))
            throw new InvalidOperationException($"'{projectDir}' não tem aurora.project.json — não é uma pasta de projeto Aurora.");

        var settings = ProjectSettings.Find(settingsPath);

        string? scenePath = null;
        if (settings.LastScene is { Length: > 0 } last)
        {
            string candidate = Path.GetFullPath(Path.Combine(projectDir, last));
            if (File.Exists(candidate))
                scenePath = candidate;
        }

        if (scenePath is null)
        {
            string scenesDir = Path.Combine(projectDir, "Assets", "scenes");
            string mainCandidate = Path.Combine(scenesDir, "main.json");
            scenePath = File.Exists(mainCandidate) ? mainCandidate
                : Directory.Exists(scenesDir)
                    ? Directory.EnumerateFiles(scenesDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
                    : null;
        }

        if (scenePath is null)
            throw new InvalidOperationException($"Nenhuma cena encontrada em '{projectDir}\\Assets\\scenes'.");

        OpenScene(scenePath);
    }

    private void RebuildEntities()
    {
        Entities.Clear();
        if (_document is null)
        {
            RebuildEventEntities();
            return;
        }

        foreach (var objectNode in _document.Objects.OfType<System.Text.Json.Nodes.JsonObject>())
        {
            var entity = new EntityViewModel(objectNode, this);
            entity.Edited += OnEdited;
            Entities.Add(entity);
        }
        ApplyEntityFilter();
        RebuildEventEntities();
    }

    private void RebuildEventEntities()
    {
        EventEntities.Clear();
        foreach (var e in Entities.Where(e => e.HasEventTrigger))
            EventEntities.Add(e);
        Raise(nameof(HasEventEntities));
    }

    private static readonly string[] TextureExtensions = [".png", ".jpg", ".jpeg"];

    /// <summary>Varre a raiz de assets por texturas (para o asset browser).</summary>
    public void ReloadAssets()
    {
        Assets.Clear();
        if (_document is null || !Directory.Exists(_document.AssetsRoot))
            return;

        var files = Directory.EnumerateFiles(_document.AssetsRoot, "*", SearchOption.AllDirectories)
            .Where(f => TextureExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            string relative = Path.GetRelativePath(_document.AssetsRoot, file).Replace('\\', '/');
            Assets.Add(new AssetViewModel(_document.AssetsRoot, relative));
        }
    }

    /// <summary>Subpasta de destino por extensão — mesma convenção que os samples já usam
    /// (Assets/sprites, Assets/sounds, Assets/fonts). Extensão fora da lista vai pra raiz.</summary>
    private static readonly Dictionary<string, string> ImportSubfolders = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "sprites", [".jpg"] = "sprites", [".jpeg"] = "sprites",
        [".wav"] = "sounds", [".ogg"] = "sounds",
        [".ttf"] = "fonts",
    };

    /// <summary>
    /// Copia arquivos externos pra dentro da pasta de assets do projeto (subpasta por tipo)
    /// e recarrega o painel ASSETS — sem precisar sair pro Explorer e copiar na mão.
    /// Não sobrescreve: em conflito de nome, renomeia com sufixo numérico.
    /// </summary>
    public void ImportAssets(IEnumerable<string> sourcePaths)
    {
        if (_document is null)
            return;

        int imported = 0;
        foreach (string source in sourcePaths)
        {
            CopyIntoAssets(source);
            imported++;
        }

        ReloadAssets();
        Status = imported == 1 ? "1 asset importado." : $"{imported} assets importados.";
    }

    /// <summary>
    /// Preenchido pela janela (ela é quem tem o seletor de arquivo do sistema): abre o diálogo,
    /// traz o arquivo pra dentro do projeto se ele veio de fora e devolve o caminho relativo à
    /// raiz de assets. Null = cancelou. É o que os campos de textura do inspector chamam.
    /// </summary>
    public Func<Task<string?>>? PickTextureFromDisk { get; set; }

    /// <summary>
    /// Caminho relativo (com '/') de um arquivo qualquer do disco, copiando pra pasta de assets
    /// quando ele está fora dela — escolher um PNG que mora em Downloads tem que virar um asset
    /// do projeto, senão o jogo não acha a textura ao rodar fora do editor.
    /// </summary>
    public string? EnsureAssetInProject(string absolutePath)
    {
        if (_document is null || !File.Exists(absolutePath))
            return null;

        string root = Path.GetFullPath(_document.AssetsRoot);
        string full = Path.GetFullPath(absolutePath);

        if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(root, full).Replace('\\', '/');

        string copied = CopyIntoAssets(full);
        ReloadAssets();
        Status = $"'{Path.GetFileName(full)}' importado para os assets do projeto.";
        return Path.GetRelativePath(root, copied).Replace('\\', '/');
    }

    /// <summary>Copia um arquivo pra subpasta de assets do tipo dele e devolve o caminho final.</summary>
    private string CopyIntoAssets(string source)
    {
        string destDir = ImportSubfolders.TryGetValue(Path.GetExtension(source), out var subfolder)
            ? Path.Combine(_document!.AssetsRoot, subfolder)
            : _document!.AssetsRoot;

        Directory.CreateDirectory(destDir);
        string destPath = UniquePath(Path.Combine(destDir, Path.GetFileName(source)));
        File.Copy(source, destPath);
        return destPath;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        string dir = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);

        for (int i = 1; ; i++)
        {
            string candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    /// <summary>Varre a raiz de assets por cenas .json (para o painel CENAS). Cena tem "Objects"
    /// na raiz sem marca "UI" — tela de UI (mesma pasta) tem "Objects" + "UI":true; prefab tem
    /// "Components" na raiz sem "Objects". As três nunca se confundem.</summary>
    public void ReloadSceneFiles()
    {
        SceneFiles.Clear();
        if (_document is null || !Directory.Exists(_document.AssetsRoot))
            return;

        var files = Directory.EnumerateFiles(_document.AssetsRoot, "*.json", SearchOption.AllDirectories)
            .Where(f => LooksLikeScene(f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            string relative = Path.GetRelativePath(_document.AssetsRoot, file).Replace('\\', '/');
            SceneFiles.Add(new SceneFileViewModel(file, relative)
            {
                IsCurrent = string.Equals(Path.GetFullPath(file), Path.GetFullPath(_document.FilePath),
                    StringComparison.OrdinalIgnoreCase),
            });
        }
    }

    /// <summary>Varre a raiz de assets por telas de UI .json (para o painel TELAS UI). Mesmo
    /// formato de cena (Objects/Components), com componentes UiText/UiImage/UiBar/UiPanel em
    /// pixels de tela — persistem entre trocas de cena no runtime (ver Aurora.Runtime.UI.UIManager).</summary>
    public void ReloadUiScreens()
    {
        UiScreens.Clear();
        if (_document is null || !Directory.Exists(_document.AssetsRoot))
            return;

        var files = Directory.EnumerateFiles(_document.AssetsRoot, "*.json", SearchOption.AllDirectories)
            .Where(f => LooksLikeUiScreen(f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            string relative = Path.GetRelativePath(_document.AssetsRoot, file).Replace('\\', '/');
            UiScreens.Add(new SceneFileViewModel(file, relative)
            {
                IsCurrent = string.Equals(Path.GetFullPath(file), Path.GetFullPath(_document.FilePath),
                    StringComparison.OrdinalIgnoreCase),
            });
        }
    }

    /// <summary>Varre a raiz de assets por prefabs .json (para o painel PREFABS). Prefab tem
    /// "Components" na raiz sem "Objects" — o oposto de uma cena.</summary>
    public void ReloadPrefabs()
    {
        Prefabs.Clear();
        if (_document is null || !Directory.Exists(_document.AssetsRoot))
            return;

        var files = Directory.EnumerateFiles(_document.AssetsRoot, "*.json", SearchOption.AllDirectories)
            .Where(f => LooksLikePrefab(f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            string relative = Path.GetRelativePath(_document.AssetsRoot, file).Replace('\\', '/');
            Prefabs.Add(new PrefabFileViewModel(file, relative));
        }
    }

    /// <summary>Pasta "Scripts" do projeto aberto (irmã de "Assets", criada por
    /// GameProjectScaffolder pra todo projeto novo) — raiz do painel SCRIPTS e destino sugerido
    /// pro "+ Novo…". Deriva da pasta de aurora.project.json, não de GameProjectPath, porque
    /// GameProjectPath pode apontar pra um .exe dentro de bin/Debug em vez da raiz do projeto.</summary>
    public string ScriptsDirPath =>
        _settings is { FilePath.Length: > 0 }
            ? Path.Combine(Path.GetDirectoryName(_settings.FilePath)!, "Scripts")
            : "";

    /// <summary>O que está selecionado em cada aba do painel de projeto — sem isso não há como
    /// saber o que o botão de excluir deve apagar.</summary>
    public SceneFileViewModel? SelectedSceneFile { get => _selectedSceneFile; set { _selectedSceneFile = value; Raise(); } }
    public SceneFileViewModel? SelectedUiScreen { get => _selectedUiScreen; set { _selectedUiScreen = value; Raise(); } }
    public PrefabFileViewModel? SelectedPrefab { get => _selectedPrefab; set { _selectedPrefab = value; Raise(); } }
    public ScriptFileViewModel? SelectedScript { get => _selectedScript; set { _selectedScript = value; Raise(); } }
    public AssetViewModel? SelectedAsset { get => _selectedAsset; set { _selectedAsset = value; Raise(); } }

    private SceneFileViewModel? _selectedSceneFile;
    private SceneFileViewModel? _selectedUiScreen;
    private PrefabFileViewModel? _selectedPrefab;
    private ScriptFileViewModel? _selectedScript;
    private AssetViewModel? _selectedAsset;

    /// <summary>
    /// Apaga um arquivo do projeto e recarrega a lista correspondente. Devolve o que aconteceu
    /// pra janela mostrar na barra de status.
    ///
    /// <para>Não existe desfazer: o Ctrl+Z do editor volta edição de cena, não arquivo apagado.
    /// Quem chama tem que ter confirmado antes.</para>
    /// </summary>
    public string DeleteProjectFile(string fullPath, Action reload)
    {
        if (string.IsNullOrEmpty(fullPath))
            return "Nada selecionado.";

        string name = Path.GetFileName(fullPath);

        try
        {
            if (!File.Exists(fullPath))
            {
                reload();
                return $"'{name}' já não existia — lista atualizada.";
            }

            File.Delete(fullPath);
            reload();
            return $"Excluído: {name}";
        }
        catch (Exception ex)
        {
            // Arquivo aberto noutro programa, permissão negada, disco somente-leitura.
            return $"Não deu pra excluir '{name}': {ex.Message}";
        }
    }

    /// <summary>Caminho do banco de itens do projeto (Assets/database/items.json).</summary>
    public string ItemDatabasePath =>
        _document is null ? "" : Path.Combine(_document.AssetsRoot, "database", "items.json");

    /// <summary>Caminho das tabelas de spawn do projeto (Assets/database/spawns.json).</summary>
    public string SpawnTablePath =>
        _document is null ? "" : Path.Combine(_document.AssetsRoot, "database", "spawns.json");

    /// <summary>Eventos comuns do projeto (Assets/database/common_events.json).</summary>
    public string CommonEventPath =>
        _document is null ? "" : Path.Combine(_document.AssetsRoot, "database", "common_events.json");

    /// <summary>Efeitos de status do projeto (Assets/database/status.json).</summary>
    public string StatusPath =>
        _document is null ? "" : Path.Combine(_document.AssetsRoot, "database", "status.json");

    /// <summary>Listas de categorias do projeto (Assets/database/types.json).</summary>
    public string TypesPath =>
        _document is null ? "" : Path.Combine(_document.AssetsRoot, "database", "types.json");

    /// <summary>Textos de interface do projeto (Assets/database/terms.json).</summary>
    public string TermsPath =>
        _document is null ? "" : Path.Combine(_document.AssetsRoot, "database", "terms.json");

    // ---------- Sugestões pros campos que apontam pra algo do projeto ----------
    //
    // Tudo aqui é calculado na hora, não cacheado: o inspector fica aberto enquanto entidades
    // nascem, prefabs são salvos e telas são criadas — lista congelada envelheceria na tela.

    /// <summary>Nomes das entidades da cena aberta — pros campos Follow/TargetName/TargetPrefix.</summary>
    public IEnumerable<string> EntityNames =>
        Entities.Select(e => e.Name).Where(n => n.Length > 0).Distinct().OrderBy(n => n);

    /// <summary>
    /// Etiquetas já usadas na cena aberta, sem o <c>#</c> — pro campo Tags.Value e pros campos de
    /// alvo. Etiqueta não tem cadastro em lugar nenhum: ela existe por estar escrita em alguma
    /// entidade, então a lista é varrida das próprias entidades. Prefabs ficam de fora porque a
    /// etiqueta interessa é no que está na cena.
    /// </summary>
    public IEnumerable<string> TagNames =>
        Entities.SelectMany(e => e.Components)
            .Where(c => c.Type == "Tags")
            .Select(c => c.GetString("Value") ?? "")
            .SelectMany(v => v.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries
                                                    | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t);

    /// <summary>Prefabs do projeto MAIS os ids das tabelas de spawn: os dois valem no mesmo campo,
    /// então os dois têm que aparecer na mesma lista.</summary>
    public IEnumerable<string> PrefabOrTableNames =>
        Prefabs.Select(p => p.RelativePath).Concat(SpawnTableIds).Distinct();

    /// <summary>Ids das tabelas de spawn cadastradas no banco.</summary>
    public IEnumerable<string> SpawnTableIds => ReadIds(SpawnTablePath, "Tables");

    /// <summary>Ids dos eventos comuns cadastrados — pra ação CallEvent.</summary>
    public IEnumerable<string> CommonEventIds => ReadIds(CommonEventPath, "Events");

    /// <summary>Ids dos efeitos de status cadastrados — pras ações AddStatus/RemoveStatus e pro
    /// campo Initial do componente Status.</summary>
    public IEnumerable<string> StatusIds => ReadIds(StatusPath, "Status");

    /// <summary>
    /// Valores da lista ItemTypes do banco de tipos — pro campo Tipo do item. Lista vazia (ou sem
    /// arquivo) significa "sem controle": o campo continua aceitando texto livre.
    /// </summary>
    public IEnumerable<string> ItemTypeValues => TypeValues("ItemTypes");

    /// <summary>Valores de uma lista do banco de tipos, lidos na hora do arquivo.</summary>
    public IEnumerable<string> TypeValues(string listId)
    {
        string path = TypesPath;
        if (path.Length == 0 || !File.Exists(path))
            return [];

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("Types", out var lists))
                return [];

            foreach (var list in lists.EnumerateArray())
            {
                if (!list.TryGetProperty("Id", out var id)
                    || !string.Equals(id.GetString(), listId, StringComparison.OrdinalIgnoreCase)
                    || !list.TryGetProperty("Values", out var values))
                    continue;

                return values.EnumerateArray()
                    .Select(v => v.GetString() ?? "")
                    .Where(v => v.Length > 0)
                    .ToList();
            }
        }
        catch
        {
            // Arquivo quebrado é problema da aba Tipos, que mostra o erro. Aqui só não sugere nada.
        }

        return [];
    }

    /// <summary>Ids das telas de UI (nome do arquivo sem extensão) — é o que ShowUI/HideUI e os
    /// campos de joystick/botão esperam.</summary>
    public IEnumerable<string> UiScreenIds =>
        UiScreens.Select(s => Path.GetFileNameWithoutExtension(s.Name)).Distinct().OrderBy(n => n);

    /// <summary>Nomes dos elementos dentro das telas de UI — pros campos que apontam pra um
    /// joystick ou botão específico.</summary>
    public IEnumerable<string> UiElementNames
    {
        get
        {
            var names = new List<string>();

            foreach (var screen in UiScreens)
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(screen.FullPath));
                    if (!doc.RootElement.TryGetProperty("Objects", out var objects))
                        continue;

                    foreach (var obj in objects.EnumerateArray())
                    {
                        if (obj.TryGetProperty("Name", out var name)
                            && name.GetString() is { Length: > 0 } text)
                            names.Add(text);
                    }
                }
                catch
                {
                    // Tela malformada não pode derrubar o inspector — só não sugere nada dela.
                }
            }

            return names.Distinct().OrderBy(n => n);
        }
    }

    /// <summary>Assets de áudio, pros campos de som.</summary>
    public IEnumerable<string> SoundAssets =>
        Assets.Select(a => a.RelativePath)
            .Where(p => p.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                     || p.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
                     || p.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Teclas comuns, pelos nomes do enum Silk.NET.Input.Key que o runtime usa. Sugestão e não
    /// lista fechada de propósito: cobre o que 99% dos jogos ligam sem impedir quem precisa de
    /// uma tecla exótica de digitar o nome dela.
    /// </summary>
    public static IEnumerable<string> KeyNames { get; } =
    [
        // Mouse primeiro: "MouseLeft" e o toque na tela no Android (o InputManager dobra o
        // ponteiro da MainActivity no mesmo caminho do mouse), entao e a escolha certa pra
        // interacao que precisa funcionar nas duas plataformas.
        "MouseLeft", "MouseRight", "MouseMiddle",
        "GamepadA", "GamepadB", "GamepadX", "GamepadY",
        "Space", "Enter", "Escape", "Tab", "Backspace", "Delete",
        "Left", "Right", "Up", "Down",
        "ShiftLeft", "ShiftRight", "ControlLeft", "ControlRight", "AltLeft", "AltRight",
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "Number0", "Number1", "Number2", "Number3", "Number4",
        "Number5", "Number6", "Number7", "Number8", "Number9",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
    ];

    /// <summary>Lê os "Id" de um array nomeado num arquivo de banco. Banco ausente ou quebrado
    /// devolve lista vazia — sugestão que falha não pode impedir de editar a cena.</summary>
    private static IEnumerable<string> ReadIds(string path, string arrayName)
    {
        if (path.Length == 0 || !File.Exists(path))
            return [];

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty(arrayName, out var items))
                return [];

            return items.EnumerateArray()
                .Select(i => i.TryGetProperty("Id", out var id) ? id.GetString() ?? "" : "")
                .Where(id => id.Length > 0)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Ids do banco de itens, pros seletores das ações UseItem/AddItem. Lido do arquivo
    /// a cada consulta em vez de cacheado: o banco é pequeno e pode ser editado por fora.</summary>
    public IEnumerable<string> ItemIds
    {
        get
        {
            string path = ItemDatabasePath;
            if (path.Length == 0 || !File.Exists(path))
                return [];

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("Items", out var items))
                    return [];

                return items.EnumerateArray()
                    .Select(i => i.TryGetProperty("Id", out var id) ? id.GetString() ?? "" : "")
                    .Where(id => id.Length > 0)
                    .ToList();
            }
            catch
            {
                return [];   // banco malformado não pode derrubar o inspector
            }
        }
    }

    /// <summary>Varre a pasta Scripts do projeto por arquivos .cs (para o painel SCRIPTS).</summary>
    public void ReloadScripts()
    {
        Scripts.Clear();
        string dir = ScriptsDirPath;
        if (dir.Length == 0 || !Directory.Exists(dir))
            return;

        var files = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            string relative = Path.GetRelativePath(dir, file).Replace('\\', '/');
            Scripts.Add(new ScriptFileViewModel(file, relative));
        }
    }

    /// <summary>Código inicial do editor interno pro template selecionado, já com o namespace
    /// do projeto e o nome de classe pedido — o arquivo em si só nasce no Salvar.</summary>
    public string BuildScriptTemplateSource(string className)
        => ScriptTemplates.Build(SelectedScriptTemplate, ResolveScriptNamespace(),
            className.Length > 0 ? className : SelectedScriptTemplate.DefaultClassName);

    /// <summary>Grava o texto do editor interno em disco e já registra os [SceneScript] dele no
    /// catálogo por leitura de texto (<see cref="ScriptSourceParser"/>) — é isso que dispensa o
    /// ciclo "salva fora → ↻ → espera buildar" antes de anexar o script numa entidade. O "↻"
    /// continua existindo e corrige o catálogo com a verdade do assembly compilado.</summary>
    public void SaveScriptSource(string absolutePath, string source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllText(absolutePath, source);

        ReloadScripts();
        var parsed = ScriptSourceParser.Parse(source);
        RegisterParsedScripts(parsed);

        string names = parsed.Count > 0 ? string.Join(", ", parsed.Select(s => s.Name)) : "nenhum [SceneScript]";
        Status = $"Script salvo: {Path.GetFileName(absolutePath)} ({names})";
        StatusDetail = null;
    }

    /// <summary>Insere/atualiza scripts no catálogo <see cref="CustomScripts"/> por nome.
    /// Substitui em vez de duplicar pra um campo novo aparecer no inspector no mesmo instante
    /// em que foi salvo.</summary>
    public void RegisterParsedScripts(IReadOnlyList<GameScriptDiscovery.ScriptInfo> scripts)
    {
        foreach (var script in scripts)
        {
            int existing = -1;
            for (int i = 0; i < CustomScripts.Count; i++)
            {
                if (CustomScripts[i].Name == script.Name)
                {
                    existing = i;
                    break;
                }
            }

            if (existing >= 0)
                CustomScripts[existing] = script;
            else
                CustomScripts.Add(script);
        }
    }

    public sealed record CompileResult(bool Success, string Summary, string? Detail);

    /// <summary>Roda <c>dotnet build</c> no projeto do jogo só pra dizer se o script compila —
    /// usado pelo botão "Verificar" do editor de scripts, que precisa mostrar o erro do
    /// compilador dentro da própria janela (o editor não tem console).</summary>
    public async Task<CompileResult> CompileGameProjectAsync()
    {
        string project = _settings?.GameProject?.Trim() ?? "";
        if (project.Length == 0)
            return new CompileResult(false, "Configure o caminho do projeto (Inspector → PROJETO) antes de verificar.", null);
        if (project.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return new CompileResult(false, "PROJETO aponta pra um .exe — verificar precisa do .csproj (ou pasta) do jogo.", null);

        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("build");
            psi.ArgumentList.Add(project);

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Não consegui iniciar o dotnet build.");

            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return process.ExitCode == 0
                ? new CompileResult(true, "Compilou sem erros.", null)
                : new CompileResult(false, GameScriptDiscovery.FirstErrorLine(stdout, stderr),
                    GameScriptDiscovery.CombineLog(stdout, stderr));
        }
        catch (Exception ex)
        {
            return new CompileResult(false, ex.Message, ex.ToString());
        }
    }

    /// <summary>Anexa um script já registrado à entidade selecionada (mesmo caminho do
    /// "+ Add Componente", inclusive campos pré-preenchidos com os defaults). False quando não
    /// há entidade selecionada — quem chama avisa na UI.</summary>
    public bool AttachScriptToSelectedEntity(string scriptName)
    {
        if (SelectedEntity is not { } entity || scriptName.Length == 0)
            return false;

        entity.NewComponentType = scriptName;
        entity.AddComponent();
        Status = $"{scriptName} anexado a {entity.Name}.";
        return true;
    }

    /// <summary>Nome do namespace pros templates de "Novo Script": nome do .csproj do jogo se
    /// conhecido, senão o nome da pasta do projeto — só estética (Game.AutoRegisterScripts acha
    /// [SceneScript] por reflection, não importa o namespace nem precisa de "using").</summary>
    private string ResolveScriptNamespace()
    {
        string? csproj = _settings?.GameProject;
        if (!string.IsNullOrWhiteSpace(csproj) && csproj.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return GameProjectScaffolder.ToIdentifier(Path.GetFileNameWithoutExtension(csproj));

        string projectDir = _settings is { FilePath.Length: > 0 } ? Path.GetDirectoryName(_settings.FilePath)! : "";
        string dirName = projectDir.Length > 0 ? Path.GetFileName(projectDir.TrimEnd('\\', '/')) : "";
        return dirName.Length > 0 ? GameProjectScaffolder.ToIdentifier(dirName) : "Game";
    }

    private static bool LooksLikeScene(string jsonPath)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jsonPath));
            return doc.RootElement.TryGetProperty("Objects", out _) && !IsUiMarked(doc.RootElement);
        }
        catch { return false; }
    }

    private static bool LooksLikeUiScreen(string jsonPath)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jsonPath));
            return doc.RootElement.TryGetProperty("Objects", out _) && IsUiMarked(doc.RootElement);
        }
        catch { return false; }
    }

    private static bool IsUiMarked(System.Text.Json.JsonElement root)
        => root.TryGetProperty("UI", out var ui) && ui.ValueKind == System.Text.Json.JsonValueKind.True;

    private static bool LooksLikePrefab(string jsonPath)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jsonPath));
            return doc.RootElement.TryGetProperty("Components", out _)
                && !doc.RootElement.TryGetProperty("Objects", out _);
        }
        catch { return false; }
    }

    /// <summary>Cria uma tela de UI vazia (mesmo formato de cena, com a marca "UI":true) e
    /// já abre pra edição — reusa toda a maquinaria de OpenScene (hierarquia, inspector,
    /// undo/redo funcionam sem nenhum código a mais).</summary>
    public void NewUiScreen(string filePath)
    {
        var root = new System.Text.Json.Nodes.JsonObject
        {
            ["Scene"] = Path.GetFileNameWithoutExtension(filePath),
            ["UI"] = true,
            ["Objects"] = new System.Text.Json.Nodes.JsonArray(),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath,
            root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        OpenScene(filePath);
    }

    /// <summary>Troca de cena a partir do painel CENAS. Salva a atual antes, se suja (sem
    /// diálogo de confirmação — mesmo comportamento silencioso que Play() já usa).</summary>
    public void OpenSceneFile(SceneFileViewModel file)
    {
        if (file.IsCurrent)
            return;

        if (IsDirty)
            SaveScene();

        OpenScene(file.FullPath);
    }

    /// <summary>Aplica a textura no SpriteRenderer (ou Tilemap, UiImage, UiButton) da entidade
    /// selecionada — os dois últimos porque numa tela de UI não existe SpriteRenderer nenhum pra
    /// receber a imagem.</summary>
    public void ApplyTextureToSelection(AssetViewModel asset)
    {
        var textureProperty = SelectedEntity?.Sprite?.Text("Texture")
            ?? SelectedEntity?.Tilemap?.Text("Texture")
            ?? SelectedEntity?.Component("UiImage")?.Text("Texture")
            ?? SelectedEntity?.Component("UiButton")?.Text("Texture");
        if (textureProperty is null)
        {
            Status = "Selecione uma entidade com SpriteRenderer, Tilemap, UiImage ou UiButton para aplicar a textura.";
            return;
        }

        textureProperty.Value = asset.RelativePath;
        Status = $"{asset.RelativePath} → {SelectedEntity!.Name}";
    }

    // ---- Paleta de tiles ----

    private int? _selectedTileIndex;
    private TileBrushViewModel? _selectedTileBrush;
    private string? _paletteSignature;

    public ObservableCollection<TileBrushViewModel> PaletteTiles { get; } = [];

    public bool HasTilePalette => PaletteTiles.Count > 0;

    /// <summary>Tile ativo para pintura no canvas. Null = modo seleção/movimento normal.</summary>
    public int? SelectedTileIndex
    {
        get => _selectedTileIndex;
        private set => Set(ref _selectedTileIndex, value);
    }

    /// <summary>Item selecionado na paleta (binding do ListBox).</summary>
    public TileBrushViewModel? SelectedTileBrush
    {
        get => _selectedTileBrush;
        set
        {
            if (Set(ref _selectedTileBrush, value))
                SelectedTileIndex = value?.Index;
        }
    }

    /// <summary>Sai do modo pintura (Escape).</summary>
    public void ClearTileBrush() => SelectedTileBrush = null;

    /// <summary>Monta a paleta a partir do tileset da entidade selecionada.</summary>
    private void RebuildTilePalette()
    {
        var map = SelectedEntity?.Tilemap;
        string? texture = map?.GetString("Texture");
        int tileWidth = (int)(map?.GetFloat("TileWidth", 16f) ?? 16);
        int tileHeight = (int)(map?.GetFloat("TileHeight", 16f) ?? 16);

        string? signature = map is null || texture is null
            ? null
            : $"{texture}|{tileWidth}|{tileHeight}";

        if (signature == _paletteSignature)
            return;

        _paletteSignature = signature;
        SelectedTileBrush = null;
        PaletteTiles.Clear();

        if (signature is not null && _document is not null && tileWidth > 0 && tileHeight > 0)
        {
            string fullPath = Path.Combine(_document.AssetsRoot, texture!);
            if (File.Exists(fullPath))
            {
                var bitmap = new Avalonia.Media.Imaging.Bitmap(fullPath);
                int columns = Math.Max(1, (int)bitmap.PixelSize.Width / tileWidth);
                int rows = Math.Max(1, (int)bitmap.PixelSize.Height / tileHeight);

                PaletteTiles.Add(new TileBrushViewModel(-1, null));

                for (int index = 0; index < columns * rows; index++)
                {
                    var source = new Avalonia.PixelRect(
                        index % columns * tileWidth, index / columns * tileHeight, tileWidth, tileHeight);
                    PaletteTiles.Add(new TileBrushViewModel(index,
                        new Avalonia.Media.Imaging.CroppedBitmap(bitmap, source)));
                }
            }
        }

        Raise(nameof(HasTilePalette));
    }

    public void SaveScene()
    {
        if (_document is null)
            return;

        _document.Save();
        IsDirty = false;
        Status = $"Salvo: {_document.FilePath}";
    }

    public void SaveSceneAs(string path)
    {
        if (_document is null)
            return;

        _document.Save(path);
        IsDirty = false;
        Raise(nameof(Title));
        Raise(nameof(DocumentName));
        Status = $"Salvo: {path}";
        ReloadSceneFiles();
        ReloadUiScreens();
    }

    /// <summary>
    /// Cria entidade com Transform + SpriteRenderer. Sem textura vira placeholder
    /// magenta no canvas; com textura (drop do asset browser) nasce nomeada pelo arquivo.
    /// </summary>
    public void CreateEntity(double x, double y, string? texturePath = null)
    {
        if (_document is null)
            return;

        string baseName = texturePath is null
            ? "Entidade"
            : char.ToUpperInvariant(Path.GetFileNameWithoutExtension(texturePath)[0])
              + Path.GetFileNameWithoutExtension(texturePath)[1..];

        var names = Entities.Select(e => e.Name).ToHashSet();
        string name = baseName;
        for (int number = 1; names.Contains(name); number++)
            name = $"{baseName}{number}";

        var sprite = new System.Text.Json.Nodes.JsonObject { ["Type"] = "SpriteRenderer" };
        if (texturePath is not null)
            sprite["Texture"] = texturePath;

        var node = new System.Text.Json.Nodes.JsonObject
        {
            ["Name"] = name,
            ["Components"] = new System.Text.Json.Nodes.JsonArray(
                new System.Text.Json.Nodes.JsonObject
                {
                    ["Type"] = "Transform",
                    ["X"] = (float)Math.Round(x),
                    ["Y"] = (float)Math.Round(y),
                },
                sprite),
        };

        _document.Objects.Add(node);

        var entity = new EntityViewModel(node, this);
        entity.Edited += OnEdited;
        Entities.Add(entity);
        SelectedEntity = entity;
        OnEdited($"create:{node.GetHashCode()}");
    }

    /// <summary>Cria um tilemap 20x15 (tiles 16px) centrado no ponto dado, sem tileset.</summary>
    public void CreateTilemap(double x, double y)
    {
        if (_document is null)
            return;

        var names = Entities.Select(e => e.Name).ToHashSet();
        string name = "Tilemap";
        for (int number = 1; names.Contains(name); number++)
            name = $"Tilemap{number}";

        var node = new System.Text.Json.Nodes.JsonObject
        {
            ["Name"] = name,
            ["Components"] = new System.Text.Json.Nodes.JsonArray(
                new System.Text.Json.Nodes.JsonObject
                {
                    ["Type"] = "Transform",
                    ["X"] = (float)Math.Round(x - 160),
                    ["Y"] = (float)Math.Round(y - 120),
                },
                new System.Text.Json.Nodes.JsonObject
                {
                    ["Type"] = "Tilemap",
                    ["TileWidth"] = 16,
                    ["TileHeight"] = 16,
                    ["Width"] = 20,
                    ["Height"] = 15,
                    ["Tiles"] = new System.Text.Json.Nodes.JsonArray(),
                }),
        };

        _document.Objects.Add(node);

        var entity = new EntityViewModel(node, this);
        entity.Edited += OnEdited;
        Entities.Add(entity);
        SelectedEntity = entity;
        OnEdited($"create:{node.GetHashCode()}");
        Status = $"{name} criado — defina o tileset (duplo-clique num asset) e pinte.";
    }

    /// <summary>Instancia uma prefab na cena atual: clona os Components do arquivo, dá um
    /// Transform novo na posição pedida e linka a entidade à prefab (duplo-clique no painel PREFABS).</summary>
    public void CreatePrefabInstance(PrefabFileViewModel prefab, double x, double y)
    {
        if (_document is null)
            return;

        if (System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(prefab.FullPath))
                is not System.Text.Json.Nodes.JsonObject prefabRoot
            || prefabRoot["Components"] is not System.Text.Json.Nodes.JsonArray prefabComponents)
        {
            Status = $"Prefab '{prefab.Name}' inválida — sem 'Components'.";
            return;
        }

        var names = Entities.Select(e => e.Name).ToHashSet();
        string name = prefab.Name;
        for (int number = 1; names.Contains(name); number++)
            name = $"{prefab.Name}{number}";

        var components = new System.Text.Json.Nodes.JsonArray(
            new System.Text.Json.Nodes.JsonObject
            {
                ["Type"] = "Transform",
                ["X"] = (float)Math.Round(x),
                ["Y"] = (float)Math.Round(y),
            });
        foreach (var comp in prefabComponents)
        {
            if (comp is System.Text.Json.Nodes.JsonObject obj && obj["Type"]?.GetValue<string>() == "Transform")
                continue;
            components.Add(System.Text.Json.Nodes.JsonNode.Parse(comp!.ToJsonString()));
        }

        var node = new System.Text.Json.Nodes.JsonObject
        {
            ["Name"] = name,
            ["Prefab"] = prefab.RelativePath,
            ["Components"] = components,
        };

        _document.Objects.Add(node);

        var entity = new EntityViewModel(node, this);
        entity.Edited += OnEdited;
        Entities.Add(entity);
        SelectedEntity = entity;
        OnEdited($"create:{node.GetHashCode()}");
        Status = $"{name} instanciada de {prefab.Name}.";
    }

    // ---- Validar projeto ----

    public bool CanValidate => _document is not null;

    /// <summary>
    /// Varre todas as cenas do projeto atrás de referência quebrada e joga o resultado no
    /// painel de saída. É o passo que faltava entre "compila" e "roda": nada aqui é erro de
    /// compilação — asset faltando fecha o jogo sozinho, componente desconhecido some calado.
    /// </summary>
    public void ValidateProject()
    {
        if (_document is null)
        {
            Status = "Abra uma cena antes de validar.";
            return;
        }

        ReloadSceneFiles();
        ReloadUiScreens();

        // Cenas de gameplay E telas de UI: as duas listas vêm da mesma pasta de assets e as
        // duas podem apontar pra textura que não existe mais.
        var files = SceneFiles.Select(f => f.FullPath)
            .Concat(UiScreens.Select(f => f.FullPath))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        // Nativos + os [SceneScript] descobertos. Sem os descobertos, todo componente custom
        // do usuário viraria falso positivo — e uma lista cheia de erro falso não se lê.
        var known = EntityViewModel.NativeComponentTypes
            .Concat(CustomScripts.Select(s => s.Name))
            .ToList();

        var problems = ProjectValidator.Validate(
            _document.AssetsRoot, files, known, _settings?.EffectiveUiFont);

        ClearOutput();
        Log($"--- Validação: {_document.AssetsRoot} ---");

        if (problems.Count == 0)
        {
            Log("Nenhum problema encontrado.");
            Status = "Validação: nenhum problema encontrado.";
            StatusDetail = null;
            return;
        }

        foreach (var problem in problems)
            Log($"! {problem.Where}: {problem.Message}");

        Status = $"Validação: {problems.Count} problema(s) — veja o painel de saída.";
        StatusDetail = string.Join(Environment.NewLine,
            problems.Select(problem => $"{problem.Where}: {problem.Message}"));
    }

    // ---- Hierarquia (pai/filho) ----

    /// <summary>
    /// Move uma entidade e leva os descendentes pelo mesmo deslocamento, igual o runtime faz
    /// (ver World.UpdateHierarchy). Sem isto o editor mentiria: no viewport a arma ficaria
    /// parada enquanto o player anda, e só no Play ela grudaria de volta.
    ///
    /// <para>Delta, não reposicionamento: preserva o encaixe que a pessoa montou, que é a mesma
    /// regra do runtime.</para>
    /// </summary>
    public void MoveEntityWithChildren(EntityViewModel entity, float x, float y)
    {
        var transform = entity.Transform;
        if (transform is null)
            return;

        float deltaX = x - transform.GetFloat("X", 0f);
        float deltaY = y - transform.GetFloat("Y", 0f);

        entity.SetPosition(x, y);

        if (deltaX == 0f && deltaY == 0f)
            return;

        // Tag do PAI em todos: o arrasto inteiro vira um passo de undo só.
        foreach (var descendant in Descendants(entity))
        {
            if (descendant.Transform is not { } childTransform)
                continue;

            descendant.SetPosition(
                childTransform.GetFloat("X", 0f) + deltaX,
                childTransform.GetFloat("Y", 0f) + deltaY,
                entity.MoveTag);
        }
    }

    /// <summary>Filhos, netos e por aí abaixo. Guarda contra ciclo (A pai de B, B pai de A) —
    /// hierarquia é por nome e nada impede a pessoa de fechar o laço no inspector.</summary>
    public IEnumerable<EntityViewModel> Descendants(EntityViewModel root)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { root.Name };
        var pending = new Queue<string>();
        pending.Enqueue(root.Name);

        while (pending.Count > 0)
        {
            string parentName = pending.Dequeue();

            foreach (var candidate in Entities)
            {
                if (candidate.ParentName != parentName || !visited.Add(candidate.Name))
                    continue;

                pending.Enqueue(candidate.Name);
                yield return candidate;
            }
        }
    }

    // ---- Duplicar / copiar / colar ----

    private string? _entityClipboard;

    /// <summary>Tem entidade copiada esperando um Ctrl+V (a cópia atravessa troca de cena —
    /// é assim que se leva um objeto montado de uma fase pra outra).</summary>
    public bool CanPaste => _entityClipboard is not null && _document is not null;

    /// <summary>Nome livre a partir de um nome base, ignorando o número que ele já tenha no
    /// fim: duplicar "Plataforma3" dá "Plataforma4", não "Plataforma31".</summary>
    private string UniqueEntityName(string desired)
    {
        var taken = Entities.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(desired))
            return desired;

        string root = desired.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        if (root.Length == 0)
            root = desired; // nome só de dígitos: trata o todo como raiz

        // Continua a contagem do número que o original já tinha, em vez de recomeçar do 1:
        // duplicar "Plataforma3" com "Plataforma1" livre daria "Plataforma1", que parece
        // outro objeto.
        int start = 1;
        if (root.Length < desired.Length && int.TryParse(desired[root.Length..], out int current))
            start = current + 1;

        for (int number = start; ; number++)
        {
            string candidate = root + number;
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    /// <summary>Insere um nó de entidade já pronto (cópia/colagem) na cena, com nome livre,
    /// selecionado e num passo de undo próprio.</summary>
    private void InsertEntityNode(System.Text.Json.Nodes.JsonObject node, string editTag)
    {
        if (_document is null)
            return;

        node["Name"] = UniqueEntityName(node["Name"]?.GetValue<string>() ?? "Entidade");

        _document.Objects.Add(node);

        var entity = new EntityViewModel(node, this);
        entity.Edited += OnEdited;
        Entities.Add(entity);

        // Diferente de CreateEntity: uma entidade colada PODE trazer EventTrigger junto, e sem
        // isto ela não apareceria na aba de eventos até recarregar a cena.
        RebuildEventEntities();

        SelectedEntity = entity;
        OnEdited($"{editTag}:{node.GetHashCode()}");
    }

    /// <summary>Duplica a entidade selecionada na mesma posição e passa a seleção pra cópia —
    /// duplicar e arrastar é o gesto, então quem sai selecionado tem que ser o novo.</summary>
    public void DuplicateSelectedEntity()
    {
        if (_document is null || SelectedEntity is null)
            return;

        var clone = SelectedEntity.Node.DeepClone().AsObject();
        InsertEntityNode(clone, "duplicate");
        Status = $"Duplicado: {SelectedEntity.Name} (mesma posição — arraste pra separar).";
    }

    public void CopySelectedEntity()
    {
        if (SelectedEntity is null)
            return;

        _entityClipboard = SelectedEntity.Node.ToJsonString();
        Raise(nameof(CanPaste));
        Status = $"Copiado: {SelectedEntity.Name}.";
    }

    public void PasteEntity()
    {
        if (_document is null || _entityClipboard is null)
            return;

        System.Text.Json.Nodes.JsonObject node;
        try
        {
            node = System.Text.Json.Nodes.JsonNode.Parse(_entityClipboard)!.AsObject();
        }
        catch (Exception ex)
        {
            Status = $"Não consegui colar: {ex.Message}";
            return;
        }

        InsertEntityNode(node, "paste");
        Status = $"Colado: {SelectedEntity!.Name}.";
    }

    public void DeleteSelectedEntity()
    {
        if (_document is null || SelectedEntity is null)
            return;

        int index = Entities.IndexOf(SelectedEntity);
        var node = SelectedEntity.Node;
        _document.Objects.Remove(node);
        Entities.Remove(SelectedEntity);

        SelectedEntity = Entities.Count > 0
            ? Entities[Math.Min(index, Entities.Count - 1)]
            : null;
        OnEdited($"delete:{node.GetHashCode()}");
    }

    // ---- Undo / Redo ----

    public void Undo()
    {
        if (_undoStack.Count == 0 || _document is null)
            return;

        _redoStack.Push(_lastSnapshot);
        Restore(_undoStack.Pop());
        Status = "Desfeito.";
    }

    public void Redo()
    {
        if (_redoStack.Count == 0 || _document is null)
            return;

        _undoStack.Push(_lastSnapshot);
        Restore(_redoStack.Pop());
        Status = "Refeito.";
    }

    private void Restore(string json)
    {
        string? selectedName = SelectedEntity?.Name;

        _restoring = true;
        _document = SceneDocument.FromJson(json, _document!.FilePath, _document.AssetsRoot);
        RebuildEntities();
        SelectedEntity = Entities.FirstOrDefault(e => e.Name == selectedName) ?? Entities.FirstOrDefault();
        _restoring = false;

        _lastSnapshot = json;
        _lastEditTag = null;
        IsDirty = true;
        RaiseUndoState();
        SceneEdited?.Invoke();
    }

    private void OnEdited(string tag)
    {
        if (_restoring || _document is null)
            return;

        bool coalesce = tag == _lastEditTag
            && (DateTime.UtcNow - _lastEditAt).TotalMilliseconds < CoalesceWindowMs;

        if (!coalesce)
        {
            _undoStack.Push(_lastSnapshot);
            _redoStack.Clear();
            RaiseUndoState();
        }

        _lastSnapshot = _document.Root.ToJsonString();
        _lastEditTag = tag;
        _lastEditAt = DateTime.UtcNow;

        if (tag.StartsWith("addcomp:") || tag.StartsWith("removecomp:"))
            RebuildEventEntities();

        // Mexeu no campo Parent: a marca "↳ Pai" na hierarquia tem que acompanhar na hora.
        if (tag is "Transform.Parent")
            foreach (var entity in Entities)
                entity.RaiseParentChanged();

        IsDirty = true;
        RebuildTilePalette();
        SceneEdited?.Invoke();
    }

    private void RaiseUndoState()
    {
        Raise(nameof(CanUndo));
        Raise(nameof(CanRedo));
    }
}
