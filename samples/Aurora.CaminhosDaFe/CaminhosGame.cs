using System.Numerics;
using Aurora.Runtime;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.UI;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace CaminhosDaFe;

/// <summary>
/// Caminhos da Fé — protótipo 0.1 do Capítulo 1 (Davi).
///
/// <para>Aqui ficam o fluxo de telas (menu → campo → pausa → fim) e o que é desenho por cima do
/// mundo: a mira da funda, a dica "[E] Falar" e o balido das ovelhas. A regra do jogo está
/// espalhada onde ela acontece: roteiro em <see cref="Missao"/>, comportamento em Scripts/.</para>
///
/// <para>Diálogo congela o mundo (<c>World.Paused</c>): o lobo não morde ninguém enquanto o
/// Jessé fala. Por isso o avanço do diálogo pela tecla E mora aqui, no <c>OnUpdate</c>, que roda
/// mesmo com o mundo parado.</para>
/// </summary>
public sealed class CaminhosGame : Game
{
    private static readonly string[] Telas = ["MainMenu", "Hud", "Pausa", "Fim"];

    private const string CenaMenu = "scenes/menu.json";
    private const string CenaCampo = "scenes/campo.json";

    private static readonly Color CorDica = Color.FromBytes(255, 241, 204);
    private static readonly Color FundoDica = new(0.08f, 0.05f, 0.03f, 0.75f);

    private enum Estado { Menu, Jogando, Pausa, Fim }

    private Font _fonte = null!;
    private Estado _estado;

    public CaminhosGame()
    {
        GameName = "CaminhosDaFe";
        DesignResolution = new Vector2D<int>(1280, 720);
        ClearColor = Color.FromBytes(20, 14, 10);
    }

    protected override void OnLoad()
    {
        _fonte = Assets.LoadFont("fonts/DejaVuSans.ttf", 20f);

        foreach (string tela in Telas)
            UI.Load($"scenes/{tela}.json", Assets);

        // Play do editor com o campo aberto cai direto no jogo.
        if (BootScene is { } boot && boot.Replace('\\', '/').EndsWith("campo.json", StringComparison.OrdinalIgnoreCase))
            NovoJogo();
        else
            IrParaMenu();
    }

    protected override void OnUpdate(float deltaTime)
    {
        switch (_estado)
        {
            case Estado.Menu:
                if (Clicou("MainMenu", "BtnNovo")) NovoJogo();
                else if (Clicou("MainMenu", "BtnSair")) Exit();
                break;
            case Estado.Jogando:
                AtualizarJogo();
                break;
            case Estado.Pausa:
                if (Clicou("Pausa", "BtnContinuar") || Input.WasKeyPressed(Key.Escape)) Continuar();
                else if (Clicou("Pausa", "BtnMenu")) IrParaMenu();
                break;
            case Estado.Fim:
                if (Clicou("Fim", "BtnDeNovo")) NovoJogo();
                else if (Clicou("Fim", "BtnMenu")) IrParaMenu();
                break;
        }
    }

    // ---------------------------------------------------------------- jogo

    private void NovoJogo()
    {
        State.Clear();
        Inventory.Clear();
        Quests.Clear();
        SceneState.Clear();
        Dialogue.Clear();

        World.Paused = false;
        _estado = Estado.Jogando;
        MostrarSomente("Hud");
        LoadScene(CenaCampo);
    }

    private void AtualizarJogo()
    {
        // Espaço/Enter a engine já trata; E e o botão A do controle também passam a fala, pra
        // quem abriu a conversa com E não precisar trocar de tecla.
        bool dialogoNoInicio = Dialogue.IsActive;
        if (dialogoNoInicio && (Input.WasKeyPressed(Key.E) || Input.WasGamepadButtonPressed(ButtonName.A)))
            Dialogue.Advance();

        // "No início" também conta: o E que fechou a última fala não pode, no mesmo frame, chegar
        // ao Davi e abrir a conversa de novo.
        World.Paused = dialogoNoInicio || Dialogue.IsActive;

        Texto("Hud", "Objetivo", Missao.Objetivo(Quests, State));

        if (World.Paused)
            return;

        if (World.TryFind("Player", out var davi) && davi.Get<Health>() is { IsDead: true })
        {
            Terminar("Davi caiu...",
                "O lobo foi mais rápido desta vez. Lembre: quando ele se encolher, vai saltar: " +
                "saia da frente ou acerte uma pedra antes.",
                "Tentar de novo");
            return;
        }

        if (Quests.GetStage(Missao.Id) >= Missao.Concluida)
        {
            Terminar("Capítulo concluído",
                $"As três ovelhas voltaram ao curral e o lobo foi afastado.\n" +
                $"Fé {(int)State.GetVariable(Missao.VarFe)}  ·  Coragem {(int)State.GetVariable(Missao.VarCoragem)}\n\n" +
                "Fim do protótipo 0.1. Próximo passo: a Missão 03, \"A Mensagem\".",
                "Jogar de novo");
            return;
        }

        if (Input.WasKeyPressed(Key.Escape))
        {
            World.Paused = true;
            _estado = Estado.Pausa;
            MostrarSomente("Hud", "Pausa");
        }
    }

