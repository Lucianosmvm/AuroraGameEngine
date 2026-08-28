using Aurora.Runtime.Assets;
using Aurora.Runtime.Audio;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Events;
using Aurora.Runtime.Input;
using Aurora.Runtime.Net;
using Aurora.Runtime.Saves;
using Aurora.Runtime.Scenes;
using Aurora.Runtime.UI;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Aurora.Runtime;

/// <summary>
/// Classe base de um jogo Aurora: roda o loop update/render sobre um <see cref="IView"/> —
/// janela no desktop, view SDL no Android. Herde e implemente <see cref="OnLoad"/>.
/// </summary>
public abstract class Game : IDisposable
{
    /// <summary>Superfície onde o jogo roda. No desktop também é um <see cref="IWindow"/>.</summary>
    public IView View { get; private set; } = null!;

    /// <summary>Janela desktop, ou null quando rodando em view mobile.</summary>
    public IWindow? Window => View as IWindow;

    public GL Gl { get; private set; } = null!;
    public InputManager Input { get; private set; } = null!;
    public SpriteBatch SpriteBatch { get; private set; } = null!;
    public AssetManager Assets { get; private set; } = null!;
    public AudioManager Audio { get; private set; } = null!;
    public SceneManager SceneManager { get; private set; } = null!;
    public SaveManager Save { get; private set; } = null!;

    /// <summary>
    /// Nome do jogo — define a pasta de save em %LocalAppData%/[GameName]/saves/.
    /// Defina antes de <see cref="Run"/> se quiser um nome personalizado.
    /// </summary>
    public string GameName { get; set; } = "AuroraGame";

    /// <summary>Tamanho do framebuffer em cache — atualizado só no load e no evento de resize
    /// (View.FramebufferResize), NUNCA lido direto no meio do loop de update/render. No Android,
    /// ler View.FramebufferSize repetidamente por frame (visto: 1x virou 3x quando UI.Update/Draw
    /// passaram a precisar do tamanho de tela pro sistema de Anchor) parece interagir mal com o
    /// resize handling interno do Silk.NET/SDL — crash real em device: "You cannot call `Reset`
    /// inside of the render loop!" logo na abertura. Ler o cache em vez da propriedade evita isso.</summary>
    public Vector2D<int> ScreenSize { get; private set; }

    /// <summary>
    /// Resolução fixa de referência (ex.: 1280x720) pra manter enquadramento consistente em
    /// qualquer tamanho de janela/aparelho — sem isso (padrão, null) a câmera/UI usam o
    /// framebuffer real e a cena mostra mais ou menos mundo dependendo da tela (comportamento
    /// de sempre, sem mudança). Setando isso antes de <see cref="Run(IView)"/>, o viewport de
    /// GL vira uma barra centralizada com a proporção certa (letterbox/pillarbox) e
    /// ScreenSize/Camera/toque passam a usar essa resolução fixa, não o tamanho físico.
    /// </summary>
    public Vector2D<int>? DesignResolution { get; set; }

    public Camera2D Camera { get; } = new();
    public World World { get; } = new();
    public SceneSerializer Scenes { get; } = new();
    public GameState State { get; } = new();
    public InventoryManager Inventory { get; } = new();
    public QuestManager Quests { get; } = new();

    /// <summary>O que já aconteceu com as entidades marcadas com Persistent, por cena (inimigo
    /// morto, baú aberto). Vale na mesma partida e atravessa o save.</summary>
    public Saves.SceneStateStore SceneState { get; } = new();

    /// <summary>Catálogo de itens (nome, ícone, preço, efeito ao usar). Carregado sozinho de
    /// <c>Assets/database/items.json</c> no boot, se o arquivo existir — jogo sem itens não
    /// precisa criar nada.</summary>
    public Database.ItemDatabase Items { get; } = new();

    /// <summary>Tabelas de spawn (grupos de prefabs sorteados por peso, com condição). Carregadas
    /// de <c>Assets/database/spawns.json</c>. Onde se escreve um prefab também se pode escrever o
    /// id de uma tabela.</summary>
    public Database.SpawnTableDatabase SpawnTables { get; } = new();

