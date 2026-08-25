using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Aurora.Editor.Models;

namespace Aurora.Editor.ViewModels;

/// <summary>Componente de uma entidade no inspector.</summary>
public class ComponentViewModel : ViewModelBase
{
    /// <summary>
    /// Campos canônicos dos componentes nativos: aparecem no inspector mesmo quando
    /// ausentes do JSON (o JSON omite valores default). Componentes fora desta lista
    /// mostram só o que o JSON tem — e são preservados no save do mesmo jeito.
    /// </summary>
    private static readonly Dictionary<string, (string Name, object Default)[]> KnownSchemas = new()
    {
        // Parent/InheritRotation por último: são o caso menos comum, e X/Y no topo é o que se
        // procura ao abrir o inspector. Os dois só entram no JSON quando saem do padrão (ver
        // TextPropertyViewModel/BoolPropertyViewModel), então cena sem hierarquia não muda.
        ["Transform"] =
        [
            ("X", 0f), ("Y", 0f), ("Rotation", 0f), ("ScaleX", 1f), ("ScaleY", 1f),
            ("Parent", ""), ("InheritRotation", true),
        ],
        // SizeX/SizeY: tamanho de desenho em pixels de mundo, independente da resolução do PNG
        // (um slime de 178x150 cabe em 28x28 sem redimensionar o arquivo). Os dois em 0 = tamanho
        // natural da textura, mesma convenção de Width/Height do UiImage — e como o ScaleX/ScaleY
        // do Transform multiplica o resultado, os dois continuam funcionando juntos.
        ["SpriteRenderer"] =
        [
            ("Texture", ""), ("Layer", 0f), ("SizeX", 0f), ("SizeY", 0f),
            ("OriginX", 0.5f), ("OriginY", 0.5f),
            ("FlipX", false), ("FlipY", false), ("Visible", true), ("Color", "#FFFFFFFF"),
        ],
        ["Tilemap"] =
        [
            ("Texture", ""), ("TileWidth", 16f), ("TileHeight", 16f),
            ("Width", 0f), ("Height", 0f), ("Layer", 0f),
            ("Color", "#FFFFFFFF"),
            ("SolidTiles", ""),   // índices separados por vírgula, ex: "1, 3, 5"
            // Tileset com linhas de frame (água/lava do LiquidTileset): AnimationFrames = nº
            // de linhas, AnimationColumns = largura de uma linha (0 = linha cheia do tileset).
            ("AnimationFrames", 1f), ("AnimationFrameDuration", 0.15f), ("AnimationColumns", 0f),
        ],
        ["Animator"] =
        [
            ("FrameWidth", 0f), ("FrameHeight", 0f), ("SheetColumns", 1f),
        ],
        ["Collider"] =
        [
            ("Shape", "Box"), ("Width", 16f), ("Height", 16f), ("Radius", 8f),
            ("OffsetX", 0f), ("OffsetY", 0f),
            ("IsSolid", true), ("IsKinematic", false),
            ("Layer", 1f), ("Mask", -1f),
        ],
        // Vida — dano/cura em código via World.Damage/Heal, ou sem código via EventAction
        // "Damage"/"Heal" no EventTrigger.
        ["Health"] =
        [
            ("Max", 100f), ("Current", 100f), ("InvulnerabilityAfterHit", 0f),
            ("Invulnerable", false), ("DestroyOnDeath", true),
        ],
        // Etiquetas do grupo a que a entidade pertence ("inimigo, voador"). É o que as ações de
        // evento miram com "#inimigo" — sem isto, alvo só por nome exato, uma entidade por ação.
        ["Tags"] =
        [
            ("Value", ""),
        ],
        // Efeitos de status ativos. Só precisa ser autorado na cena quando a entidade JÁ NASCE
        // com algum (chefe blindado, bicho de pântano envenenado) — as ações AddStatus criam o
        // componente sozinhas em quem não tem.
        ["Status"] =
        [
            ("Initial", ""),
        ],
        // Congela os Behaviors da entidade enquanto ela estiver fora da vista. Vale pro bicho de
        // enfeite no canto do mapa; NÃO ponha em ninho, chefe ou plataforma móvel que precisa
        // continuar funcionando longe do jogador.
        ["SleepOffscreen"] =
        [
            ("Margin", 256f),
        ],
        // Ataque à distância — Velocity/Source são setados em código no spawn (não fazem
        // sentido numa cena estática), só aparecem os campos abaixo pra editar/prefab.
        ["Projectile"] =
        [
            ("Life", 2f), ("Damage", 20f), ("TargetPrefix", ""),
        ],
        ["CameraController"] =
        [
            ("Follow", ""),
            ("FollowSpeed", 5f), ("Zoom", 1f),
            ("OffsetX", 0f), ("OffsetY", 0f),
            ("ViewWidth", 1280f), ("ViewHeight", 720f),
            ("ClampBounds", false),
            ("BoundsX", 0f), ("BoundsY", 0f),
            ("BoundsWidth", 1280f), ("BoundsHeight", 720f),
        ],
        // Componentes de UI (HUD/menu): X/Y em pixel de tela, não seguem a câmera.
        // Texto suporta tokens {Var}, {Item:Nome}, {Quest:Nome} — ver Aurora.Runtime.UI.UIManager.
        // AnchorX/Y: "Left"/"Top" (padrão, X/Y é canto absoluto — bom pra HUD grudado no canto)
        // | "Center" (X/Y vira deslocamento a partir do centro da tela — bom pra menu, funciona
        // igual em qualquer resolução) | "Right"/"Bottom" (a partir da borda oposta).
        ["UiText"] =
        [
            ("X", 0f), ("Y", 0f), ("AnchorX", "Left"), ("AnchorY", "Top"),
            ("Text", ""), ("Color", "#FFFFFFFF"), ("Scale", 1f),
            // MaxWidth: 0 = sem quebra automática de linha; >0 = largura em pixels de tela.
            ("MaxWidth", 0f),
        ],
        ["UiImage"] =
        [
            ("X", 0f), ("Y", 0f), ("AnchorX", "Left"), ("AnchorY", "Top"),
            ("Texture", ""), ("Width", 0f), ("Height", 0f), ("Color", "#FFFFFFFF"),
        ],
        ["UiBar"] =
        [
            ("X", 0f), ("Y", 0f), ("AnchorX", "Left"), ("AnchorY", "Top"),
            ("Width", 100f), ("Height", 12f),
            ("Variable", ""), ("Max", 100f),
            ("FillColor", "#40C040FF"), ("BackColor", "#303030FF"),
        ],
        ["UiPanel"] =
        [
            ("X", 0f), ("Y", 0f), ("AnchorX", "Left"), ("AnchorY", "Top"),
            ("Width", 100f), ("Height", 100f), ("Color", "#000000AA"),
        ],
        // Botão clicável (mouse/toque) — ações em "OnClick" (editor: UiButtonViewModel).
        // Texture preenchida troca o retângulo colorido pela imagem (Color/HoverColor/PressedColor
        // passam a não valer; o Text continua sendo desenhado por cima). HoverTexture/PressedTexture
        // são opcionais: sem elas o mesmo PNG é clareado no hover e escurecido no clique.
        // Width/Height em 0 com Texture preenchida herdam o tamanho da imagem.
        ["UiButton"] =
        [
            ("X", 0f), ("Y", 0f), ("AnchorX", "Left"), ("AnchorY", "Top"),
            ("Width", 120f), ("Height", 32f), ("Text", "Botão"),
            ("Texture", ""), ("HoverTexture", ""), ("PressedTexture", ""),
            ("Color", "#3A3860FF"), ("HoverColor", "#4A4880FF"), ("PressedColor", "#2A2850FF"),
            ("TextColor", "#FFFFFFFF"),
        ],
        // Joystick virtual (toque multi-dedo) — X/Y+Anchor definem o canto de um quadrado de
        // lado 2*Radius (mesma convenção de posição dos outros Ui*); centro fica no meio dele.
        // Leia UIManager.Find<UiJoystick>(tela, nome).Value em código pra mover o player.
        ["UiJoystick"] =
        [
            ("X", 0f), ("Y", 0f), ("AnchorX", "Left"), ("AnchorY", "Bottom"),
            ("Radius", 70f), ("BaseColor", "#FFFFFF2E"), ("KnobColor", "#FFFFFF66"),
        ],
        // Emissor de partículas (fumaça, faíscas, folhas) — sem Texture desenha quad colorido.
        ["ParticleEmitter"] =
        [
            ("Texture", ""), ("Rate", 10f), ("Emitting", true),
            ("LifeMin", 0.6f), ("LifeMax", 1.2f),
            ("SpeedMin", 20f), ("SpeedMax", 60f),
            ("AngleMin", 0f), ("AngleMax", 360f),
            ("SizeStart", 8f), ("SizeEnd", 0f),
            ("ColorStart", "#FFFFFFFF"), ("ColorEnd", "#FFFFFF00"),
            ("GravityX", 0f), ("GravityY", 0f),
            ("SpawnAreaWidth", 0f), ("SpawnAreaHeight", 0f),
            ("Layer", 0f), ("MaxParticles", 200f),
        ],
        // Luz 2D: brilho aditivo (glow), não é sombra/oclusão dinâmica.
        ["Light2D"] =
        [
            ("Radius", 100f), ("Color", "#FFDC96FF"), ("Intensity", 1f), ("Enabled", true),
        ],
        // Tinta multiplicativa de tela inteira: dia/noite, tempestade, filtro subaquático.
        // Liga/desliga em runtime via EventAction SetActive.
        ["GlobalTint"] =
        [
            ("Color", "#000028FF"), ("Intensity", 0.3f), ("Enabled", true),
        ],
        // NavAgent: com Follow preenchido persegue aquela entidade sozinho (inimigo atrás do
        // jogador, sem script); vazio, o destino vem de SetTarget() em código.
        ["NavAgent"] =
        [
            ("Speed", 100f), ("ArriveThreshold", 4f),
            ("Follow", ""), ("RepathInterval", 0.25f), ("FollowRange", 0f),
            ("Enabled", true),
        ],
        // Perambula em volta de onde nasceu, com pausas — cavalo pastando, galinha, aldeão.
        // Com NavAgent junto, contorna parede; sem, anda reto. O Rideable liga/desliga isto
        // sozinho conforme alguém monta ou desce.
        ["Wander"] =
        [
            ("Radius", 80f), ("Speed", 40f),
            ("PauseMin", 1f), ("PauseMax", 4f), ("ArriveThreshold", 4f),
            ("FlipSpriteByDirection", true), ("AnimatorSpeedParameter", "Speed"),
            ("Enabled", true),
        ],
        // Andar do jogador visto de cima. JoystickScreen/JoystickName ligam num UiJoystick da
        // HUD pro mesmo personagem funcionar no celular; AnimatorSpeedParameter alimenta a
        // transição parado↔andando do Animator.
        ["TopDownController"] =
        [
            ("Movement", "Free"),
            ("Speed", 100f), ("UseKeyboard", true),
            ("JoystickScreen", ""), ("JoystickName", ""),
            ("FlipSpriteByDirection", true), ("AnimatorSpeedParameter", "Speed"),
            ("Enabled", true),
        ],
        // Ataque: instancia Prefab a Distance na direção da mira, respeitando Cooldown. Corpo-a-
        // corpo ou tiro é decidido pelo prefab (Animator+Lifetime vs Projectile), não aqui.
        // AimMode: "Facing" (pra onde anda) | "Mouse". DirectionSnap trava em N direções iguais.
        ["AttackSpawner"] =
        [
            ("Prefab", ""), ("Cooldown", 0.35f), ("Distance", 24f),
            ("AimMode", "Facing"), ("DirectionSnap", 0f),
            ("TriggerKey", ""), ("TriggerMouse", false),
            ("TriggerUiScreen", ""), ("TriggerUiButton", ""),
            ("RotateSpawn", true), ("AttachToAttacker", true), ("ProjectileSpeed", 0f),
            ("Enabled", true),
        ],
        // Machuca quem encostar enquanto o contato durar. TargetPrefix vazio pega qualquer
        // entidade com Health — ponha "Player" num inimigo pra ele não machucar os colegas.
        ["ContactDamage"] =
        [
            ("Damage", 10f), ("Interval", 1f), ("TargetPrefix", ""),
            ("Knockback", 0f), ("DestroySelfOnHit", false),
            ("Enabled", true),
        ],
        // Cola a posição na de outra entidade. FollowSpeed 0 gruda instantâneo.
        ["FollowTarget"] =
        [
            ("TargetName", "Player"), ("OffsetX", 0f), ("OffsetY", 0f),
            ("FollowSpeed", 0f), ("DestroyWhenTargetGone", false),
            ("Enabled", true),
        ],
        // Se destrói sozinho: por tempo e/ou no fim da animação sem loop.
        ["Lifetime"] =
        [
            ("Seconds", 2f), ("DestroyOnAnimationEnd", false),
            ("Enabled", true),
        ],
        // Faz nascer prefabs num ritmo, com teto de vivos ao mesmo tempo — ninho de inimigo,
        // onda, recurso que repõe. MaxAlive 0 = sem teto; TotalLimit 0 = infinito.
        ["Spawner"] =
        [
            ("Prefab", ""), ("Interval", 3f), ("MaxAlive", 5f), ("TotalLimit", 0f),
            ("Radius", 0f), ("StartDelay", 0f),
            ("RequiredSwitch", ""), ("RequiredSwitchOn", true),
            ("Enabled", true),
        ],
        // Jogo de plataforma: anda no eixo X e cai sozinho. CoyoteTime e JumpBufferTime são o
        // que separa um pulo que responde de um que "come" comando; JumpCut dá pulo curto e
        // longo com a mesma tecla. Precisa de Collider sólido — o chão vem da colisão.
        ["PlatformerController"] =
        [
            ("MoveSpeed", 150f), ("Acceleration", 1200f), ("Friction", 1400f), ("AirControl", 0.6f),
            ("Gravity", 1400f), ("JumpSpeed", 420f), ("JumpCut", 0.45f), ("MaxFallSpeed", 600f),
            ("CoyoteTime", 0.1f), ("JumpBufferTime", 0.12f),
            ("JumpKey", "Space"), ("UseKeyboard", true),
            ("JoystickScreen", ""), ("JoystickName", ""), ("JumpButtonName", ""),
            ("FlipSpriteByDirection", true),
            ("AnimatorSpeedParameter", "Speed"), ("AnimatorAirborneParameter", "Airborne"),
            ("Enabled", true),
        ],
        // Veículo com volante: acelera pra onde o bico aponta e vira girando. Carro e nave são o
        // mesmo movimento — muda só o Grip (a nave ignora, e por isso derrapa).
        // O sprite deve apontar pra DIREITA pra bater com Transform.Rotation.
        ["VehicleController"] =
        [
            ("Mode", "Car"),
            ("Acceleration", 420f), ("MaxSpeed", 320f), ("ReverseSpeed", 120f),
            ("TurnSpeed", 180f), ("TurnRequiresMovement", true),
            ("Drag", 260f), ("Grip", 0.9f),
            ("UseKeyboard", true), ("JoystickScreen", ""), ("JoystickName", ""),
            ("AnimatorSpeedParameter", "Speed"),
            ("Enabled", true),
        ],
        // Montaria/veículo em que o jogador entra e sai: transfere o controle entre o passageiro
        // e a montaria. Ponha junto com o controlador de movimento DELA (VehicleController pra
        // carro, TopDownController pra cavalo) — ele fica desligado até alguém montar.
        ["Rideable"] =
        [
            ("InteractKey", "E"), ("Range", 40f), ("RiderName", "Player"),
            ("SeatOffsetX", 0f), ("SeatOffsetY", -8f), ("HideRiderWhileRiding", false),
            ("ExitOffsetX", 24f), ("ExitOffsetY", 0f),
            ("InteractUiScreen", ""), ("InteractUiButton", ""),
            // Assobio: CallKey vazio desliga. CallRange 0 alcança o mapa inteiro.
            ("CallKey", ""), ("CallRange", 0f), ("CallSpeed", 90f), ("CallArriveDistance", 28f),
            ("CallUiScreen", ""), ("CallUiButton", ""),
            ("Enabled", true),
        ],
        // Anda entre pontos fixos: plataforma móvel, ronda de guarda, elevador. Points é
        // "x,y; x,y" RELATIVO à posição inicial, então o mesmo prefab serve a fase inteira.
        ["PatrolPath"] =
        [
            ("Points", "0,0; 64,0"), ("Speed", 60f), ("WaitAtPoint", 0f),
            ("PingPong", true), ("FlipSpriteByDirection", false),
            ("Enabled", true),
        ],
        // Clima de cena: monta o emissor e a tinta sozinho e cola na câmera. Type: None | Rain |
        // Storm | Snow | Fog | Ash. Intensity 0..1 escala partículas e tinta juntas.
        ["Weather"] =
        [
            ("Kind", "Rain"), ("Intensity", 1f), ("Wind", 0f),
            ("Lightning", false), ("LightningMinInterval", 5f), ("LightningMaxInterval", 16f),
            ("ThunderSound", ""), ("Texture", ""), ("Layer", 100f), ("Margin", 120f),
            ("Enabled", true),
        ],
        // Movimento decorativo: gira e/ou balança sozinho. Os dois efeitos somam.
        ["AutoMotion"] =
        [
            ("RotateSpeedDegrees", 0f),
            ("BobAmplitude", 0f), ("BobSpeed", 1f), ("BobAngleDegrees", 90f),
            ("Enabled", true),
        ],
    };

