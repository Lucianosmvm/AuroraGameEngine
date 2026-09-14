using Aurora.Runtime;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.UI;
using BeastArena.Render;
using BeastArena.Sim;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace BeastArena;

/// <summary>
/// Liga as três camadas do protótipo:
///
/// <list type="bullet">
/// <item><c>Sim/</c> — a batalha como lógica pura (regras, IA). Não referencia a Aurora.</item>
/// <item><c>Render/</c> — desenha a batalha e lê o toque. Só lê a simulação e manda comandos.</item>
/// <item>Este arquivo — fluxo de telas e o relógio: acumula o tempo real e avança a simulação
/// em passos fixos.</item>
/// </list>
/// </summary>
public sealed class BeastArenaGame : Game
{
    private static readonly string[] Telas = ["MainMenu", "Fim"];

    /// <summary>Teto de passos por frame. Um engasgo de 1 s (janela arrastada, GC) não pode
    /// virar 30 passos de uma vez: o jogador veria a partida "pular".</summary>
    private const int MaximoDePassosPorFrame = 5;

    private enum Estado { Menu, Batalha, Fim }

    private readonly List<EventoDeBatalha> _eventos = [];
    private readonly Mao _mao = new();

    private Font _fonte = null!;
    private Font _fonteGrande = null!;
    private Formas _formas = null!;
    private Sprites _sprites = null!;
    private CatalogoCartas _catalogo = null!;
    private VisaoArena _visao = null!;

    private Estado _estado = Estado.Menu;
    private Batalha? _batalha;
    private IaOponente? _ia;
    private Dificuldade _dificuldade = Dificuldade.Normal;
    private float _acumulado;

    public BeastArenaGame()
    {
        GameName = "BeastArena";
        DesignResolution = new Vector2D<int>(720, 1280);
        ClearColor = Color.FromBytes(14, 18, 13);
    }

    protected override void OnLoad()
    {
        _fonte = Assets.LoadFont("fonts/DejaVuSans.ttf", 26f);
        _fonteGrande = Assets.LoadFont("fonts/DejaVuSans.ttf", 52f);
        _formas = new Formas(Gl);
        _sprites = new Sprites(Gl, Assets);

        _catalogo = CatalogoCartas.Carregar(Assets.LoadText(CatalogoCartas.Caminho));
        _visao = new VisaoArena(_catalogo, _sprites);

        foreach (string tela in Telas)
            UI.Load($"scenes/{tela}.json", Assets);

        // Play do editor com a arena aberta cai direto na batalha.
        if (BootScene is { } boot && boot.Replace('\\', '/').EndsWith("arena.json", StringComparison.OrdinalIgnoreCase))
        {
            IniciarBatalha(Dificuldade.Normal);
            return;
        }

        IrParaMenu();
    }

    protected override void OnUpdate(float deltaTime)
    {
        switch (_estado)
        {
            case Estado.Menu:
                if (Clicou("MainMenu", "BtnNormal"))
                    IniciarBatalha(Dificuldade.Normal);
                else if (Clicou("MainMenu", "BtnFacil"))
                    IniciarBatalha(Dificuldade.Facil);
                else if (Clicou("MainMenu", "BtnSair"))
                    Exit();
                break;

            case Estado.Batalha:
                AtualizarBatalha(deltaTime);
                break;

            case Estado.Fim:
                _visao.Atualizar(deltaTime);
                if (Clicou("Fim", "BtnDeNovo"))
                    IniciarBatalha(_dificuldade);
                else if (Clicou("Fim", "BtnMenu"))
                    IrParaMenu();
                break;
        }
    }

    // ---------------------------------------------------------------- fluxo

    private void IrParaMenu()
    {
        _estado = Estado.Menu;
        _batalha = null;
        _ia = null;
        MostrarSomente("MainMenu");
    }

    private void IniciarBatalha(Dificuldade dificuldade)
    {
        _dificuldade = dificuldade;

        // Semente nova por partida; guardada na batalha, reproduz a partida inteira junto com
        // a lista de comandos.
        int semente = Environment.TickCount;
        var baralho = _catalogo.Baralho();

        _batalha = new Batalha(baralho, baralho, semente);
        _ia = new IaOponente(_batalha, Equipe.Inimigo, dificuldade, semente ^ 0x5A5A);
        _acumulado = 0f;
        _eventos.Clear();
        _visao.Limpar();
        _mao.Reiniciar();

        _estado = Estado.Batalha;
        MostrarSomente();
    }