    /// <summary>Eventos comuns: sequências de ações cadastradas por id, chamadas pela ação
    /// CallEvent de qualquer cena, item ou botão de UI. Carregados de
    /// <c>Assets/database/common_events.json</c>.</summary>
    public Database.CommonEventDatabase CommonEvents { get; } = new();

    /// <summary>Efeitos de status (veneno, lentidão, blindagem). Carregados de
    /// <c>Assets/database/status.json</c>; aplicados pelas ações AddStatus/RemoveStatus.</summary>
    public Database.StatusDatabase StatusDatabase { get; } = new();

    /// <summary>Listas de categorias do jogo (ItemTypes e as que o seu jogo inventar). Carregadas
    /// de <c>Assets/database/types.json</c>.</summary>
    public Database.TypeDatabase Types { get; } = new();

    /// <summary>Textos de interface ("Comprar", "Sair"…). Carregados de
    /// <c>Assets/database/terms.json</c>; sem o arquivo valem os padrões em português.</summary>
    public Database.TermDatabase Terms { get; } = new();
    public DialogueSystem Dialogue { get; } = new();
    public UIManager UI { get; } = new();
    public EventSystem Events { get; }

    /// <summary>
    /// Multiplayer local (LAN). Offline até alguém chamar <c>Net.StartHost()</c> ou
    /// <c>Net.Join(ip)</c> — nenhuma porta é aberta em jogo single player.
    /// </summary>
    public NetSession Net { get; } = new();

    protected Game()
    {
        Events = new EventSystem(World, State)
        {
            Dialogue = Dialogue, Inventory = Inventory, Quests = Quests, UI = UI, Items = Items,
            CommonEvents = CommonEvents, Status = StatusDatabase, Terms = Terms,
        };
    }

    public Color ClearColor { get; set; } = Color.CornflowerBlue;

    /// <summary>
    /// Teto do deltaTime entregue a update/física, em segundos (padrão 0,05 = 20 FPS).
    /// Sem esse teto, qualquer travada longa (alt-tab, breakpoint no debugger, load de cena
    /// pesado, app voltando do background no Android) entrega um dt gigante num único frame:
    /// a integração de posição salta centenas de pixels de uma vez e o passo de colisão —
    /// que testa sobreposição na posição JÁ atualizada, não ao longo do caminho — simplesmente
    /// não vê a parede no meio (tunneling). Com o teto, o jogo desacelera em vez de teleportar.
    /// </summary>
    public float MaxDeltaTime { get; set; } = 0.05f;

    /// <summary>
    /// Cena passada pelo editor via --scene. Use em <see cref="OnLoad"/>:
    /// <c>LoadScene(BootScene ?? "scenes/inicio.json");</c>
    /// </summary>
    protected string? BootScene { get; private set; }

    private string? _describeScriptsOutputPath;

    /// <summary>
    /// Processa argumentos de linha de comando. Chame antes de <see cref="Run"/>.
    /// <para>Argumentos reconhecidos: <c>--scene &lt;caminho&gt;</c>,
    /// <c>--describe-scripts &lt;arquivo&gt;</c> (usado pelo editor pra descobrir
    /// scripts [SceneScript] sem abrir janela).</para>
    /// </summary>
    public void ParseArgs(string[] args)
    {
        // Percorre TODOS os argumentos (não args.Length - 1): "--debug" não tem valor, e com o
        // laço parando um antes ele era ignorado quando vinha por último.
        for (int i = 0; i < args.Length; i++)
        {
            bool hasValue = i + 1 < args.Length;

            if (args[i] == "--debug")
                DebugOverlayEnabled = true;
            else if (args[i] == "--scene" && hasValue)
                BootScene = args[i + 1];
            else if (args[i] == "--describe-scripts" && hasValue)
                _describeScriptsOutputPath = args[i + 1];
            else if (args[i] == "--debug-font" && hasValue)
                _debugFontPath = args[i + 1];
        }
    }

    private string? _debugFontPath;

    /// <summary>Overlay de diagnóstico ligado (<c>--debug</c>): hitbox de cada Collider por cima
    /// da cena e FPS/contagens no canto. Também dá pra ligar em código, antes do Run.</summary>
    public bool DebugOverlayEnabled { get; set; }

    /// <summary>Estado do overlay. Público pra dar pra ajustar (espessura da linha) ou desenhar
    /// coisa extra a partir do jogo.</summary>
    public DebugOverlay Debug { get; } = new();

