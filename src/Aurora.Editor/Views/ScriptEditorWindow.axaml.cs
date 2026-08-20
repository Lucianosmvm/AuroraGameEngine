using System.Text.RegularExpressions;
using Aurora.Editor.Models;
using Aurora.Editor.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit.Highlighting;

namespace Aurora.Editor.Views;

/// <summary>
/// Editor de C# embutido no próprio Aurora Editor: "+ Novo" abre já com o template escolhido,
/// "Carregar" abre um .cs existente, e Salvar grava na pasta Scripts do projeto e registra o
/// [SceneScript] na hora (<see cref="MainViewModel.SaveScriptSource"/>) — sem sair pro VS Code,
/// sem colar arquivo na pasta na mão e sem esperar um build só pra o script aparecer na lista de
/// componentes. "Verificar" continua existindo pra quando importar ver o erro do compilador.
/// </summary>
public partial class ScriptEditorWindow : Window
{
    private readonly MainViewModel _viewModel;

    /// <summary>Null enquanto o script novo não foi salvo — nesse estado o nome do arquivo ainda
    /// é decidido pelo nome da classe.</summary>
    private string? _path;

    /// <summary>Nome de classe atualmente refletido na caixa "Classe:" — guardado pra saber qual
    /// identificador trocar no código quando o usuário renomeia por lá.</summary>
    private string _className;

    private string _savedText;
    private bool _confirmedClose;
    private bool _syncingName;

    public ScriptEditorWindow()
        : this(new MainViewModel(), null)
    {
    }

    /// <param name="path">Arquivo a abrir; null cria um script novo a partir do template
    /// selecionado no painel SCRIPTS.</param>
    public ScriptEditorWindow(MainViewModel viewModel, string? path)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _path = path;

        Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 4;
        Editor.Options.HighlightCurrentLine = true;

