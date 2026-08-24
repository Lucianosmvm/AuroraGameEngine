using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Aurora.Editor.Models;

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
        "Teleport", "Destroy", "Spawn", "SetWeather",
        "Damage", "Heal",
        "PlayAnimation", "StopAnimation",
        "PlaySound", "PlayMusic", "StopMusic",
        "ChangeScene", "Save", "Load", "NewGame", "Quit",
        "AddItem", "RemoveItem", "UseItem",
        "If", "Else", "EndIf",
        "SetQuestStage", "AdvanceQuest",
        "SetActive",
        "ShowUI", "HideUI", "ToggleUI",
        "SetPause",
    ];

    /// <summary>Operadores do campo Op. If compara, SetVariable atribui — a mesma caixa serve os
    /// dois porque a ação já mandava no significado do campo.</summary>
    public string[] OpTypes => ActionType == "If"
        ? [">=", "<=", ">", "<", "==", "!="]
        : ["Set", "Add"];

    /// <summary>O que o If compara. Guardado em "Text" — o campo já existia e estava livre pras
    /// ações de fluxo, então não custou nenhuma chave nova no JSON de cena.</summary>
    public string[] ConditionKinds { get; } = ["Variable", "Switch", "Item", "Quest"];

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
        // Spawn lista os prefabs pelo caminho relativo a Assets — é literalmente a string que o
        // runtime passa pro AssetManager, então escolher da lista não tem como digitar errado.
        "Spawn" => _owner?.Prefabs.Select(p => p.RelativePath) ?? [],
        // UseItem e If sobre item listam os ids do banco de dados — digitar id na mão é o jeito
        // mais fácil de criar um item que não faz nada e não avisa.
        "UseItem" => _owner?.ItemIds ?? [],
        _ => [],
    };

    public bool ShowNamePicker => ActionType is "ChangeScene" or "ShowUI" or "HideUI" or "ToggleUI"
        or "Spawn" or "UseItem";

    /// <summary>
    /// Sugestões do campo Nome quando ele é texto livre. Diferente do seletor acima: aqui o valor
    /// PODE ser algo que ainda não existe (entidade que só nasce em jogo, variável criada no
    /// primeiro SetVariable), então a lista orienta sem travar.
    /// </summary>
    public IEnumerable<string> NameSuggestions => ActionType switch
    {
        // Ações que miram entidade listam as etiquetas ANTES dos nomes: "#inimigo" atinge o grupo
        // todo, e é quase sempre o que se quer quando existe mais de um tipo do mesmo bicho —
        // nome exato pega uma entidade só (a mais antiga viva com aquele nome).
        "Teleport" or "Destroy" or "Damage" or "Heal" or "PlayAnimation" or "StopAnimation"
            or "SetActive" => TagTargets.Concat(_owner?.EntityNames ?? []),
        "SetWeather" => _owner?.EntityNames ?? [],
        "PlaySound" or "PlayMusic" => _owner?.SoundAssets ?? [],
        "AddItem" or "RemoveItem" => _owner?.ItemIds ?? [],
        _ => [],
    };

    /// <summary>Teclas comuns, pro gatilho KeyPress e pro campo de tecla das ações.</summary>
    public IEnumerable<string> KeyNames => MainViewModel.KeyNames;
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
        "Spawn" => "Prefab / tabela",
        "SetWeather" => "Tipo",
        "UseItem" => "Item",
        "If" => "Nome",
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
        get => _node["Value"].AsFloat(0f);
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

    /// <summary>Etiquetas da cena com o <c>#</c> na frente — é a forma que o EventSystem
    /// entende como "todos deste grupo".</summary>
    private IEnumerable<string> TagTargets => (_owner?.TagNames ?? []).Select(t => "#" + t);

    /// <summary>
    /// Alcance em pixels a partir de quem disparou o evento. 0 = cena inteira. Só muda alguma
    /// coisa quando o alvo é "#etiqueta": é o que faz um item de dano ser uma bomba em vez de
    /// uma arma que limpa o mapa.
    /// </summary>
    public float RadiusFloat
    {
        get => _node["Radius"].AsFloat(0f);
        set
        {
            float clamped = Math.Max(0f, value);
            if (clamped <= 0f) _node.Remove("Radius");
            else _node["Radius"] = clamped;
            Raise();
            Raise(nameof(RadiusText));
            _onEdited();
        }
    }

    public string RadiusText
    {
        get => RadiusFloat.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                RadiusFloat = f;
        }
    }

    public string ValueLabel => ActionType switch
    {
        "PlaySound" or "PlayMusic" => "Volume",
        "Save" or "Load" => "Slot",
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

    /// <summary>Probabilidade de 0 a 1. Removido do JSON quando é 1 pra não poluir toda ação de
    /// toda cena com um campo que quase sempre vale o padrão.</summary>
    public float ChanceFloat
    {
        get => _node["Chance"].AsFloat(1f);
        set
        {
            float clamped = Math.Clamp(value, 0f, 1f);
            if (clamped >= 1f) _node.Remove("Chance");
            else _node["Chance"] = clamped;
            Raise();
            Raise(nameof(ChanceText));
            _onEdited();
        }
    }

    public string ChanceText
    {
        get => ChanceFloat.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                ChanceFloat = f;
        }
    }

    /// <summary>ChangeScene: marcador da cena de destino onde o jogador aparece.</summary>
    public string SpawnPoint
    {
        get => _node["SpawnPoint"]?.GetValue<string>() ?? "";
        set
        {
            if (string.IsNullOrEmpty(value)) _node.Remove("SpawnPoint");
            else _node["SpawnPoint"] = value;
            Raise();
            _onEdited();
        }
    }

    /// <summary>O que o If compara — mora em "Text", que as ações de fluxo não usam pra outra
    /// coisa. Property separada porque no XAML é um ComboBox, não a caixa de texto do Text.</summary>
    public string ConditionKind
    {
        get => _node["Text"]?.GetValue<string>() ?? "Variable";
        set { _node["Text"] = value; Raise(); _onEdited(); }
    }

    public string OnLabel => ActionType switch
    {
        "PlayMusic" => "Loop",
        "SetPause" => "Pausar",
        _ => "Ligar",
    };

    public float X
    {
        get => _node["X"].AsFloat(0f);
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
        get => _node["Y"].AsFloat(0f);
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
        get => _node["Seconds"].AsFloat(1f);
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
        "Teleport"       => "Move a entidade (ou o grupo #etiqueta) pra posição X,Y",
        "Destroy"        => "Remove da cena a entidade — ou todas do grupo, com #etiqueta",
        "Spawn"          => "Instancia um prefab (ou sorteia de uma tabela de spawn); X,Y é o deslocamento a partir de quem disparou o evento",
        "SetWeather"     => "Troca o clima da cena — Nome é o tipo (Rain/Storm/Snow/Fog/Ash/None) e Valor a intensidade 0..1",
        "UseItem"        => "Usa um item do banco: roda o efeito dele e consome, se for consumível",
        "If"             => "Só executa as ações seguintes se a condição for verdadeira (até o Else/EndIf)",
        "Else"           => "Caminho alternativo do If acima",
        "EndIf"          => "Fecha o bloco do If",
        "Damage"         => "Aplica dano em quem tem Health (ignora se invencível/i-frames). Alvo #etiqueta pega o grupo todo; Alcance limita ao redor de quem disparou",
        "Heal"           => "Cura quem tem Health, sem passar do Max. Alvo #etiqueta cura o grupo todo",
        "Quit"           => "Fecha o jogo",
        "PlayAnimation"  => "Troca o clipe ativo do Animator de uma entidade",
        "StopAnimation"  => "Para a animação ativa de uma entidade",
        "PlaySound"      => "Toca um efeito sonoro (arquivo em Assets)",
        "PlayMusic"      => "Toca música em loop (canal separado dos efeitos)",
        "StopMusic"      => "Para a música que está tocando",
        "ChangeScene"    => "Carrega outra cena (arquivo .json)",
        "Save"           => "Salva o jogo num slot",
        "Load"           => "Carrega o jogo de um slot (negativo = autosave). É o que faz o botão \"Continuar\" do menu",
        "NewGame"        => "Zera variáveis, switches, inventário, quests e o que já aconteceu nas cenas — use no botão \"Novo Jogo\", antes do ChangeScene",
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
        or "Spawn" or "SetWeather" or "Damage" or "Heal"
        or "PlayAnimation" or "StopAnimation" or "ChangeScene" or "PlaySound" or "PlayMusic" or "ShowMessage" or "ShowChoice"
        or "AddItem" or "RemoveItem" or "UseItem" or "SetQuestStage" or "AdvanceQuest" or "SetActive"
        or "ShowUI" or "HideUI" or "ToggleUI" or "If";
    public bool ShowOp => ActionType is "SetVariable" or "If";

    /// <summary>Chance vale pra qualquer ação de verdade, mas não pras de fluxo: um If sorteado
    /// deixaria o Else e o EndIf órfãos e a sequência executaria os dois lados.</summary>
    public bool ShowChance => ActionType.Length > 0 && ActionType is not ("If" or "Else" or "EndIf");
    public bool ShowConditionKind => ActionType == "If";
    public bool ShowSpawnPoint => ActionType == "ChangeScene";
    public bool ShowValue => ActionType is "SetVariable" or "PlaySound" or "PlayMusic" or "Save"
        or "Load" or "AddItem" or "RemoveItem" or "SetQuestStage" or "AdvanceQuest" or "Damage"
        or "Heal" or "If" or "SetWeather";
    public bool ShowOn => ActionType is "SetSwitch" or "PlayMusic" or "SetActive" or "SetPause" or "If";
    public bool ShowXY => ActionType is "Teleport" or "Spawn";

    /// <summary>Alcance só aparece nas ações que miram entidade — é onde "#etiqueta" pode pegar
    /// meio mapa e o campo tem o que limitar.</summary>
    public bool ShowRadius => ActionType is "Damage" or "Heal" or "Destroy" or "Teleport"
        or "PlayAnimation" or "StopAnimation" or "SetActive";
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
        Raise(nameof(ShowRadius));
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
        Raise(nameof(ShowChance));
        Raise(nameof(ShowConditionKind));
        Raise(nameof(ShowSpawnPoint));
        Raise(nameof(OpTypes));
        Raise(nameof(ConditionKind));
        Raise(nameof(NameSuggestions));
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