    /// <summary>
    /// Campos de valor fechado: viram ComboBox em vez de texto livre, com rótulo em português e
    /// o valor em inglês que vai pro arquivo. Digitar "Chuva" num campo que espera "Rain" dá uma
    /// cena que carrega sem erro e não faz nada — o pior tipo de bug pra quem está montando fase.
    ///
    /// <para>A chave é <c>Componente.Campo</c> quando a opção só vale ali, ou só <c>Campo</c>
    /// quando vale em qualquer componente (o caso das âncoras de UI, que repetem em seis).</para>
    /// </summary>
    private static readonly Dictionary<string, EnumOption[]> EnumFields = new()
    {
        ["AnchorX"] = [new("Left", "Left"), new("Center", "Center"), new("Right", "Right")],
        ["AnchorY"] = [new("Top", "Top"), new("Center", "Center"), new("Bottom", "Bottom")],

        ["TopDownController.Movement"] =
        [
            new("Livre (analógico)", "Free"),
            new("8 direções", "EightWay"),
            new("4 direções (grade)", "FourWay"),
        ],

        ["VehicleController.Mode"] =
        [
            new("Carro (pneu agarra)", "Car"),
            new("Nave (inércia, derrapa)", "Ship"),
        ],

        ["Weather.Kind"] =
        [
            new("Nenhum", "None"),
            new("Chuva", "Rain"),
            new("Tempestade", "Storm"),
            new("Neve", "Snow"),
            new("Neblina", "Fog"),
            new("Vento", "Wind"),
            new("Tempestade de areia", "Sandstorm"),
            new("Cinzas", "Ash"),
        ],

        ["AttackSpawner.AimMode"] =
        [
            new("Direção do movimento", "Facing"),
            new("Mouse", "Mouse"),
        ],

        ["Collider.Shape"] =
        [
            new("Retângulo", "Box"),
            new("Círculo", "Circle"),
        ],
    };

