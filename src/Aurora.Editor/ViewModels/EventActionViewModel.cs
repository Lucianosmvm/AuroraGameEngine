using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace Aurora.Editor.ViewModels;

public sealed class EventActionViewModel : ViewModelBase
{
    private readonly JsonObject _node;
    private readonly Action _onEdited;
    private readonly MainViewModel? _owner;

    public ICommand RemoveCommand { get; }
    public ICommand AddOptionCommand { get; }

    public ObservableCollection<EventOptionViewModel> Options { get; } = [];

    // Static lists exposed as instance properties so XAML binding works
    public string[] ActionTypes { get; } =
    [
        "Wait", "SetVariable", "SetSwitch",
        "ShowMessage", "ShowChoice",
        "Teleport", "Destroy",
        "Damage", "Heal",
        "PlayAnimation", "StopAnimation",
        "PlaySound", "PlayMusic", "StopMusic",
        "ChangeScene", "Save", "Quit",
        "AddItem", "RemoveItem",
        "SetQuestStage", "AdvanceQuest",
        "SetActive",
        "ShowUI", "HideUI", "ToggleUI",
        "SetPause",
    ];

    public string[] OpTypes { get; } = ["Set", "Add"];

    public EventActionViewModel(JsonObject node, Action onEdited, Action<EventActionViewModel> onRemove, MainViewModel? owner = null)
    {
        _node = node;
        _onEdited = onEdited;
        _owner = owner;
        RemoveCommand = new RelayCommand(() => onRemove(this));
        AddOptionCommand = new RelayCommand(AddOption);
        RebuildOptions();
    }

    /// <summary>ChangeScene lista as cenas de gameplay; ShowUI/HideUI/ToggleUI listam as telas de
    /// UI pelo id (nome do arquivo sem .json) — evita digitar caminho/extensão na mão.</summary>
    public IEnumerable<string> NamePickerOptions => ActionType switch
    {
        "ChangeScene" => _owner?.SceneFiles.Select(s => s.Name) ?? [],
        "ShowUI" or "HideUI" or "ToggleUI" => _owner?.UiScreens.Select(s => System.IO.Path.GetFileNameWithoutExtension(s.Name)) ?? [],
        _ => [],
    };

    public bool ShowNamePicker => ActionType is "ChangeScene" or "ShowUI" or "HideUI" or "ToggleUI";
    public bool ShowNameText => ShowName && !ShowNamePicker;

    public string ActionType
    {
        get => _node["Action"]?.GetValue<string>() ?? "Wait";
        set
        {
            _node["Action"] = value;
            Raise();
            RaiseVisibility();
            _onEdited();
        }
    }

    public string Name
    {
        get => _node["Name"]?.GetValue<string>() ?? "";
        set
        {
            if (string.IsNullOrEmpty(value)) _node.Remove("Name");
            else _node["Name"] = value;
            Raise();
            _onEdited();
        }
    }

    public string NameLabel => ActionType switch
    {
        "SetVariable" or "SetSwitch" => "Variável",
        "Teleport" or "Destroy" or "Damage" or "Heal" or "PlayAnimation" or "StopAnimation" or "SetActive" => "Entidade",
        "ChangeScene" or "PlaySound" or "PlayMusic" => "Arquivo",
        "AddItem" or "RemoveItem" => "Item",
        "SetQuestStage" or "AdvanceQuest" => "Quest",
        "ShowUI" or "HideUI" or "ToggleUI" => "Tela UI",
        _ => "Falante",
    };

    public string Op
    {
        get => _node["Op"]?.GetValue<string>() ?? "Set";
        set { _node["Op"] = value; Raise(); _onEdited(); }
    }

    public float ValueFloat
    {
        get => _node["Value"]?.GetValue<float>() ?? 0f;
        set { _node["Value"] = value; Raise(); Raise(nameof(ValueText)); _onEdited(); }
    }

