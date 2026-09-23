using Aurora.Runtime;
using Aurora.Runtime.Graphics;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Bichinhos;

/// <summary>
/// Boot, relógio do bichinho, save e a troca entre telas (escolha, casa, brincadeira, batalha,
/// evolução).
///
/// <para>O jogo não usa cena nem tela de UI em JSON: é tudo desenhado em código pelas classes de
/// <c>Telas/</c>, porque quase tudo aqui é dinâmico (barras, bicho animado, golpes que mudam por
/// nível). A engine entra com janela, loop, input com toque, texturas, fonte e letterbox.</para>
///
/// <para>Divisão do projeto: <c>Game/</c> é a regra (necessidades, XP, batalha — testável sem
/// janela), <c>Render/</c> é o pincel, <c>Telas/</c> junta os dois, e os dados moram em
/// <c>Assets/database/especies.json</c>.</para>
/// </summary>
public sealed class BichinhosGame : Game
{
    public const int Largura = 720;
    public const int Altura = 1280;

    /// <summary>Segundos entre saves automáticos. O Android mata app em segundo plano sem aviso,
    /// então não dá pra contar com o OnUnload.</summary>
    private const float IntervaloSave = 10f;

    private Tela? _tela;
    private float _desdeSave;
    private DateTime _relogio = DateTime.UtcNow;

    public Tinta Tinta { get; private set; } = null!;
    public Toque Toque { get; private set; } = null!;
    public Progresso Progresso { get; private set; } = new();
    public Random Rng { get; } = new();

    /// <summary>Quanto o relógio do bicho anda por segundo real. 1 = tempo de verdade;
    /// <c>--rapido</c> põe 60 (um minuto por segundo) pra testar fome e cocô sem esperar.</summary>
    public float EscalaTempo { get; set; } = 1f;

    public Bicho? Bicho => Progresso.Bicho;

    // Ferramentas de teste (ver Program.cs). No Android nenhuma é ligada.
    private string? _pastaSave;
    private string? _demo;
    private string? _foto;
    private float _fotoT;

    private string PastaSave => _pastaSave ?? Save.SaveDirectory;

    public BichinhosGame()
    {
        GameName = "AuroraBichinhos";
        DesignResolution = new Vector2D<int>(Largura, Altura);
        ClearColor = Color.FromBytes(24, 18, 32);
    }