    /// <summary>Fonte do texto do overlay. Sem ela o overlay ainda desenha as hitboxes — só o
    /// bloco de números fica de fora. Carregada de <c>--debug-font</c>, que o editor preenche
    /// com o uiFont do projeto: o runtime não tem fonte embutida nem sabe do aurora.project.json.</summary>
    private Font? _debugFont;

    /// <summary>Origem dos assets. Defina antes de Run (Android: AndroidAssetSource). Null = pasta "Assets".</summary>
    public IAssetSource? AssetSource { get; set; }

    /// <summary>Desktop: cria uma janela e bloqueia até o jogo fechar.</summary>
    public void Run(string title = "Aurora Game", int width = 1280, int height = 720, bool vsync = true)
    {
        if (_describeScriptsOutputPath is { } outputPath)
        {
            DescribeScriptsAndWrite(outputPath);
            return;
        }

        var options = WindowOptions.Default with
        {
            Title = title,
            Size = new Vector2D<int>(width, height),
            VSync = vsync,
        };

        var window = Silk.NET.Windowing.Window.Create(options);
        Run(window);
        window.Dispose();
    }

    /// <summary>
    /// Varre o assembly do jogo por [SceneScript] e escreve nome+campos em JSON no arquivo
    /// indicado, sem criar janela. Não passa por HandleLoad — não precisa de GL/janela pra
    /// só ler reflection.
    /// </summary>
    private void DescribeScriptsAndWrite(string outputPath)
    {
        var scripts = Scenes.DescribeScripts(GetType().Assembly);
        File.WriteAllText(outputPath, System.Text.Json.JsonSerializer.Serialize(scripts));
    }

    /// <summary>Roda sobre uma view já criada (Android: obtida via Window.GetView na Activity).</summary>
    public void Run(IView view)
    {
        View = view;
        View.Load += HandleLoad;
        View.Update += HandleUpdate;
        View.Render += HandleRender;
        View.FramebufferResize += HandleResize;
        View.Closing += HandleClosing;

        View.Run();
    }

    /// <summary>
    /// O que fazer quando o jogo pede pra sair, pra quem hospeda o jogo sem janela própria
    /// (play-in-editor: sair = parar o modo Play, não fechar o editor). Null = fecha a View,
    /// que é o comportamento de um jogo publicado.
    /// </summary>
    public Action? ExitHandler { get; set; }

    /// <summary>Fecha a view e encerra o loop.</summary>
    public void Exit()
    {
        if (ExitHandler is { } handler)
            handler();
        else
            View.Close();
    }

    // ---------------------------------------------------------------------------------------
    // Ciclo de vida sem janela própria
    //
    // Um jogo publicado cria a própria janela e o Silk.NET chama estes quatro passos por evento
    // (ver Run/HandleLoad). O play-in-editor não pode fazer isso: quem é dono do contexto de GL
    // e do loop de frame é o editor. Então os passos são públicos e o caminho com janela virou
    // casca fina sobre eles — os dois modos correm exatamente o mesmo código, e nada aqui muda
    // pra quem só chama Run().
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Monta o jogo sobre um contexto de GL que outro alguém criou e é dono.
    /// </summary>
    /// <param name="gl">Contexto já corrente na thread que vai renderizar.</param>
    /// <param name="input">Input a usar; hospedado, é um <see cref="InputManager"/> sem
    /// dispositivo, alimentado pelo host.</param>
    /// <param name="gles">True se o contexto é OpenGL ES — troca o dialeto do shader. O editor
    /// no Windows recebe GLES (ANGLE), o mesmo caso do Android.</param>
    /// <param name="framebufferSize">Tamanho da superfície de destino, em pixels.</param>
    public void Initialize(GL gl, InputManager input, bool gles, Vector2D<int> framebufferSize)
    {
        Gl = gl;
        Input = input;
        SpriteBatch = new SpriteBatch(Gl, gles);

        SetUpSystems();

        Gl.Enable(EnableCap.Blend);
        Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        ApplyViewport(framebufferSize);

        AutoRegisterScripts();
        LoadDebugFont();
        OnLoad();
    }