        if (path is null)
        {
            _className = viewModel.SelectedScriptTemplate.DefaultClassName;
            Editor.Document.Text = viewModel.BuildScriptTemplateSource(_className);
            SetStatus($"Template \"{viewModel.SelectedScriptTemplate.DisplayName}\". " +
                      "Salvar grava em Scripts/ com o nome da classe.");
        }
        else
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex)
            {
                text = "";
                SetStatus($"Erro ao abrir {Path.GetFileName(path)}: {ex.Message}");
            }

            Editor.Document.Text = text;
            _className = ScriptSourceParser.FindPrimaryClassName(text)
                ?? Path.GetFileNameWithoutExtension(path);
        }

        _savedText = Editor.Document.Text;
        ClassNameBox.Text = _className;
        ClassNameBox.TextChanged += OnClassNameChanged;
        Editor.TextChanged += (_, _) => UpdateTitle();
        UpdateTitle();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                Save();
                e.Handled = true;
            }
        };

        Closing += (_, e) =>
        {
            // Sem caixa de diálogo: o primeiro fechar com alteração pendente vira aviso, o
            // segundo descarta — o usuário nunca perde código por um clique só.
            if (IsDirty && !_confirmedClose)
            {
                _confirmedClose = true;
                e.Cancel = true;
                SetStatus("Alterações não salvas. Salve (Ctrl+S) ou feche de novo para descartar.");
            }
        };
    }

    /// <summary>Arquivo que esta janela está editando; null enquanto o script novo não foi salvo.
    /// A MainWindow usa isto pra não abrir duas janelas sobre o mesmo arquivo.</summary>
    public string? CurrentPath => _path;

    private bool IsDirty => Editor.Document.Text != _savedText;

    private void UpdateTitle()
    {
        string name = _path is null ? $"{_className}.cs (novo)" : Path.GetFileName(_path);
        Title = $"Editor de Script — {name}{(IsDirty ? " *" : "")}";
        PathText.Text = _path ?? _viewModel.ScriptsDirPath;
        if (IsDirty)
            _confirmedClose = false;
    }

    /// <summary>Renomear na caixa "Classe:" renomeia o identificador no código também — senão
    /// salvaria um arquivo Foo.cs com "class MeuScript" dentro, que é justo o tipo de detalhe que
    /// o fluxo interno deveria eliminar.</summary>
    private void OnClassNameChanged(object? sender, TextChangedEventArgs e)
    {
        if (_syncingName)
            return;

        // Caixa vazia é "está no meio de digitar", não "renomeie pro fallback do ToIdentifier".
        string typed = ClassNameBox.Text ?? "";
        if (typed.Trim().Length == 0)
            return;

        string newName = GameProjectScaffolder.ToIdentifier(typed);
        if (newName == _className)
            return;

        Editor.Document.Text = Regex.Replace(Editor.Document.Text,
            $@"\b{Regex.Escape(_className)}\b", newName);
        _className = newName;
        UpdateTitle();
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Save();

    private void OnSaveAndAttach(object? sender, RoutedEventArgs e)
    {
        if (!Save())
            return;

        var scripts = ScriptSourceParser.Parse(Editor.Document.Text);
        if (scripts.Count == 0)
        {
            SetStatus("Salvo, mas nenhuma classe [SceneScript] encontrada — só essas podem virar componente.");
            return;
        }

        if (_viewModel.AttachScriptToSelectedEntity(scripts[0].Name))
            SetStatus($"Salvo e anexado: {scripts[0].Name} → {_viewModel.SelectedEntity!.Name}.");
        else
            SetStatus("Salvo. Selecione uma entidade na hierarquia para anexar o script.");
    }

    /// <summary>Grava o arquivo. Script novo vira &lt;Classe&gt;.cs na pasta Scripts do projeto;
    /// script carregado é regravado no mesmo caminho (renomear classe não move arquivo).</summary>
    private bool Save()
    {
        string text = Editor.Document.Text;
        string? parsedClass = ScriptSourceParser.FindPrimaryClassName(text);

        if (_path is null)
        {
            string scriptsDir = _viewModel.ScriptsDirPath;
            if (scriptsDir.Length == 0)
            {
                SetStatus("Abra um projeto (aurora.project.json) antes de salvar o script.");
                return false;
            }

            string className = GameProjectScaffolder.ToIdentifier(
                parsedClass ?? ClassNameBox.Text ?? "");
            if (className.Length == 0)
            {
                SetStatus("Dê um nome de classe válido antes de salvar.");
                return false;
            }

            string target = Path.Combine(scriptsDir, $"{className}.cs");
            if (File.Exists(target))
            {
                SetStatus($"Já existe {className}.cs em Scripts/. Mude o nome da classe (ou carregue o arquivo existente).");
                return false;
            }

            _path = target;
            SyncClassNameBox(className);
        }
        else if (parsedClass is not null)
        {
            SyncClassNameBox(parsedClass);
        }

        try
        {
            _viewModel.SaveScriptSource(_path, text);
        }
        catch (Exception ex)
        {
            SetStatus($"Erro ao salvar: {ex.Message}");
            return false;
        }

        _savedText = text;
        _confirmedClose = false;
        UpdateTitle();
        SetStatus(_viewModel.Status);
        return true;
    }

    /// <summary>Reflete o nome real da classe na caixa sem disparar o rename de volta no código.</summary>
    private void SyncClassNameBox(string className)
    {
        _className = className;
        _syncingName = true;
        ClassNameBox.Text = className;
        _syncingName = false;
    }

    private async void OnCompile(object? sender, RoutedEventArgs e)
    {
        if (IsDirty && !Save())
            return;

        SaveButton.IsEnabled = SaveAttachButton.IsEnabled = CompileButton.IsEnabled = false;
        SetStatus("Compilando (dotnet build)... pode demorar na primeira vez.");
        try
        {
            var result = await _viewModel.CompileGameProjectAsync();
            SetStatus(result.Success ? "Compilou sem erros." : $"Erro de compilação: {result.Summary}", result.Detail);

            // Compilou: o catálogo do assembly é a verdade — vale rodar o discover completo,
            // que também pega scripts de outros arquivos editados fora daqui.
            if (result.Success)
                _viewModel.RefreshScriptCatalog();
        }
        finally
        {
            SaveButton.IsEnabled = SaveAttachButton.IsEnabled = CompileButton.IsEnabled = true;
        }
    }

    private void SetStatus(string message, string? detail = null)
    {
        StatusText.Text = message;
        ToolTip.SetTip(StatusText, detail);
    }
}
