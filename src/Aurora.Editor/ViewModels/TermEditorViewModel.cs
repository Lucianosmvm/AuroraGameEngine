using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Aurora.Runtime.Database;

namespace Aurora.Editor.ViewModels;

/// <summary>Um texto de interface: a chave, o que o jogo vai mostrar, e o padrão da engine.</summary>
public sealed class TermRowViewModel : ViewModelBase
{
    private readonly Action _onEdited;
    private string _key;
    private string _text;

    public TermRowViewModel(string key, string text, string standard, string description, bool known, Action onEdited)
    {
        _key = key;
        _text = text;
        Standard = standard;
        Description = description;
        IsKnown = known;
        _onEdited = onEdited;
    }

    /// <summary>Texto que a engine usa quando a chave não é preenchida. Vira o watermark do
    /// campo: o autor vê o que já acontece hoje antes de decidir trocar.</summary>
    public string Standard { get; }

    public string Description { get; }

    /// <summary>Chave que a própria engine consulta (a loja, por exemplo). As conhecidas não
    /// podem ser renomeadas — renomear só faria a engine parar de achar o texto.</summary>
    public bool IsKnown { get; }

    public bool CanEditKey => !IsKnown;

    public string Key
    {
        get => _key;
        set { _key = value; Raise(); _onEdited(); }
    }

    public string Text
    {
        get => _text;
        set { _text = value; Raise(); _onEdited(); }
    }
}

/// <summary>
/// Aba "Termos" do banco. Edita <c>Assets/database/terms.json</c>: as palavras que a ENGINE
/// escreve na tela ("Comprar", "Sair", "Não dá pro seu bolso").
///
/// <para>Serve pra dois usos: adequar o vocabulário ao seu jogo (numa nave, "Comprar" pode ser
/// "Requisitar") e traduzir sem caçar string dentro do código. Toda chave tem um padrão em
/// português embutido — deixar em branco mantém o que já aparece hoje, e um jogo sem o arquivo
/// não muda em nada.</para>
///
/// <para>Diálogo, nome e descrição de item NÃO ficam aqui: aquilo é conteúdo, e já tem lugar (a
/// cena e o banco de itens). Aqui é só interface.</para>
/// </summary>
public sealed class TermEditorViewModel : ViewModelBase
{
    private readonly string _path;

    public ObservableCollection<TermRowViewModel> Rows { get; } = [];

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }

    private TermRowViewModel? _selected;
    public TermRowViewModel? Selected
    {
        get => _selected;
        set { _selected = value; Raise(); Raise(nameof(CanRemove)); }
    }

    /// <summary>Só chave própria do jogo pode ser removida — apagar uma linha conhecida não
    /// apagaria nada (o padrão continua valendo) e daria a impressão de ter sumido.</summary>
    public bool CanRemove => _selected is { IsKnown: false };

    private string _status = "";
    public string Status
    {
        get => _status;
        private set { _status = value; Raise(); }
    }

    public TermEditorViewModel(string jsonPath)
    {
        _path = jsonPath;
        AddCommand = new RelayCommand(AddRow);
        RemoveCommand = new RelayCommand(RemoveSelected);
        Load();
    }

    private void Load()
    {
        Rows.Clear();

        var saved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (_path.Length > 0 && File.Exists(_path))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(_path)) is JsonObject root
                    && root["Terms"] is JsonObject terms)
                {
                    foreach (var (key, value) in terms)
                        saved[key] = value?.GetValue<string>() ?? "";
                }
            }
            catch (Exception ex)
            {
                Status = $"terms.json inválido ({ex.Message}) — não salve por cima sem conferir.";
                return;
            }
        }

        // As chaves da engine aparecem sempre, preenchidas ou não: sem isso, descobrir que
        // "shop.cantAfford" existe exigiria ler o código-fonte da loja.
        foreach (var (key, standard, description) in TermDatabase.KnownKeys)
        {
            Rows.Add(new TermRowViewModel(key, saved.GetValueOrDefault(key, ""), standard, description,
                known: true, MarkDirty));
            saved.Remove(key);
        }

        // O que sobrou é chave do próprio jogo, lida por script ou pelo token {Term:…} do UiText.
        foreach (var (key, text) in saved)
            Rows.Add(new TermRowViewModel(key, text, "", "Chave do seu jogo", known: false, MarkDirty));

        Status = $"{Rows.Count(r => r.Text.Length > 0)} termo(s) personalizado(s).";
    }

    private void MarkDirty() => Status = "Alterado — clique em Salvar.";

    private void AddRow()
    {
        var row = new TermRowViewModel(UniqueKey(), "", "", "Chave do seu jogo", known: false, MarkDirty);
        Rows.Add(row);
        Selected = row;
        MarkDirty();
    }

    private string UniqueKey()
    {
        const string baseKey = "meujogo.texto";
        if (Rows.All(r => !string.Equals(r.Key, baseKey, StringComparison.OrdinalIgnoreCase)))
            return baseKey;

        for (int n = 2; ; n++)
        {
            string candidate = $"{baseKey}_{n}";
            if (Rows.All(r => !string.Equals(r.Key, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    private void RemoveSelected()
    {
        if (Selected is not { IsKnown: false } row)
            return;

        Rows.Remove(row);
        Selected = null;
        MarkDirty();
    }

    public void Save()
    {
        if (_path.Length == 0)
        {
            Status = "Sem projeto aberto — nada a salvar.";
            return;
        }

        var terms = new JsonObject();
        foreach (var row in Rows)
        {
            // Chave em branco não vai pro arquivo: gravar "" apagaria o padrão da engine e a loja
            // mostraria opção sem texto nenhum.
            if (row.Key.Length > 0 && row.Text.Length > 0)
                terms[row.Key] = row.Text;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path,
            new JsonObject { ["Terms"] = terms }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Status = $"Salvo: {Path.GetFileName(_path)} ({terms.Count} termo(s)).";
    }
}
