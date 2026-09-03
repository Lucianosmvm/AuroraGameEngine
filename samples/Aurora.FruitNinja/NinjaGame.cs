using System.Numerics;
using Aurora.Runtime;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.UI;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace FruitNinja;

/// <summary>
/// O jogo: boot, telas e o vaivém entre menu, loja de lâminas, partida, pausa e fim.
///
/// <para>Por que o fluxo mora AQUI e não num Behavior: pausa e fim congelam o mundo
/// (<c>World.Paused</c>), e mundo congelado não roda Behavior nenhum — um script na cena não
/// conseguiria nem ler o botão que descongela. O <c>OnUpdate</c> do Game roda sempre.</para>
///
/// <para>Divisão do projeto: aqui é o fluxo de telas; a REGRA do jogo está em Game/ (catálogos,
/// curva de dificuldade, partida); o que acontece dentro da arena está em Scripts/ (lançador,
/// fruta, metade, lâmina, espirro); e os DADOS estão em Assets/database/*.json.</para>
/// </summary>
public sealed class NinjaGame : Game
{
    private static readonly string[] Telas = ["MainMenu", "Hud", "Pausa", "GameOver", "Laminas"];

    private const string CenaMenu = "scenes/menu.json";
    private const string CenaArena = "scenes/arena.json";

    private enum Estado { Menu, Jogando, Pausa, Fim, Laminas }

    private Font _font = null!;
    private Font _fontGrande = null!;
    private Estado _estado = Estado.Menu;
    private Partida? _partida;
    private string _mensagemLoja = "";

    private Texture2D? _marca;
    private Texture2D? _marcaVazia;

    public NinjaGame()
    {
        GameName = "AuroraNinja";
        DesignResolution = new Vector2D<int>(Arena.Largura, Arena.Altura);
        ClearColor = Color.FromBytes(8, 7, 12);
    }