    public JsonObject Node { get; }
    public string Type { get; }
    public List<PropertyViewModel> Properties { get; } = [];

    /// <summary>Janela principal, quando o componente foi criado com contexto (ver construtor).</summary>
    protected MainViewModel? Owner { get; }

    /// <summary>Definido por EntityViewModel para componentes removíveis (todos exceto Transform).</summary>
    public ICommand? RemoveCommand { get; internal set; }

    public event Action<string>? Edited;

    protected void RaiseEdited(string tag) => Edited?.Invoke($"{Type}.{tag}");

    /// <param name="owner">Janela principal — de onde vêm a lista de assets e o seletor de
    /// arquivo usados pelos campos de textura. Null em testes e em VMs sem contexto.</param>
    public ComponentViewModel(JsonObject node, MainViewModel? owner = null)
    {
        Owner = owner;
        Node = node;
        Type = node["Type"]?.GetValue<string>() ?? "?";

        var added = new HashSet<string> { "Type" };

        if (KnownSchemas.TryGetValue(Type, out var schema))
        {
            foreach (var (name, fallback) in schema)
            {
                AddProperty(name, fallback);
                added.Add(name);
            }
        }

        // Campos presentes no JSON além do esquema (componentes de jogo/plugins).
        // Arrays e objetos (ex.: Tiles do Tilemap) não viram editor de texto.
        foreach (var (name, value) in Node)
        {
            if (added.Contains(name) || value is null)
                continue;

            var kind = value.GetValueKind();
            if (kind is JsonValueKind.Array or JsonValueKind.Object)
                continue;

            object fallback = kind switch
            {
                JsonValueKind.Number => 0f,
                JsonValueKind.True or JsonValueKind.False => false,
                _ => "",
            };
            AddProperty(name, fallback);
        }
    }

