using Aurora.Runtime;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.UI;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace Survivors;

/// <summary>
/// O jogo: boot, telas e o fluxo entre menu, loja, partida, level up, pausa e derrota.
///
/// <para>Por que o fluxo mora AQUI e não num Behavior: level up e pausa congelam o mundo
/// (<c>World.Paused</c>), e mundo congelado não roda Behavior nenhum — um script na cena não
/// conseguiria nem ler o botão que descongela. O <c>OnUpdate</c> do Game continua rodando
/// sempre, então ele é o lugar certo pra tudo que precisa acontecer com o jogo parado.</para>
///
/// <para>Divisão de trabalho do projeto: aqui é o fluxo de telas; a regra de cada sistema está em
/// Game/ (partida, melhorias, loja); o que acontece dentro da arena está em Scripts/ (jogador,
/// arma, inimigo, spawner, coletável).</para>
/// </summary>
public sealed class SurvivorsGame : Game
{
    /// <summary>Telas de UI carregadas no boot. O id de cada uma é o nome do arquivo sem
    /// extensão — é por ele que <c>UI.Show/Hide/Find</c> acham a tela.</summary>
    private static readonly string[] Telas = ["MainMenu", "Loja", "Hud", "LevelUp", "GameOver", "Pausa"];

    private const string CenaMenu = "scenes/menu.json";
    private const string CenaArena = "scenes/arena.json";

    private enum Estado { Menu, Loja, Jogando, SubindoNivel, Pausa, Morto }

    private readonly RunManager _run = new();
    private Font _font = null!;
    private Estado _estado = Estado.Menu;
    private string _mensagemLoja = "";

    public SurvivorsGame()
    {
        GameName = "AuroraSurvivors";
        DesignResolution = new Vector2D<int>(1280, 720);
        ClearColor = Color.FromBytes(12, 12, 20);
    }

    protected override void OnLoad()
    {
        _font = Assets.LoadFont("fonts/DejaVuSans.ttf", 22f);

        foreach (string tela in Telas)
            UI.Load($"scenes/{tela}.json", Assets);

        // Progresso permanente (moedas + níveis da loja) vive no save do slot 0. O save guarda
        // também a cena de quando foi gravado, e é por isso que o boot termina sempre chamando
        // IrParaMenu(): sem isso, quem fechou o jogo na tela de derrota voltaria direto pra arena.
        if (Save.HasSave(0))
            Save.Load(0);

        // --scene do editor: dar Play com a arena aberta cai direto numa partida, sem passar
        // pelo menu. Qualquer outra cena (ou nenhuma) abre o menu normal.
        if (BootScene is { } boot && boot.Replace('\\', '/').EndsWith("arena.json", StringComparison.OrdinalIgnoreCase))
        {
            IniciarPartida();
            return;
        }

        IrParaMenu();
    }

    protected override void OnUpdate(float deltaTime)
    {
        switch (_estado)
        {
            case Estado.Menu: AtualizarMenu(); break;
            case Estado.Loja: AtualizarLoja(); break;
            case Estado.Jogando: AtualizarPartida(deltaTime); break;
            case Estado.SubindoNivel: AtualizarEscolha(); break;
            case Estado.Pausa: AtualizarPausa(); break;
            case Estado.Morto: AtualizarMorte(); break;
        }
    }

    protected override void OnRenderUI(float deltaTime)
    {
        // ScreenSize (e não View.FramebufferSize): é o tamanho que UI.Update usa no hit-test dos
        // botões. Desenhar num tamanho e testar clique noutro põe o botão num lugar e a área
        // clicável em outro.
        UI.Draw(SpriteBatch, _font, State, Inventory, Quests, ScreenSize.X, ScreenSize.Y);
        Dialogue.Draw(SpriteBatch, _font, ScreenSize.X, ScreenSize.Y);
    }

    // ---------------------------------------------------------------- menu e loja

    private void AtualizarMenu()
    {
        if (Clicou("MainMenu", "BtnJogar"))
            IniciarPartida();
        else if (Clicou("MainMenu", "BtnLoja"))
            AbrirLoja();
        else if (Clicou("MainMenu", "BtnSair"))
            Exit();
    }

    private void AbrirLoja()
    {
        _mensagemLoja = "";
        _estado = Estado.Loja;
        MostrarSomente("Loja");
    }

    private void AtualizarLoja()
    {
        for (int i = 0; i < MetaShop.Itens.Count; i++)
        {
            if (!Clicou("Loja", $"BtnComprar{i}"))
                continue;

            if (MetaShop.Comprar(State, Inventory, MetaShop.Itens[i], out _mensagemLoja))
                Save.Save(0);   // compra é progresso permanente: grava na hora
        }

        if (Clicou("Loja", "BtnVoltar"))
        {
            IrParaMenu();
            return;
        }

        AtualizarTextosLoja();
    }

    private void AtualizarTextosLoja()
    {
        for (int i = 0; i < MetaShop.Itens.Count; i++)
        {
            var item = MetaShop.Itens[i];
            int nivel = MetaShop.Nivel(State, item);

            Texto("Loja", $"Item{i}", $"{item.Nome}  [{nivel}/{item.MaxNivel}]  —  {item.Descricao}");

            if (UI.Find<UiButton>("Loja", $"BtnComprar{i}") is { } botao)
                botao.Text = MetaShop.NoMaximo(State, item) ? "Máximo" : $"Comprar ({MetaShop.Preco(State, item)})";
        }

        Texto("Loja", "Mensagem", _mensagemLoja);
    }

    // ---------------------------------------------------------------- partida