    public string ValueText
    {
        get => ValueFloat.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                ValueFloat = f;
        }
    }

    public string ValueLabel => ActionType switch
    {
        "PlaySound" or "PlayMusic" => "Volume",
        "Save" => "Slot",
        "AddItem" or "RemoveItem" => "Quantidade",
        "SetQuestStage" => "Estágio",
        "AdvanceQuest" => "Incremento",
        "Damage" or "Heal" => "Quantidade",
        _ => "Valor",
    };

    public bool On
    {
        get => _node["On"]?.GetValue<bool>() ?? true;
        set { _node["On"] = value; Raise(); _onEdited(); }
    }

    public string OnLabel => ActionType switch
    {
        "PlayMusic" => "Loop",
        "SetPause" => "Pausar",
        _ => "Ligar",
    };

    public float X
    {
        get => _node["X"]?.GetValue<float>() ?? 0f;
        set { _node["X"] = value; Raise(); Raise(nameof(XText)); _onEdited(); }
    }

    public string XText
    {
        get => X.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                X = f;
        }
    }

    public float Y
    {
        get => _node["Y"]?.GetValue<float>() ?? 0f;
        set { _node["Y"] = value; Raise(); Raise(nameof(YText)); _onEdited(); }
    }

    public string YText
    {
        get => Y.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                Y = f;
        }
    }

    public float Seconds
    {
        get => _node["Seconds"]?.GetValue<float>() ?? 1f;
        set { _node["Seconds"] = value; Raise(); Raise(nameof(SecondsText)); _onEdited(); }
    }

    public string SecondsText
    {
        get => Seconds.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                Seconds = f;
        }
    }

    public string Text
    {
        get => _node["Text"]?.GetValue<string>() ?? "";
        set
        {
            if (string.IsNullOrEmpty(value)) _node.Remove("Text");
            else _node["Text"] = value;
            Raise();
            _onEdited();
        }
    }

    public string TextLabel => ActionType == "PlayAnimation" ? "Clipe" : "Texto";

    public string ActionDescription => ActionType switch
    {
        "Wait"           => "Espera X segundos antes da próxima ação",
        "SetVariable"    => "Define ou soma um valor numa variável do GameState",
        "SetSwitch"      => "Liga/desliga um switch (booleano) do GameState",
        "ShowMessage"    => "Mostra uma caixa de diálogo com texto",
        "ShowChoice"     => "Mostra diálogo com opções de escolha (cada uma liga um switch)",
        "Teleport"       => "Move uma entidade pra posição X,Y",
        "Destroy"        => "Remove uma entidade da cena",
        "Damage"         => "Aplica dano numa entidade com Health (ignora se invencível/i-frames)",
        "Heal"           => "Cura uma entidade com Health, sem passar do Max",
        "Quit"           => "Fecha o jogo",
        "PlayAnimation"  => "Troca o clipe ativo do Animator de uma entidade",
        "StopAnimation"  => "Para a animação ativa de uma entidade",
        "PlaySound"      => "Toca um efeito sonoro (arquivo em Assets)",
        "PlayMusic"      => "Toca música em loop (canal separado dos efeitos)",
        "StopMusic"      => "Para a música que está tocando",
        "ChangeScene"    => "Carrega outra cena (arquivo .json)",
        "Save"           => "Salva o jogo num slot",
        "AddItem"        => "Adiciona quantidade ao item no inventário",
        "RemoveItem"     => "Remove quantidade do item no inventário (nunca fica negativo)",
        "SetQuestStage"  => "Define o estágio atual da quest",
        "AdvanceQuest"   => "Avança o estágio da quest (padrão +1)",
        "SetActive"      => "Liga/desliga ParticleEmitter, Light2D ou GlobalTint de uma entidade sem destruí-la",
        "ShowUI"         => "Mostra uma tela de UI já carregada (HUD, menu)",
        "HideUI"         => "Esconde uma tela de UI já carregada",
        "ToggleUI"       => "Alterna visível/escondido de uma tela de UI já carregada",
        "SetPause"       => "Liga/desliga a simulação do World (behaviors, colisão, partículas, vida) — cena continua desenhada, só para de mexer",
        _                => "",
    };

    // Visibility — recalculated when ActionType changes
    public bool ShowName => ActionType is "SetVariable" or "SetSwitch" or "Teleport" or "Destroy"
        or "Damage" or "Heal"
        or "PlayAnimation" or "StopAnimation" or "ChangeScene" or "PlaySound" or "PlayMusic" or "ShowMessage" or "ShowChoice"
        or "AddItem" or "RemoveItem" or "SetQuestStage" or "AdvanceQuest" or "SetActive"
        or "ShowUI" or "HideUI" or "ToggleUI";
    public bool ShowOp => ActionType == "SetVariable";
    public bool ShowValue => ActionType is "SetVariable" or "PlaySound" or "PlayMusic" or "Save"
        or "AddItem" or "RemoveItem" or "SetQuestStage" or "AdvanceQuest" or "Damage" or "Heal";
    public bool ShowOn => ActionType is "SetSwitch" or "PlayMusic" or "SetActive" or "SetPause";
    public bool ShowXY => ActionType == "Teleport";
    public bool ShowSeconds => ActionType == "Wait";
    public bool ShowText => ActionType is "ShowMessage" or "ShowChoice" or "PlayAnimation";
    public bool ShowOptions => ActionType == "ShowChoice";

    private void RaiseVisibility()
    {
        Raise(nameof(ShowName));
        Raise(nameof(ShowOp));
        Raise(nameof(ShowValue));
        Raise(nameof(ShowOn));
        Raise(nameof(ShowXY));
        Raise(nameof(ShowSeconds));
        Raise(nameof(ShowText));
        Raise(nameof(ShowOptions));
        Raise(nameof(ShowNamePicker));
        Raise(nameof(ShowNameText));
        Raise(nameof(NamePickerOptions));
        Raise(nameof(NameLabel));
        Raise(nameof(ValueLabel));
        Raise(nameof(OnLabel));
        Raise(nameof(TextLabel));
        Raise(nameof(ActionDescription));
    }

    private void AddOption()
    {
        var optNode = new JsonObject { ["Text"] = "Opção" };
        if (_node["Options"] is not JsonArray arr)
            _node["Options"] = arr = [];
        arr.Add(optNode);
        Options.Add(new EventOptionViewModel(optNode, _onEdited, RemoveOption));
        _onEdited();
    }

    private void RemoveOption(EventOptionViewModel opt)
    {
        int index = Options.IndexOf(opt);
        if (index >= 0 && _node["Options"] is JsonArray arr && index < arr.Count)
            arr.RemoveAt(index);
        Options.Remove(opt);
        _onEdited();
    }

    private void RebuildOptions()
    {
        Options.Clear();
        if (_node["Options"] is JsonArray arr)
        {
            foreach (var item in arr.OfType<JsonObject>())
                Options.Add(new EventOptionViewModel(item, _onEdited, RemoveOption));
        }
    }
}