    private void AddProperty(string name, object fallback)
    {
        PropertyViewModel property = fallback switch
        {
            float number => new NumberPropertyViewModel(Node, name, number),
            bool flag => new BoolPropertyViewModel(Node, name, flag),
            string text when TryGetEnumOptions(name, out var options)
                => new EnumPropertyViewModel(Node, name, text, options),
            string text when TryGetSuggestions(name, out var source, out string hint)
                => new SuggestPropertyViewModel(Node, name, text, source, hint),
            string text when IsColorField(name, text) => new ColorPropertyViewModel(Node, name, text),
            string when IsTextureField(name) => new TexturePropertyViewModel(Node, name, Owner),
            _ => new TextPropertyViewModel(Node, name, (string)fallback),
        };
        property.Edited += tag => Edited?.Invoke($"{Type}.{tag}");
        Properties.Add(property);
    }

    /// <summary>Opções do campo, procurando primeiro a chave específica do componente e caindo
    /// na genérica. Sem a específica, um campo "Kind" em qualquer componente futuro herdaria a
    /// lista de climas.</summary>
    private bool TryGetEnumOptions(string name, out EnumOption[] options)
        => EnumFields.TryGetValue($"{Type}.{name}", out options!)
           || EnumFields.TryGetValue(name, out options!);