    protected override void OnLoad()
    {
        // Duas fontes de verdade em vez de uma esticada: o aviso de combo é desenhado no
        // dobro do tamanho da HUD, e ampliar um atlas de 26 px até lá borra tudo.
        _font = Assets.LoadFont("fonts/DejaVuSans.ttf", 26f);
        _fontGrande = Assets.LoadFont("fonts/DejaVuSans.ttf", 52f);

        _marca = Assets.LoadTexture("sprites/marca.png");
        _marcaVazia = Assets.LoadTexture("sprites/marca_vazia.png");

        CatalogoFrutas.Atual = CatalogoFrutas.Carregar(Assets.LoadText(CatalogoFrutas.Caminho));
        CatalogoLaminas.Atual = CatalogoLaminas.Carregar(Assets.LoadText(CatalogoLaminas.Caminho));

        foreach (string tela in Telas)
            UI.Load($"scenes/{tela}.json", Assets);

        // Recorde, moedas e lâminas compradas vivem no save do slot 0.
        if (Save.HasSave(0))
            Save.Load(0);

        // --scene do editor: dar Play com a arena aberta cai direto numa partida. Qualquer
        // outra cena (ou nenhuma) abre o menu — inclusive depois de carregar o save, que
        // guarda a cena de quando foi gravado e senão devolveria o jogador direto pro fim.
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
            case Estado.Laminas: AtualizarLoja(); break;
            case Estado.Jogando: AtualizarPartida(deltaTime); break;
            case Estado.Pausa: AtualizarPausa(); break;
            case Estado.Fim: AtualizarFim(); break;
        }
    }

    // ---------------------------------------------------------------- menu

    private void AtualizarMenu()
    {
        Texto("MainMenu", "Equipada", $"Lâmina: {CatalogoLaminas.Atual.Equipada(State).Nome}");

        if (Clicou("MainMenu", "BtnJogar"))
            IniciarPartida();
        else if (Clicou("MainMenu", "BtnLaminas"))
            AbrirLoja();
        else if (Clicou("MainMenu", "BtnSair"))
            Exit();
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

    // ---------------------------------------------------------------- loja de lâminas

    private void AbrirLoja()
    {
        _mensagemLoja = "";
        _estado = Estado.Laminas;
        MostrarSomente("Laminas");
    }

    private void AtualizarLoja()
    {
        var catalogo = CatalogoLaminas.Atual;

        for (int i = 0; i < catalogo.Todas.Count && i < 4; i++)
        {
            if (!Clicou("Laminas", $"BtnLamina{i}"))
                continue;

            var lamina = catalogo.Todas[i];

            if (catalogo.Comprada(State, lamina))
            {
                catalogo.Equipar(State, lamina);
                _mensagemLoja = $"{lamina.Nome} equipada.";
            }
            else
            {
                catalogo.Comprar(State, lamina, out _mensagemLoja);
            }

            Save.Save(0);   // compra e escolha são progresso permanente: grava na hora
        }

        if (Clicou("Laminas", "BtnVoltar"))
        {
            IrParaMenu();
            return;
        }

        AtualizarTextosLoja();
    }

    private void AtualizarTextosLoja()
    {
        var catalogo = CatalogoLaminas.Atual;
        var equipada = catalogo.Equipada(State);

        for (int i = 0; i < 4; i++)
        {
            bool existe = i < catalogo.Todas.Count;
            var lamina = existe ? catalogo.Todas[i] : null;

            Texto("Laminas", $"Lamina{i}", lamina is null
                ? ""
                // Traço curto, não travessão: o atlas da Font cobre ASCII + Latin-1, e "—"
                // (U+2014) está fora dos dois — sairia um "?" na tela.
                : $"{lamina.Nome} · {lamina.Descricao}");

            if (UI.Find<UiButton>("Laminas", $"BtnLamina{i}") is not { } botao)
                continue;

            if (lamina is null)
            {
                botao.Text = "";
                continue;
            }

            botao.Text = !catalogo.Comprada(State, lamina) ? $"Comprar ({lamina.Preco})"
                : lamina.Id == equipada.Id ? "Equipada"
                : "Equipar";
        }

        Texto("Laminas", "Mensagem", _mensagemLoja);
    }

    // ---------------------------------------------------------------- partida

    private void IniciarPartida()
    {
        _partida = Partida.Iniciar(State, CatalogoLaminas.Atual.Equipada(State));
        World.Paused = false;
        _estado = Estado.Jogando;
        MostrarSomente("Hud");
        LoadScene(CenaArena);
    }

    private void AtualizarPartida(float deltaTime)
    {
        if (_partida is null)
            return;

        _partida.Update(deltaTime);

        if (_partida.Acabou)
        {
            Terminar();
            return;
        }

        if (Input.WasKeyPressed(Key.Escape) || Clicou("Hud", "BtnPausa"))
            Pausar();
    }

    // ---------------------------------------------------------------- pausa e fim

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

    private void Terminar()
    {
        World.Paused = true;
        _estado = Estado.Fim;

        var partida = _partida!;
        int ganhas = Math.Max(0, (int)State.GetVariable(Partida.VarMoedas) - partida.MoedasNoInicio);

        Texto("GameOver", "Motivo", partida.MotivoDoFim);
        Texto("GameOver", "Resumo",
            $"{partida.Pontos} pontos  ·  nível {partida.Nivel}\n" +
            $"{(int)State.GetVariable(Partida.VarFrutasCortadas)} frutas  ·  " +
            $"melhor combo x{(int)State.GetVariable(Partida.VarMelhorCombo)}\n" +
            $"+{ganhas} moedas");
        Texto("GameOver", "Recorde",
            partida.Pontos >= partida.Recorde ? "NOVO RECORDE!" : $"Recorde: {partida.Recorde}");

        // Grava aqui: fechar o jogo nesta tela não pode custar o recorde nem as moedas.
        Save.Save(0);
        MostrarSomente("GameOver");
    }

    private void AtualizarFim()
    {
        if (Clicou("GameOver", "BtnDeNovo"))
            IniciarPartida();
        else if (Clicou("GameOver", "BtnMenu"))
            IrParaMenu();
    }

    // ---------------------------------------------------------------- desenho

    protected override void OnRender(float deltaTime)
    {
        // Passe de MUNDO: rastro e avisos precisam da mesma projeção das frutas pra cair em
        // cima delas. O rastro vem antes dos avisos e depois das frutas (World.Render já
        // desenhou), que é a ordem do original: a lâmina passa por cima da fruta.
        if (World.TryFind("Lamina", out var entidade) && entidade.Get<Lamina>() is { } lamina)
            lamina.Desenhar(SpriteBatch);

        if (_partida is null)
            return;

        foreach (var aviso in _partida.Avisos)
        {
            float t = Math.Clamp(aviso.Idade / aviso.Duracao, 0f, 1f);
            var fonte = aviso.Escala >= 1.4f ? _fontGrande : _font;
            float escala = aviso.Escala >= 1.4f ? aviso.Escala * 0.5f : aviso.Escala;

            var tamanho = fonte.MeasureText(aviso.Texto, escala);
            var posicao = aviso.Posicao - tamanho / 2f - new Vector2(0f, t * 70f);
            var cor = Color.FromHex(aviso.Cor);

            fonte.Draw(SpriteBatch, aviso.Texto, posicao, cor.WithAlpha(cor.A * (1f - t * t)), escala);
        }
    }

    protected override void OnRenderUI(float deltaTime)
    {
        UI.Draw(SpriteBatch, _font, State, Inventory, Quests, ScreenSize.X, ScreenSize.Y);

        // Na tela de fim os X saem: ali quem conta a história é o resumo da partida.
        if (_estado is Estado.Jogando or Estado.Pausa)
        {
            DesenharVidas();
            DesenharPoderes();
        }

        Dialogue.Draw(SpriteBatch, _font, ScreenSize.X, ScreenSize.Y);
    }

    /// <summary>Os X do canto, como no original: um X acesso por fruta que escapou.</summary>
    private void DesenharVidas()
    {
        if (_marca is null || _marcaVazia is null)
            return;

        int perdidas = Partida.VidasIniciais - (int)State.GetVariable(Partida.VarVidas);
        const float lado = 46f;

        for (int i = 0; i < Partida.VidasIniciais; i++)
        {
            var textura = i < perdidas ? _marca : _marcaVazia;
            var posicao = new Vector2(ScreenSize.X - 28f - (i + 1) * (lado + 8f), 26f);
            SpriteBatch.Draw(textura, posicao, new Vector2(lado, lado), Vector2.Zero, 0f, Color.White);
        }
    }

    /// <summary>Faixa dos poderes ligados, com o tempo que resta. Sem isso o jogador sente a
    /// câmera lenta acabar sem entender por quê.</summary>
    private void DesenharPoderes()
    {
        if (_partida is null)
            return;

        float y = ScreenSize.Y - 96f;

        foreach (var (efeito, rotulo, cor) in new[]
        {
            (EfeitoDePoder.Congelar, "CONGELADO", "#4FC3F7FF"),
            (EfeitoDePoder.Frenesi, "FRENESI", "#EF5350FF"),
            (EfeitoDePoder.Dobro, "PONTOS X2", "#FFD54FFF"),
        })
        {
            if (!_partida.Ativo(efeito))
                continue;

            string texto = $"{rotulo}  {_partida.Restante(efeito):0.0}s";
            var tamanho = _font.MeasureText(texto, 0.8f);
            _font.Draw(SpriteBatch, texto,
                new Vector2((ScreenSize.X - tamanho.X) / 2f, y), Color.FromHex(cor), 0.8f);
            y -= 34f;
        }
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