    private void Continuar()
    {
        World.Paused = false;
        _estado = Estado.Jogando;
        MostrarSomente("Hud");
    }

    private void Terminar(string titulo, string resumo, string botao)
    {
        World.Paused = true;
        _estado = Estado.Fim;
        Texto("Fim", "Titulo", titulo);
        Texto("Fim", "Resumo", resumo);
        if (UI.Find<UiButton>("Fim", "BtnDeNovo") is { } b)
            b.Text = botao;
        MostrarSomente("Hud", "Fim");
    }

    private void IrParaMenu()
    {
        Dialogue.Clear();
        World.Paused = false;
        _estado = Estado.Menu;
        MostrarSomente("MainMenu");
        LoadScene(CenaMenu);
    }

    // ---------------------------------------------------------------- desenho por cima do mundo

    /// <summary>Mira da funda, em coordenadas de mundo: pontilhado até onde a pedra chegaria e a
    /// barrinha de carga embaixo do Davi.</summary>
    protected override void OnRender(float deltaTime)
    {
        if (_estado != Estado.Jogando || !World.TryFind("Player", out var davi)
            || davi.Get<Funda>() is not { Mirando: true } funda || davi.Get<Transform>() is not { } t)
            return;

        var cheia = funda.Carga >= 1f;
        var cor = cheia ? new Color(1f, 0.85f, 0.35f, 0.9f) : new Color(1f, 1f, 1f, 0.7f);
        for (float d = 10f; d <= funda.Alcance; d += 9f)
        {
            var ponto = funda.Origem + funda.Direcao * d;
            float alfa = 1f - d / (funda.Alcance + 20f);
            SpriteBatch.DrawRect(ponto - Vector2.One, new Vector2(2f, 2f), cor.WithAlpha(cor.A * alfa));
        }

        var barra = t.Position + new Vector2(-7f, 11f);
        SpriteBatch.DrawRect(barra, new Vector2(14f, 2f), new Color(0f, 0f, 0f, 0.6f));
        SpriteBatch.DrawRect(barra, new Vector2(14f * funda.Carga, 2f), cor);
    }

    protected override void OnRenderUI(float deltaTime)
    {
        if (_estado is Estado.Jogando && !Dialogue.IsActive)
            DesenharDicasDoMundo();

        UI.Draw(SpriteBatch, _fonte, State, Inventory, Quests, ScreenSize.X, ScreenSize.Y);
        Dialogue.Draw(SpriteBatch, _fonte, ScreenSize.X, ScreenSize.Y);
    }

    private void DesenharDicasDoMundo()
    {
        foreach (var (entidade, ovelha) in World.Query<Ovelha>())
        {
            if (ovelha.Balindo > 0f && entidade.Get<Transform>() is { } t)
                Balao(t.Position + new Vector2(0f, -8f), "Bééé!", 0.8f);
        }

        if (World.TryFind("Player", out var davi) && davi.Get<Davi>() is { AlvoProximo: { } alvo }
            && alvo.Get<Transform>() is { } ta && alvo.Get<Interagivel>() is { } interagivel)
        {
            Balao(ta.Position + new Vector2(0f, -12f), $"[E] {interagivel.Verbo}", 0.9f);
        }
    }

    /// <summary>Texto com fundo, centrado acima de um ponto do mundo.</summary>
    private void Balao(Vector2 mundo, string texto, float escala)
    {
        var tela = (mundo - Camera.Position) * Camera.Zoom
            + new Vector2(Camera.ViewportWidth, Camera.ViewportHeight) / 2f;
        var tamanho = _fonte.MeasureText(texto, escala);
        var canto = tela - new Vector2(tamanho.X / 2f, tamanho.Y);

        SpriteBatch.DrawRect(canto - new Vector2(6f, 3f), tamanho + new Vector2(12f, 6f), FundoDica);
        _fonte.Draw(SpriteBatch, texto, canto, CorDica, escala);
    }

    // ---------------------------------------------------------------- utilidades de UI

    /// <summary>Tela de UI vive fora do World: LoadScene não esconde nada sozinho.</summary>
    private void MostrarSomente(params string[] visiveis)
    {
        foreach (string tela in Telas)
        {
            if (visiveis.Contains(tela)) UI.Show(tela);
            else UI.Hide(tela);
        }
    }

    private bool Clicou(string tela, string botao) => UI.Find<UiButton>(tela, botao) is { Clicked: true };

    private void Texto(string tela, string elemento, string valor)
    {
        if (UI.Find<UiText>(tela, elemento) is { } texto)
            texto.Text = valor;
    }
}