    /// <summary>
    /// Campos que apontam pra algo DO PROJETO: viram caixa de texto com sugestão, não lista
    /// fechada. O alvo pode não existir ainda (a entidade que só nasce em jogo, o prefab que você
    /// vai criar depois), então travar nas opções seria pior que o texto solto — mas mostrar o
    /// que já existe evita o erro de digitação que só aparece jogando.
    ///
    /// <para>Chave em <c>Componente.Campo</c>; valor descreve de onde vêm as sugestões e a dica
    /// mostrada embaixo do campo.</para>
    /// </summary>
    private static readonly Dictionary<string, (string Source, string Hint)> SuggestFields = new()
    {
        ["CameraController.Follow"] = ("entities", "entidade que a câmera segue"),
        ["NavAgent.Follow"] = ("entities", "entidade perseguida — vazio = só SetTarget() por código"),
        ["FollowTarget.TargetName"] = ("entities", "entidade em que esta gruda"),
        ["Transform.Parent"] = ("entities", "entidade que carrega esta — vazio = solta no mundo"),
        ["ContactDamage.TargetPrefix"] = ("targets", "#etiqueta, ou prefixo de nome — vazio = qualquer um com Health"),
        ["Projectile.TargetPrefix"] = ("targets", "#etiqueta, ou prefixo de nome — vazio = qualquer um com Health"),
        ["Tags.Value"] = ("tags", "etiquetas separadas por vírgula (inimigo, voador) — mire com #inimigo nas ações"),
        ["Status.Initial"] = ("status", "status com que já nasce, separados por vírgula — vazio = nenhum"),

        ["Spawner.Prefab"] = ("prefabs", "prefab ou id de tabela de spawn (sorteia entre vários)"),
        ["AttackSpawner.Prefab"] = ("prefabs", "prefab do golpe/projétil, ou id de tabela de spawn"),

        ["TopDownController.JoystickScreen"] = ("uiScreens", "tela de UI do joystick — vazio = só teclado"),
        ["AttackSpawner.TriggerUiScreen"] = ("uiScreens", "tela de UI do botão de ataque"),
        ["TopDownController.JoystickName"] = ("uiElements", "nome do UiJoystick dentro da tela"),
        ["AttackSpawner.TriggerUiButton"] = ("uiElements", "nome do UiButton dentro da tela"),

        ["AttackSpawner.TriggerKey"] = ("keys", "tecla que dispara — vazio = nenhuma"),
        ["PlatformerController.JumpKey"] = ("keys", "tecla de pulo — vazio = só o botão de toque"),
        ["PlatformerController.JoystickScreen"] = ("uiScreens", "tela de UI do joystick e do botão de pulo"),
        ["PlatformerController.JoystickName"] = ("uiElements", "nome do UiJoystick dentro da tela"),
        ["PlatformerController.JumpButtonName"] = ("uiElements", "nome do UiButton de pulo dentro da tela"),
        ["VehicleController.JoystickScreen"] = ("uiScreens", "tela de UI do joystick — vazio = só teclado"),
        ["VehicleController.JoystickName"] = ("uiElements", "nome do UiJoystick: Y acelera, X vira"),
        ["Rideable.InteractKey"] = ("keys", "tecla de entrar/sair — vazio = só botão de toque"),
        ["Rideable.RiderName"] = ("entities", "quem pode montar (nome exato, não prefixo)"),
        ["Rideable.InteractUiScreen"] = ("uiScreens", "tela de UI do botão de entrar/sair"),
        ["Rideable.InteractUiButton"] = ("uiElements", "nome do UiButton de entrar/sair"),
        ["Rideable.CallKey"] = ("keys", "tecla do assobio — vazio = sem chamado"),
        ["Rideable.CallUiScreen"] = ("uiScreens", "tela de UI do botão de assobiar"),
        ["Rideable.CallUiButton"] = ("uiElements", "nome do UiButton de assobiar"),
        ["Weather.ThunderSound"] = ("sounds", "som do trovão — vazio = mudo"),
        ["Spawner.RequiredSwitch"] = ("switches", "só nasce com este switch no estado abaixo — vazio = sempre"),
    };