    public void LerArgumentos(string[] args)
    {
        ParseArgs(args);
        for (int i = 0; i < args.Length; i++)
        {
            string? valor = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--rapido": EscalaTempo = 60f; break;
                case "--save" when valor is not null: _pastaSave = valor; break;
                case "--demo" when valor is not null: _demo = valor; break;
                case "--foto" when valor is not null: _foto = valor; break;
            }
        }
    }

    protected override void OnLoad()
    {
        Catalogo.Atual = Catalogo.Carregar(Assets.LoadText(Catalogo.Caminho));
        Tinta = new Tinta(Assets);
        Toque = new Toque(Input);

        Progresso = Progresso.Carregar(PastaSave);

        if (_demo is not null)
        {
            AbrirDemo(_demo);
            return;
        }

        if (Bicho is { } bicho)
        {
            // O tempo que o jogo ficou fechado passa de uma vez, em passos de minuto.
            double horas = Progresso.HorasDesdeUltimaVez(DateTime.UtcNow);
            bicho.Avancar(horas, Rng);
            IrPara(new TelaCasa(this, horas));
        }
        else
        {
            IrPara(new TelaEscolha(this));
        }
    }

    public void IrPara(Tela tela)
    {
        _tela?.Sair();
        _tela = tela;
        tela.Entrar();
    }

    public void Salvar()
    {
        _desdeSave = 0f;
        try
        {
            Progresso.Salvar(PastaSave);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Bichinhos] Não deu pra salvar: {ex.Message}");
        }
    }

    public void NovoBicho(string especieId)
    {
        Progresso = new Progresso { Bicho = new Bicho { EspecieId = especieId }, Moedas = 20 };
        Salvar();
    }

    public void Recomecar()
    {
        Progresso = new Progresso();
        Salvar();
        IrPara(new TelaEscolha(this));
    }

    protected override void OnUpdate(float deltaTime)
    {
        Toque.NovoFrame();

        // Relógio de parede, não o deltaTime: no Android o app fica pausado em segundo plano e
        // volta com um frame de delta limitado a 50 ms — as horas no bolso sumiriam. A diferença
        // real desde o frame anterior pega esse tempo (com o mesmo teto do jogo fechado).
        var agora = DateTime.UtcNow;
        double horas = Math.Clamp((agora - _relogio).TotalHours, 0.0, Progresso.MaxHorasFora);
        _relogio = agora;
        if (Bicho is { } bicho)
            bicho.Avancar(horas * EscalaTempo, Rng);

        _tela?.Atualizar(deltaTime);

        _desdeSave += deltaTime;
        if (Bicho is not null && _desdeSave >= IntervaloSave)
            Salvar();
    }

    protected override void OnRenderUI(float deltaTime)
    {
        Tinta.Lote = SpriteBatch;
        _tela?.Desenhar(deltaTime);

        if (_foto is not null)
        {
            _fotoT += deltaTime;
            if (_fotoT >= 1.5f)
            {
                TirarFoto(_foto);
                _foto = null;
                Exit();
            }
        }
    }

    /// <summary>Monta um bicho de exemplo e abre direto numa tela — pra testar e tirar foto.</summary>
    private void AbrirDemo(string tela)
    {
        string especie = tela.Contains(':') ? tela[(tela.IndexOf(':') + 1)..] : "brasinha";
        tela = tela.Split(':')[0];

        var bicho = new Bicho { EspecieId = especie, Nivel = 8, Estagio = 1, Xp = 40, Fome = 62, Alegria = 85, Energia = 70, Higiene = 45, Cocos = 2 };
        Progresso = new Progresso { Bicho = bicho, Moedas = 37 };

        switch (tela)
        {
            case "escolha":
                Progresso = new Progresso();
                IrPara(new TelaEscolha(this));
                break;
            case "batalha":
                var inimigo = Lutador.Selvagem(Catalogo.Atual.Especie("gotinha"), 9);
                IrPara(new TelaBatalha(this, new Batalha(Lutador.De(bicho), inimigo, Dificuldade.Normal, Rng)));
                break;
            case "brincar":
                IrPara(new TelaBrincar(this));
                break;
            case "evolucao":
                bicho.Nivel = 14;
                IrPara(new TelaEvolucao(this));
                break;
            case "noite":
                bicho.Dormindo = true;
                IrPara(new TelaCasa(this, 0));
                break;
            case "comer" or "lutar" or "ficha":
                var casa = new TelaCasa(this, 0);
                IrPara(casa);
                casa.AbrirMenu(tela);
                break;
            default:
                IrPara(new TelaCasa(this, 0));
                break;
        }
    }

    private void TirarFoto(string caminho)
    {
        // O lote ainda está aberto: fecha pra tudo chegar na GPU, lê e reabre (a engine fecha
        // de novo logo depois do OnRenderUI).
        SpriteBatch.End();
        var tamanho = View.FramebufferSize;
        var pixels = new byte[tamanho.X * tamanho.Y * 4];
        Gl.ReadPixels(0, 0, (uint)tamanho.X, (uint)tamanho.Y, PixelFormat.Rgba, PixelType.UnsignedByte, pixels.AsSpan());
        PngWriter.Write(caminho, tamanho.X, tamanho.Y, pixels, flipVertically: true);
        SpriteBatch.Begin(GetScreenProjection());
    }

    protected override void OnUnload()
    {
        if (Bicho is not null)
            Salvar();
    }
}

/// <summary>Uma tela do jogo: atualiza com o toque e desenha tudo no passe de UI.</summary>
public abstract class Tela
{
    protected Tela(BichinhosGame jogo) => Jogo = jogo;

    protected BichinhosGame Jogo { get; }
    protected Tinta Tinta => Jogo.Tinta;
    protected Toque Toque => Jogo.Toque;

    public virtual void Entrar() { }
    public virtual void Sair() { }
    public abstract void Atualizar(float dt);
    public abstract void Desenhar(float dt);

    protected void Fundo(string nome)
        => Tinta.Sprite(Tinta.Textura($"sprites/fundos/{nome}.png"), System.Numerics.Vector2.Zero,
            new System.Numerics.Vector2(BichinhosGame.Largura, BichinhosGame.Altura), System.Numerics.Vector2.Zero, Color.White);
}