    private void IniciarPartida()
    {
        _run.Iniciar(State, Inventory);
        World.Paused = false;
        _estado = Estado.Jogando;
        MostrarSomente("Hud");
        LoadScene(CenaArena);
    }

    private void AtualizarPartida(float deltaTime)
    {
        if (!World.TryFind("Player", out var jogador))
            return;

        _run.Update(deltaTime, State);
        AtualizarHud();

        if (jogador.Get<Health>() is not { } vida || vida.IsDead)
        {
            Morrer();
            return;
        }

        if (Input.WasKeyPressed(Key.Escape))
        {
            Pausar();
            return;
        }

        if (_run.PodeSubirDeNivel(State))
            AbrirLevelUp();
    }

    private void AtualizarHud()
    {
        int tempo = (int)_run.Tempo;
        Texto("Hud", "TextoTempo", $"{tempo / 60:00}:{tempo % 60:00}");
        Texto("Hud", "TextoNivel", $"Nível {_run.Nivel}");
        Texto("Hud", "TextoVida", $"{(int)State.GetVariable("Vida")} / {(int)State.GetVariable("VidaMax")}");
    }

    // ---------------------------------------------------------------- level up

    private void AbrirLevelUp()
    {
        _run.AbrirNivel(State);

        // Tudo no teto: não há o que escolher, então o nível sobe em silêncio em vez de abrir uma
        // tela vazia que travaria a partida (nenhum botão pra fechar).
        if (_run.Opcoes.Count == 0)
            return;

        for (int i = 0; i < 3; i++)
        {
            bool existe = i < _run.Opcoes.Count;
            if (UI.Find<UiButton>("LevelUp", $"Opcao{i}") is { } botao)
                botao.Text = existe ? _run.Opcoes[i].Nome : "—";
            Texto("LevelUp", $"Descricao{i}", existe ? _run.Opcoes[i].Descricao : "");
        }

        World.Paused = true;
        _estado = Estado.SubindoNivel;
        MostrarSomente("Hud", "LevelUp");
    }

    private void AtualizarEscolha()
    {
        for (int i = 0; i < _run.Opcoes.Count; i++)
        {
            if (Clicou("LevelUp", $"Opcao{i}"))
            {
                Escolher(i);
                return;
            }
        }
    }

    private void Escolher(int indice)
    {
        if (World.TryFind("Player", out var jogador) && jogador.Get<PlayerStats>() is { } stats)
        {
            _run.Escolher(indice, stats);
            SincronizarVidaMaxima(jogador, stats);
        }

        // XP acumulado pode dar mais de um nível de uma vez (uma horda inteira morrendo junto):
        // encadeia a próxima escolha em vez de voltar pro jogo e reabrir no frame seguinte.
        if (_run.PodeSubirDeNivel(State))
        {
            AbrirLevelUp();
            if (_estado == Estado.SubindoNivel)
                return;
        }

        World.Paused = false;
        _estado = Estado.Jogando;
        MostrarSomente("Hud");
    }

    /// <summary>Vida máxima ganha num upgrade também cura o mesmo tanto — subir o teto sem dar a
    /// vida junto faria o prêmio parecer que não fez nada.</summary>
    private static void SincronizarVidaMaxima(Entity jogador, PlayerStats stats)
    {
        if (jogador.Get<Health>() is not { } vida)
            return;

        float ganho = stats.MaxHealth - vida.Max;
        vida.Max = stats.MaxHealth;
        if (ganho > 0f)
            vida.Current = MathF.Min(vida.Max, vida.Current + ganho);
    }

    // ---------------------------------------------------------------- pausa, derrota, menu

    private void Pausar()
    {
        World.Paused = true;
        _estado = Estado.Pausa;
        MostrarSomente("Hud", "Pausa");
    }

    private void AtualizarPausa()
    {
        if (Clicou("Pausa", "BtnContinuar") || Input.WasKeyPressed(Key.Escape))
        {
            World.Paused = false;
            _estado = Estado.Jogando;
            MostrarSomente("Hud");
        }
        else if (Clicou("Pausa", "BtnMenu"))
        {
            IrParaMenu();
        }
    }

    private void Morrer()
    {
        World.Paused = true;
        _estado = Estado.Morto;

        int tempo = (int)_run.Tempo;
        int ganhas = Math.Max(0, Inventory.GetCount(MetaShop.Moeda) - _run.MoedasNoInicio);
        Texto("GameOver", "Resumo",
            $"Sobreviveu {tempo / 60:00}:{tempo % 60:00}  ·  nível {_run.Nivel}  ·  " +
            $"{(int)State.GetVariable("Kills")} mortes  ·  +{ganhas} moedas");

        // Grava as moedas da partida na hora: fechar o jogo nesta tela não pode custar o prêmio.
        Save.Save(0);
        MostrarSomente("GameOver");
    }

    private void AtualizarMorte()
    {
        if (Clicou("GameOver", "BtnDeNovo"))
            IniciarPartida();
        else if (Clicou("GameOver", "BtnMenu"))
            IrParaMenu();
    }

    private void IrParaMenu()
    {
        World.Paused = false;
        _estado = Estado.Menu;
        _mensagemLoja = "";
        MostrarSomente("MainMenu");
        LoadScene(CenaMenu);
        Save.Save(0);
    }

    // ---------------------------------------------------------------- utilidades de UI

    /// <summary>Deixa visíveis só as telas listadas. Tela de UI não é descartada por
    /// <c>LoadScene</c> (ela vive fora do World), então esconder o que não é da vez é
    /// obrigatório — é a origem do clássico "o menu ficou grudado por cima do jogo".</summary>
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