    private bool TryGetSuggestions(string name, out Func<IEnumerable<string>> source, out string hint)
    {
        source = () => [];
        hint = "";

        if (!SuggestFields.TryGetValue($"{Type}.{name}", out var entry))
            return false;

        hint = entry.Hint;
        var owner = Owner;

        source = entry.Source switch
        {
            "entities" => () => owner?.EntityNames ?? [],
            // Campo de alvo aceita as duas linguagens: nome (prefixo) e etiqueta. A lista junta
            // as duas porque quem preenche está escolhendo QUEM apanha, não que sintaxe usar.
            "targets" => () => (owner?.TagNames.Select(t => "#" + t) ?? []).Concat(owner?.EntityNames ?? []),
            "tags" => () => owner?.TagNames ?? [],
            "status" => () => owner?.StatusIds ?? [],
            "prefabs" => () => owner?.PrefabOrTableNames ?? [],
            "uiScreens" => () => owner?.UiScreenIds ?? [],
            "uiElements" => () => owner?.UiElementNames ?? [],
            "sounds" => () => owner?.SoundAssets ?? [],
            "keys" => () => MainViewModel.KeyNames,
            // Switches não têm cadastro: existem por serem usados. Sem fonte confiável, some a
            // sugestão em vez de mostrar uma lista vazia que parece defeito.
            _ => () => [],
        };

        return true;
    }