    /// <summary>Avança um frame de lógica. Exceção de um frame é logada e o frame ignorado —
    /// o jogo não fecha sozinho.</summary>
    public void Tick(float deltaTime)
    {
        // Antes de tudo: um Load pedido no frame passado (botão "Continuar" do menu, evento)
        // troca a cena inteira. Aqui é o único ponto do frame em que nada está iterando o World.
        if (_pendingLoadSlot is { } pendingSlot)
        {
            _pendingLoadSlot = null;
            ApplyPendingLoad(pendingSlot);
        }

        float dt = MathF.Min(deltaTime, MaxDeltaTime);
        Input.BeginFrame();

        if (DebugOverlayEnabled)
            Debug.Tick(dt);

        // World.Update já isola exceções por behavior; esse try/catch é a rede de segurança
        // pro resto (OnUpdate do seu Game, SceneManager, Events, UI) — pior caso, um frame de
        // lógica é ignorado e logado, o jogo não fecha sozinho.
        try
        {
            // Antes de tudo: quem entrou/saiu precisa estar refletido já neste frame, senão a
            // lógica roda um frame inteiro com uma lista de jogadores desatualizada.
            Net.Update(dt);
            SceneManager.Update(dt);
            Dialogue.Update();
            AdvanceDialogueInput();
            OnUpdate(dt);
            World.Update(dt);
            Events.Update(dt);
            UI.Update(Input, Events, ScreenSize.X, ScreenSize.Y);
            UpdateCamera(dt);

            // Por último de propósito: realimenta a fila de streaming da música. Se a lógica
            // do jogo estourar acima, o frame de áudio se perde junto — mas a fila tem folga
            // de quase um segundo, então um frame ruim não corta o som.
            Audio.Update();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Game] Exceção não tratada em Tick — frame ignorado: {ex}");
        }
    }

    /// <summary>Reajusta viewport e câmera a um novo tamanho de superfície.</summary>
    public void Resize(Vector2D<int> framebufferSize) => ApplyViewport(framebufferSize);

    /// <summary>
    /// Libera os recursos que dependem do contexto de GL. Tem que rodar com o contexto AINDA
    /// vivo — por isso é chamado no Closing da view, não no Dispose.
    /// </summary>
    public void Shutdown()
    {
        OnUnload();

        // Antes do resto: avisa o outro lado que saímos. Fechar o socket calado deixaria os
        // outros jogadores olhando pro nosso boneco parado até o timeout estourar.
        Net.Dispose();
        Audio?.Dispose();
        Assets?.Dispose();
        SpriteBatch?.Dispose();
    }

    /// <summary>
    /// Carrega uma cena, limpando o mundo atual. Para transição com fade use
    /// <see cref="SceneManager.LoadWithFade"/>.
    /// </summary>
    public void LoadScene(string scenePath) => SceneManager.Load(scenePath);

    /// <summary>Fade para preto, troca de cena e fade de volta.</summary>
    public void LoadSceneWithFade(string scenePath, float duration = 0.3f)
        => SceneManager.LoadWithFade(scenePath, duration);

    private void HandleLoad()
        => Initialize(
            GL.GetApi(View),
            new InputManager(View.CreateInput()),
            View.API.API == ContextAPI.OpenGLES,
            View.FramebufferSize);

    /// <summary>Liga os subsistemas uns nos outros. Nada aqui toca janela ou input — é a parte
    /// comum entre o jogo publicado e o hospedado.</summary>
    private void SetUpSystems()
    {
        var source = AssetSource ?? new FileAssetSource();
        Assets = new AssetManager(Gl, source);
        Audio = new AudioManager(source);
        Events.Audio = Audio;
        Events.Input = Input;

        SceneManager = new SceneManager(World, Scenes, Events, Dialogue, Assets);
        Events.SceneChangeRequested += (path, spawnPoint) =>
            SceneManager.LoadWithFade(path, spawnPoint: spawnPoint);

        // Uma fonte de verdade pra "quem é o jogador": o EventSystem usa no gatilho PlayerTouch,
        // o SceneManager em qual entidade vai pro marcador de spawn na troca de cena.
        SceneManager.PlayerEntityName = Events.PlayerEntityName;
        Events.QuitRequested += Exit;

        Save = new SaveManager(State, SceneManager, World, GameName, Inventory, Quests, SceneState);
        Events.Save = Save;

        // Só anota o pedido: executar aqui trocaria a cena (World.Clear) no meio da varredura
        // de gatilhos que disparou a ação. Ver HandleUpdate.
        Events.LoadRequested += slot => _pendingLoadSlot = slot;

        // Disponibiliza os sistemas do Game pra qualquer Behavior via World?.Input / World?.State
        // / etc — sem isso, cada script precisaria de um campo público + injeção manual repetida
        // em Game.OnUpdate (World já chega de graça em todo Behavior, isso só estende o mesmo canal).
        World.Input = Input;
        World.State = State;
        World.SceneState = SceneState;
        // Teto de pilha e tokens {ItemName:…} do UiText: os dois leem a ficha do item, e sem
        // estas duas linhas os campos MaxStack/Name/Description/Price ficam cadastrados sem
        // ninguém no jogo que os consulte.
        Inventory.Database = Items;
        UI.Items = Items;
        UI.Terms = Terms;
        // Espelhamento de UiTextInput.Variable: sem isto um campo com Variable preenchido não
        // guardaria nada, e a falha seria muda — o campo digita normalmente, o valor só não chega.
        UI.State = State;
        World.StatusDatabase = StatusDatabase;

        // Retrato de ShowMessage/cutscene: sem isto DialogueSystem.Assets fica null e o campo
        // Portrait é ignorado em silêncio (a caixa desenha sem imagem, nunca quebra).
        Dialogue.Assets = Assets;

        World.Inventory = Inventory;
        World.Quests = Quests;
        World.Dialogue = Dialogue;
        World.UI = UI;
        World.Audio = Audio;
        World.Save = Save;
        World.Camera = Camera;
        World.Assets = Assets;

        // Banco de itens é opcional: jogo sem item nenhum não precisa do arquivo. Erro de sintaxe
        // no JSON, porém, é avisado — silêncio aqui viraria "por que meu item não faz nada?".
        LoadDatabase(Database.ItemDatabase.DefaultPath, "itens", Items.Load);
        LoadDatabase(Database.SpawnTableDatabase.DefaultPath, "tabelas de spawn", SpawnTables.Load);
        LoadDatabase(Database.CommonEventDatabase.DefaultPath, "eventos comuns", CommonEvents.Load);
        LoadDatabase(Database.StatusDatabase.DefaultPath, "status", StatusDatabase.Load);
        LoadDatabase(Database.TypeDatabase.DefaultPath, "tipos", Types.Load);
        LoadDatabase(Database.TermDatabase.DefaultPath, "termos", Terms.Load);
        WarnAboutUnknownItemTypes();

        // World.Spawn("prefabs/slime.json", pos) e a ação de evento Spawn passam por aqui.
        // Erro de arquivo/JSON não derruba o jogo: loga e devolve null, mesma política do
        // load de cena — um prefab com nome errado não pode matar o frame inteiro.
        World.PrefabFactory = (nameOrPath, position) =>
        {
            // A tradução mora aqui, no único caminho por onde todo spawn passa: assim a ação
            // Spawn, o Spawner, o AttackSpawner e qualquer script ganham id de tabela e sorteio
            // de graça, sem nenhum deles precisar saber que tabelas existem.
            string? path = SpawnTables.Resolve(nameOrPath, Events.TestCondition);

            // Tabela existe mas nenhuma entrada passou na condição — "de dia não nasce zumbi".
            // Não é erro: é a resposta certa, e nada nasce.
            if (path is null)
                return null;

            try
            {
                return Scenes.LoadEntity(Assets.LoadText(path),
                    new Scenes.SceneContext { World = World, Assets = Assets }, position);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Game] Falha ao instanciar prefab '{path}': {ex.Message}");
                return null;
            }
        };

        // Só aqui, e não no construtor: a sincronização precisa do World já apontando pros
        // sistemas do Game, porque as fábricas de prefab montam entidades completas (sprite,
        // áudio, script) na hora que um jogador entra.
        // Identificador da busca por salas: dois jogos Aurora diferentes na mesma rede não
        // devem aparecer um na lista do outro.
        Net.GameId = GameName;
        Net.AttachWorld(World);
    }

    /// <summary>
    /// Varre o assembly do jogo em busca de classes marcadas com <c>[SceneScript]</c> e
    /// registra cada uma automaticamente no serializador de cena — sem precisar chamar
    /// <c>Scenes.Register</c> na mão nem escrever leitura/escrita de JSON campo a campo.
    /// GetType().Assembly (não Assembly.GetEntryAssembly()) — no Android a Activity não tem
    /// entry point tradicional e GetEntryAssembly() pode voltar null, silenciando TODOS os
    /// scripts custom sem erro nenhum (só os componentes nativos continuam funcionando).
    /// </summary>
    private void AutoRegisterScripts()
    {
        Scenes.RegisterScripts(GetType().Assembly);
    }

    /// <summary>Carrega a fonte do overlay, se pedida. Falha aqui NÃO derruba o jogo: um
    /// caminho de fonte errado no --debug-font não pode virar crash de um jogo que rodaria bem
    /// sem a flag.</summary>
    private void LoadDebugFont()
    {
        if (!DebugOverlayEnabled || _debugFontPath is null)
            return;

        try
        {
            _debugFont = Assets.LoadFont(_debugFontPath, 16f);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Game] --debug-font '{_debugFontPath}' não carregou ({ex.Message}) — overlay sem texto.");
        }
    }

    private void HandleUpdate(double deltaTime) => Tick((float)deltaTime);

    /// <summary>Lê o input padrão da caixa de diálogo — Espaço/Enter dispensa a mensagem ou
    /// confirma a opção selecionada, W/S e as setas navegam entre opções. Sem isto o jogo
    /// precisaria mapear essas teclas na mão em cada projeto pra a caixa fechar sozinha; aqui
    /// funciona pra qualquer EventTrigger → ShowMessage/ShowChoice sem nenhum código.</summary>
    private void AdvanceDialogueInput()
    {
        if (!Dialogue.IsActive)
            return;

        if (Input.WasKeyPressed(Key.Space) || Input.WasKeyPressed(Key.Enter) || Input.WasKeyPressed(Key.KeypadEnter))
            Dialogue.Advance();
        else if (Input.WasKeyPressed(Key.Up) || Input.WasKeyPressed(Key.W))
            Dialogue.SelectPrevious();
        else if (Input.WasKeyPressed(Key.Down) || Input.WasKeyPressed(Key.S))
            Dialogue.SelectNext();
    }

    private int? _pendingLoadSlot;

    /// <summary>Executa o Load pedido pela ação. Slot negativo = autosave. Slot vazio não é erro
    /// de programação — é um jogador clicando "Continuar" antes de ter salvado alguma vez: loga
    /// e segue, com o jogo intacto.</summary>
    private void ApplyPendingLoad(int slot)
    {
        bool loaded = slot < 0 ? Save.LoadAutoSave() : Save.Load(slot);

        if (!loaded)
            Console.Error.WriteLine(
                $"[Game] Ação Load: não há save {(slot < 0 ? "automático" : $"no slot {slot}")} — nada foi carregado.");
    }

    private void UpdateCamera(float dt)
    {
        foreach (var (_, transform, ctrl) in World.Query<Transform, CameraController>())
        {
            var target = ctrl.Follow is not null
                && World.TryFind(ctrl.Follow, out var followEntity)
                && followEntity.Get<Transform>() is { } ft
                    ? ft.Position
                    : transform.Position;

            target += ctrl.Offset;

            if (ctrl.FollowSpeed > 0f)
                Camera.Follow(target, ctrl.FollowSpeed, dt);
            else
                Camera.Position = target;

            Camera.Zoom = ctrl.Zoom;

            if (ctrl.ClampBounds)
                ClampCameraToBounds(ctrl);

            break; // apenas a primeira câmera ativa conta
        }
    }

    /// <summary>
    /// Trava a câmera dentro dos limites, pra ela não mostrar o vazio além da borda do mapa.
    ///
    /// <para>Com <c>BoundsWidth</c>/<c>BoundsHeight</c> em 0, os limites saem dos tilemaps da cena
    /// — mapa grande passa a travar sozinho, sem ninguém redigitar o tamanho do mapa aqui toda vez
    /// que ele cresce (e sem o limite envelhecer calado quando alguém esquece).</para>
    ///
    /// <para>Quando o limite é MENOR que a tela (sala pequena, mapa de teste), não há o que
    /// clampear: a câmera fica no meio dele. Antes essa conta chamava Math.Clamp com mínimo maior
    /// que o máximo, que joga ArgumentException — todo frame, derrubando o jogo.</para>
    /// </summary>
    private void ClampCameraToBounds(CameraController ctrl)
    {
        float x = ctrl.BoundsX, y = ctrl.BoundsY;
        float width = ctrl.BoundsWidth, height = ctrl.BoundsHeight;

        if (width <= 0f || height <= 0f)
        {
            if (World.TilemapWorldBounds() is not { } bounds)
                return;

            x = bounds.Min.X;
            y = bounds.Min.Y;
            width = bounds.Max.X - bounds.Min.X;
            height = bounds.Max.Y - bounds.Min.Y;
        }

        float halfWidth = Camera.ViewportWidth / (2f * MathF.Max(Camera.Zoom, 0.001f));
        float halfHeight = Camera.ViewportHeight / (2f * MathF.Max(Camera.Zoom, 0.001f));

        float minX = x + halfWidth, maxX = x + width - halfWidth;
        float minY = y + halfHeight, maxY = y + height - halfHeight;

        Camera.Position = new System.Numerics.Vector2(
            minX <= maxX ? Math.Clamp(Camera.Position.X, minX, maxX) : x + width / 2f,
            minY <= maxY ? Math.Clamp(Camera.Position.Y, minY, maxY) : y + height / 2f);
    }

    private void HandleRender(double deltaTime) => RenderFrame((float)deltaTime);

    /// <summary>Desenha um frame no framebuffer que estiver ligado. Não faz bind de framebuffer
    /// nem troca buffer: hospedado, o destino é o do editor, e quem apresenta é ele.</summary>
    public void RenderFrame(float deltaTime)
    {
        Gl.ClearColor(ClearColor.R, ClearColor.G, ClearColor.B, ClearColor.A);
        Gl.Clear(ClearBufferMask.ColorBufferBit);

        // try/finally garante o End() mesmo se World.Render/OnRender explodir — sem isso o
        // SpriteBatch fica com Begin() pendente e o PRÓXIMO frame também quebra (Begin()
        // chamado duas vezes sem End()), transformando 1 exceção em crash permanente.
        SpriteBatch.Begin(Camera.GetViewProjection());
        try
        {
            World.Render(SpriteBatch, Camera);
            OnRender(deltaTime);

            // Depois do OnRender: hitbox tem que ficar POR CIMA do que o jogo desenhou, senão
            // o sprite tapa justamente o que se quer conferir.
            if (DebugOverlayEnabled)
                Debug.DrawColliders(SpriteBatch, World);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Game] Exceção não tratada em RenderFrame (mundo) — frame ignorado: {ex}");
        }
        finally
        {
            SpriteBatch.End();
        }

        // Passe de UI em coordenadas de tela (HUD, diálogos) — não segue a câmera.
        SpriteBatch.Begin(GetScreenProjection());
        try
        {
            World.DrawGlobalTint(SpriteBatch, ScreenSize.X, ScreenSize.Y);
            OnRenderUI(deltaTime);
            SceneManager.DrawOverlay(SpriteBatch, ScreenSize.X, ScreenSize.Y);

            // Por último: os números ficam legíveis mesmo com HUD e fade de troca de cena.
            if (DebugOverlayEnabled && _debugFont is not null)
                Debug.DrawStats(SpriteBatch, _debugFont, World, SceneManager.CurrentScene ?? "(nenhuma)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Game] Exceção não tratada em RenderFrame (UI) — frame ignorado: {ex}");
        }
        finally
        {
            SpriteBatch.End();
        }
    }

    /// <summary>Projeção em pixels de tela: (0,0) no canto superior esquerdo.</summary>
    public System.Numerics.Matrix4x4 GetScreenProjection()
        => System.Numerics.Matrix4x4.CreateOrthographicOffCenter(
            0f, ScreenSize.X, ScreenSize.Y, 0f, -1f, 1f);

    private void HandleResize(Vector2D<int> size) => Resize(size);

    /// <summary>Sem DesignResolution: viewport de GL = janela inteira, ScreenSize/Camera usam o
    /// tamanho físico (comportamento de sempre). Com DesignResolution: calcula a maior área
    /// centralizada dentro da janela que preserva a proporção do design — o resto vira barra
    /// (pintada pelo ClearColor, já que glClear ignora o viewport) — e ScreenSize/Camera/toque
    /// passam a usar a resolução fixa, não o tamanho físico da janela.</summary>
    private void ApplyViewport(Vector2D<int> windowSize)
    {
        if (DesignResolution is not { } design)
        {
            ScreenSize = windowSize;
            Gl.Viewport(windowSize);
            Camera.SetViewport(windowSize.X, windowSize.Y);
            Input.ClearViewportMapping();
            return;
        }

        float windowAspect = windowSize.X / (float)Math.Max(1, windowSize.Y);
        float designAspect = design.X / (float)Math.Max(1, design.Y);

        int vw, vh;
        if (windowAspect > designAspect)
        {
            vh = windowSize.Y;
            vw = Math.Max(1, (int)MathF.Round(vh * designAspect));
        }
        else
        {
            vw = windowSize.X;
            vh = Math.Max(1, (int)MathF.Round(vw / designAspect));
        }

        int vx = (windowSize.X - vw) / 2;
        int vy = (windowSize.Y - vh) / 2; // topo-esquerda (convenção de tela)

        // GL.Viewport usa origem embaixo-esquerda — inverte o Y antes de mandar pra GPU.
        Gl.Viewport(new Vector2D<int>(vx, windowSize.Y - vy - vh), new Vector2D<int>(vw, vh));

        ScreenSize = design;
        Camera.SetViewport(design.X, design.Y);
        Input.SetViewportMapping(vx, vy, vw, vh, design.X, design.Y);
    }

    // Recursos GL precisam ser liberados com o contexto ainda vivo, por isso no Closing
    // da view e não em Dispose (que roda depois de View.Run retornar).
    private void HandleClosing() => Shutdown();

    /// <summary>Chamado uma vez com o contexto gráfico pronto. Crie entidades e carregue assets aqui.</summary>
    protected abstract void OnLoad();

    /// <summary>Chamado a cada frame antes dos behaviors do mundo.</summary>
    protected virtual void OnUpdate(float deltaTime)
    {
    }

    /// <summary>Chamado a cada frame com o SpriteBatch já aberto, após os sprites do mundo.</summary>
    protected virtual void OnRender(float deltaTime)
    {
    }

    /// <summary>Chamado a cada frame no passe de UI (coordenadas de tela). HUD e diálogos aqui.</summary>
    protected virtual void OnRenderUI(float deltaTime)
    {
    }

    /// <summary>Chamado ao fechar, antes da engine liberar os recursos gráficos.</summary>
    /// <summary>
    /// Carrega um arquivo de banco, se existir. Banco é opcional — jogo sem item nem tabela de
    /// spawn não precisa criar nada. Mas JSON quebrado é avisado: silêncio aqui viraria "por que
    /// meu item não faz nada?" sem nenhuma pista.
    /// </summary>
    /// <summary>
    /// Avisa sobre item cujo Type não está na lista ItemTypes cadastrada. Só avisa: a lista é
    /// opcional (sem ela, qualquer texto vale), e derrubar o boot por causa de uma categoria
    /// escrita torto seria pior que a categoria torta. O ponto é pegar "Consumível" vs
    /// "Consumivel" no console em vez de na loja que filtra errado.
    /// </summary>
    private void WarnAboutUnknownItemTypes()
    {
        if (Types.Get(Database.TypeDatabase.ItemTypes).Count == 0)
            return;

        foreach (var (id, item) in Items.Items)
        {
            if (!Types.Contains(Database.TypeDatabase.ItemTypes, item.Type))
            {
                Console.Error.WriteLine(
                    $"[Game] Item '{id}': tipo '{item.Type}' não está na lista ItemTypes do banco " +
                    $"de tipos. Erro de digitação, ou falta cadastrar a categoria.");
            }
        }
    }

    private void LoadDatabase(string path, string label, Action<string> load)
    {
        if (!Assets.Exists(path))
            return;

        try
        {
            load(Assets.LoadText(path));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Game] Banco de {label} '{path}' inválido: {ex.Message}");
        }
    }

    protected virtual void OnUnload()
    {
    }

    public virtual void Dispose() => GC.SuppressFinalize(this);
}