    private void AtualizarBatalha(float deltaTime)
    {
        if (_batalha is null || _ia is null)
            return;

        if (Input.WasKeyPressed(Key.Escape))
        {
            IrParaMenu();
            return;
        }

        _mao.Atualizar(deltaTime, Input, Camera, _batalha);

        _acumulado += deltaTime;
        int passos = 0;

        while (_acumulado >= Batalha.Passo && passos < MaximoDePassosPorFrame)
        {
            _ia.Passo();
            _batalha.Avancar();
            _acumulado -= Batalha.Passo;
            passos++;
        }

        if (passos == MaximoDePassosPorFrame)
            _acumulado = 0f;

        _batalha.ColherEventos(_eventos);
        _visao.Consumir(_eventos);
        _eventos.Clear();
        _visao.Atualizar(deltaTime);

        if (_batalha.Acabou)
            Terminar(_batalha);
    }

    private void Terminar(Batalha batalha)
    {
        _estado = Estado.Fim;

        var jogador = batalha.LadoDe(Equipe.Jogador);
        var inimigo = batalha.LadoDe(Equipe.Inimigo);

        Texto("Fim", "Resultado", batalha.Resultado switch
        {
            ResultadoDaBatalha.VitoriaDoJogador => "VITÓRIA!",
            ResultadoDaBatalha.VitoriaDoInimigo => "DERROTA",
            _ => "EMPATE",
        });

        Texto("Fim", "Resumo",
            $"Pontos  {(int)jogador.Pontos} x {(int)inimigo.Pontos}\n\n" +
            $"Santuários dominados: {jogador.Capturas}\n" +
            $"Suas evoluções: {jogador.Evolucoes}\n" +
            $"Evoluções da IA: {inimigo.Evolucoes}\n" +
            $"Mana de abates: {jogador.ManaDeAbates + jogador.ManaDeRecompensa:0}");

        MostrarSomente("Fim");
    }

    // ---------------------------------------------------------------- desenho

    protected override void OnRender(float deltaTime)
    {
        if (_batalha is null)
            return;

        var previa = _estado == Estado.Batalha ? _mao.Previa(Camera, _batalha) : null;
        float alfa = _estado == Estado.Batalha ? Math.Clamp(_acumulado / Batalha.Passo, 0f, 1f) : 1f;

        _visao.Desenhar(SpriteBatch, _fonte, _formas, _batalha, alfa, previa);
    }

    protected override void OnRenderUI(float deltaTime)
    {
        if (_batalha is not null)
            _mao.Desenhar(SpriteBatch, _fonte, _formas, _sprites, _batalha);

        if (_estado == Estado.Fim)
            SpriteBatch.DrawRect(System.Numerics.Vector2.Zero, new System.Numerics.Vector2(ScreenSize.X, ScreenSize.Y), Color.FromHex("#000000B0"));

        if (_estado == Estado.Menu)
            DesenharTitulo();

        UI.Draw(SpriteBatch, _fonte, State, Inventory, Quests, ScreenSize.X, ScreenSize.Y);
    }

    /// <summary>Título na fonte grande: a UI da engine desenha tudo com uma fonte só, e esticar a
    /// de 26 px até o tamanho de título borra.</summary>
    private void DesenharTitulo()
    {
        const string titulo = "BEAST ARENA";
        var medida = _fonteGrande.MeasureText(titulo, 1.2f);
        _fonteGrande.Draw(SpriteBatch, titulo, new System.Numerics.Vector2((ScreenSize.X - medida.X) / 2f, 250f),
            Color.FromHex("#3DD6B5FF"), 1.2f);
    }

    protected override void OnUnload() => _formas?.Dispose();

    // ---------------------------------------------------------------- utilidades de UI

    private void MostrarSomente(params string[] visiveis)
    {
        foreach (string tela in Telas)
        {
            if (visiveis.Contains(tela))
                UI.Show(tela);
            else
                UI.Hide(tela);
        }
    }

    private bool Clicou(string tela, string botao) => UI.Find<UiButton>(tela, botao) is { Clicked: true };

    private void Texto(string tela, string elemento, string valor)
    {
        if (UI.Find<UiText>(tela, elemento) is { } texto)
            texto.Text = valor;
    }
}