    /// <summary>
    /// Decide se o campo abre o seletor de cores em vez de uma caixa de texto: vale para os
    /// campos dos esquemas nativos cujo padrão é hex (Color, TextColor, ColorStart…) e para
    /// qualquer campo de script cujo valor no JSON seja um hex válido — assim um
    /// <c>public string Cor = "#FF0000FF"</c> num script seu também ganha a paleta, sem
    /// precisar chamar o campo de "Color".
    /// </summary>
    private bool IsColorField(string name, string fallback)
        => fallback.StartsWith('#')
           || (Node[name]?.GetValueKind() == JsonValueKind.String
               && EngineColor.TryParse(Node[name]!.GetValue<string>(), out _));

    /// <summary>
    /// Campo que guarda caminho de imagem — abre o seletor de assets em vez de caixa de texto.
    /// Pega `Texture` (SpriteRenderer, Tilemap, UiImage, UiButton) e as variantes do botão
    /// (`HoverTexture`, `PressedTexture`), inclusive num script seu que tenha campo terminado
    /// em "Texture".
    /// </summary>
    private static bool IsTextureField(string name)
        => name.EndsWith("Texture", StringComparison.OrdinalIgnoreCase);

    public NumberPropertyViewModel? Number(string name)
        => Properties.OfType<NumberPropertyViewModel>().FirstOrDefault(p => p.Name == name);

    public TextPropertyViewModel? Text(string name)
        => Properties.OfType<TextPropertyViewModel>().FirstOrDefault(p => p.Name == name);

    // Leitura direta do nó para renderização do canvas (sem passar pelos VMs de propriedade).
    public float GetFloat(string name, float fallback) => PropertyViewModel.ReadFloat(Node[name],    fallback);
    public string? GetString(string name) => Node[name]?.GetValue<string>();
    public bool GetBool(string name, bool fallback) => Node[name]?.GetValue<bool>() ?? fallback;
}
