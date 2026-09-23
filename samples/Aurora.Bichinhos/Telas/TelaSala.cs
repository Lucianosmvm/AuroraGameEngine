using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Net;

namespace Bichinhos;

/// <summary>
/// Antessala do duelo: criar sala, procurar a sala do amigo na rede local ou digitar o IP dele.
/// Quando os dois bichos se apresentam (<see cref="Duelo.Pronto"/>), vai pra batalha.
/// </summary>
public sealed class TelaSala : Tela
{
    private enum Modo { Menu, Hospedando, Procurando, Digitando, Conectando, Erro }

    private const int MaxSalasNaLista = 4;

    private readonly BichoVisual _visual = new();
    private Modo _modo = Modo.Menu;
    private Duelo? _duelo;
    private bool _entregue;       // o duelo passou pra tela de batalha: não fechar a conexão ao sair
    private string _ip;
    private string _erro = "";
    private string _conectandoEm = "";
    private float _t;
    private List<string> _meusIps = [];

    private static readonly string[] Teclas = ["1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "<"];

    public TelaSala(BichinhosGame jogo) : base(jogo) => _ip = jogo.Progresso.UltimoIp;

    private Bicho B => Jogo.Bicho!;
    private NetSession Net => Jogo.Net;

    // ================================================================= layout

    private static Caixa BotaoGrande(int i) => new(110f, 470f + i * 130f, 500f, 104f);
    private static readonly Caixa BotaoVoltar = new(160f, 1110f, 400f, 96f);
    private static Caixa BotaoSala(int i) => new(60f, 400f + i * 120f, 600f, 104f);
    private static readonly Caixa BotaoDigitarIp = new(160f, 900f, 400f, 90f);
    private static readonly Caixa CampoIp = new(110f, 330f, 500f, 90f);
    private static readonly Caixa BotaoEntrar = new(380f, 1110f, 300f, 96f);
    private static readonly Caixa BotaoVoltarIp = new(40f, 1110f, 300f, 96f);

    private static Caixa Tecla(int i) => new(160f + (i % 3) * 140f, 450f + (i / 3) * 140f, 120f, 120f);

    // ================================================================= atualizar

    public override void Atualizar(float dt)
    {
        _t += dt;
        _visual.Atualizar(dt);

        if (_duelo?.Erro is { } erro && _modo != Modo.Erro)
            Falhar(erro);

        if (_duelo is { Pronto: true })
        {
            _entregue = true;
            Jogo.IrPara(new TelaBatalha(Jogo, _duelo));
            return;
        }

        switch (_modo)
        {
            case Modo.Menu:
                if (Toque.Tocou(BotaoGrande(0))) Hospedar();
                else if (Toque.Tocou(BotaoGrande(1))) Procurar();
                else if (Toque.Tocou(BotaoGrande(2))) _modo = Modo.Digitando;
                else if (Toque.Tocou(BotaoVoltar)) Jogo.IrPara(new TelaCasa(Jogo, 0));
                break;

            case Modo.Hospedando:
            case Modo.Conectando:
                if (Toque.Tocou(BotaoVoltar)) VoltarAoMenu();
                break;

            case Modo.Procurando:
                var salas = Net.Rooms;
                for (int i = 0; i < Math.Min(salas.Count, MaxSalasNaLista); i++)
                {
                    if (Toque.Tocou(BotaoSala(i)) && !salas[i].IsFull)
                    {
                        _conectandoEm = salas[i].RoomName;
                        _duelo = Duelo.Entrar(Net, B, salas[i]);
                        _modo = Modo.Conectando;
                        return;
                    }
                }
                if (Toque.Tocou(BotaoDigitarIp)) { Net.StopBrowsing(); _modo = Modo.Digitando; }
                else if (Toque.Tocou(BotaoVoltar)) VoltarAoMenu();
                break;

            case Modo.Digitando:
                AtualizarTeclado();
                break;

            case Modo.Erro:
                if (Toque.Tocou(BotaoVoltar)) VoltarAoMenu();
                break;
        }
    }

    /// <summary>Atalho do <c>--duelo</c>: "host" cria a sala, qualquer outra coisa é o IP.</summary>
    internal void Comecar(string alvo)
    {
        if (alvo == "host")
        {
            Hospedar();
            return;
        }

        _conectandoEm = alvo;
        _duelo = Duelo.Entrar(Net, B, alvo);
        _modo = Modo.Conectando;
    }

    private void Hospedar()
    {
        _duelo = Duelo.Hospedar(Net, B);
        _meusIps = EnderecosLocais();
        _modo = Modo.Hospedando;
    }

    private void Procurar()
    {
        Net.GameId = Duelo.IdDoJogo;
        try
        {
            Net.StartBrowsing();
            _modo = Modo.Procurando;
        }
        catch (Exception ex)
        {
            Falhar($"Não deu pra procurar salas: {ex.Message}");
        }
    }

    private void AtualizarTeclado()
    {
        for (int i = 0; i < Teclas.Length; i++)
        {
            if (!Toque.Tocou(Tecla(i)))
                continue;

            if (Teclas[i] == "<")
                _ip = _ip.Length > 0 ? _ip[..^1] : "";
            else if (_ip.Length < 15)
                _ip += Teclas[i];
        }

        if (Toque.Tocou(BotaoEntrar) && IpValido(_ip))
        {
            Jogo.Progresso.UltimoIp = _ip;
            Jogo.Salvar();
            _conectandoEm = _ip;
            _duelo = Duelo.Entrar(Net, B, _ip);
            _modo = Modo.Conectando;
        }
        else if (Toque.Tocou(BotaoVoltarIp))
        {
            _modo = Modo.Menu;
        }
    }

    private static bool IpValido(string ip)
        => System.Net.IPAddress.TryParse(ip, out var endereco) && endereco.AddressFamily == AddressFamily.InterNetwork && ip.Count(c => c == '.') == 3;

    private void Falhar(string erro)
    {
        _erro = erro;
        _modo = Modo.Erro;
        FecharDuelo();
    }

    private void VoltarAoMenu()
    {
        FecharDuelo();
        Net.StopBrowsing();
        _modo = Modo.Menu;
    }

    private void FecharDuelo()
    {
        _duelo?.Dispose();
        _duelo = null;
    }

    public override void Sair()
    {
        Net.StopBrowsing();
        if (!_entregue)
            FecharDuelo();
    }

    /// <summary>
    /// Os IPs deste aparelho na rede local, pra mostrar a quem vai digitar. Varre as placas em vez
    /// de perguntar "qual placa sai pra internet": no celular que está servindo de roteador
    /// (hotspot), a saída pra internet é o 4G, e esse IP não serve pro amigo conectado nele.
    /// </summary>
    private static List<string> EnderecosLocais()
    {
        var ips = new List<string>();
        try
        {
            foreach (var placa in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (placa.OperationalStatus != OperationalStatus.Up || placa.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var e in placa.GetIPProperties().UnicastAddresses)
                {
                    var bytes = e.Address.GetAddressBytes();
                    bool privado = e.Address.AddressFamily == AddressFamily.InterNetwork
                        && (bytes[0] == 10 || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31));
                    if (privado)
                        ips.Add(e.Address.ToString());
                }
            }
        }
        catch (Exception)
        {
            // Alguns Androids negam a lista de placas; o palpite da engine ainda serve.
        }

        if (ips.Count == 0)
            ips.Add(UdpNetTransport.GetLocalAddress());
        return ips.Distinct().Take(2).ToList();
    }

    // ================================================================= desenho

    public override void Desenhar(float dt)
    {
        Fundo("escolha");
        Tinta.TextoContornado("Duelo com amigo", new Vector2(360f, 70f), Color.FromHex("#FFE27AFF"), Tinta.FonteGrande, 0.85f);

        switch (_modo)
        {
            case Modo.Menu: DesenharMenu(); break;
            case Modo.Hospedando: DesenharHospedando(); break;
            case Modo.Procurando: DesenharProcurando(); break;
            case Modo.Digitando: DesenharTeclado(); break;
            case Modo.Conectando: DesenharEspera($"Conectando em {_conectandoEm}", "Se demorar, confira se os dois estão no mesmo Wi-Fi."); break;
            case Modo.Erro: DesenharErro(); break;
        }
    }

    private void Ajuda(string texto, float y)
        => Tinta.Paragrafo(texto, new Vector2(360f, y), 620f, Color.FromHex("#E8DDFBFF"), escala: 0.9f, alinhar: Tinta.Alinhar.Centro);

    private void DesenharMenu()
    {
        _visual.Desenhar(Tinta, B.Especie, B.Estagio, new Vector2(360f, 420f), 230f);

        string[] rotulos = ["Criar sala", "Procurar sala", "Digitar IP"];
        string[] icones = ["estrela", "espadas", "nota"];
        string[] cores = ["#E0A030FF", "#3F95E0FF", "#7B61C9FF"];
        for (int i = 0; i < 3; i++)
            Tinta.Botao(BotaoGrande(i), rotulos[i], Color.FromHex(cores[i]), Toque.SegurandoEm(BotaoGrande(i)), icones[i], fonte: Tinta.FonteMedia);

        Ajuda("Os dois celulares no mesmo Wi-Fi: um cria a sala e o outro procura.\n" +
              "Sem Wi-Fi? Um liga o roteador do celular (hotspot) e o outro conecta nele.", 860f);

        Tinta.Botao(BotaoVoltar, "Voltar", Color.FromHex("#8C8098FF"), Toque.SegurandoEm(BotaoVoltar));
    }

    private void DesenharHospedando()
    {
        _visual.Desenhar(Tinta, B.Especie, B.Estagio, new Vector2(360f, 560f), 300f);
        DesenharPontinhos("Esperando seu amigo", 620f);

        var painel = new Caixa(60f, 700f, 600f, 110f + _meusIps.Count * 60f);
        Tinta.Cartao(painel, Tinta.Papel, Tinta.Tinteiro, 4f, 26f);
        Tinta.Texto("No outro celular: Procurar sala.", new Vector2(360f, painel.Y + 22f), Tinta.Tinteiro, alinhar: Tinta.Alinhar.Centro);
        Tinta.Texto("Se não aparecer, digite este IP:", new Vector2(360f, painel.Y + 60f), Color.FromHex("#6B5A6EFF"), escala: 0.85f, alinhar: Tinta.Alinhar.Centro);
        for (int i = 0; i < _meusIps.Count; i++)
            Tinta.Texto(_meusIps[i], new Vector2(360f, painel.Y + 100f + i * 60f), Color.FromHex("#7B61C9FF"), Tinta.FonteMedia, alinhar: Tinta.Alinhar.Centro);

        Tinta.Botao(BotaoVoltar, "Cancelar", Color.FromHex("#8C8098FF"), Toque.SegurandoEm(BotaoVoltar));
    }

    private void DesenharProcurando()
    {
        var salas = Net.Rooms;
        if (salas.Count == 0)
        {
            DesenharPontinhos("Procurando salas no Wi-Fi", 300f);
            Ajuda("Peça pro seu amigo tocar em \"Criar sala\". Os dois precisam estar na mesma rede.", 520f);
        }
        else
        {
            Tinta.Texto("Toque na sala do seu amigo:", new Vector2(360f, 320f), Color.White, Tinta.FonteMedia, 0.8f, Tinta.Alinhar.Centro);
            for (int i = 0; i < Math.Min(salas.Count, MaxSalasNaLista); i++)
            {
                var sala = salas[i];
                var c = BotaoSala(i);
                Tinta.Botao(c, "", Color.FromHex(sala.IsFull ? "#8C8098FF" : "#3F95E0FF"), Toque.SegurandoEm(c), null, !sala.IsFull);
                Tinta.TextoContornado(sala.RoomName, new Vector2(c.Centro.X, c.Y + 16f), Color.White, Tinta.FonteMedia, 0.8f);
                Tinta.Texto(sala.IsFull ? "cheia" : sala.Address.Address.ToString(), new Vector2(c.Centro.X, c.Y + 62f), Color.White.WithAlpha(0.85f), escala: 0.8f, alinhar: Tinta.Alinhar.Centro);
            }
        }

        Tinta.Botao(BotaoDigitarIp, "Digitar IP", Color.FromHex("#7B61C9FF"), Toque.SegurandoEm(BotaoDigitarIp));
        Tinta.Botao(BotaoVoltar, "Voltar", Color.FromHex("#8C8098FF"), Toque.SegurandoEm(BotaoVoltar));
    }

    private void DesenharTeclado()
    {
        Tinta.Texto("IP do celular do amigo", new Vector2(360f, 250f), Color.White, Tinta.FonteMedia, 0.8f, Tinta.Alinhar.Centro);
        Tinta.Cartao(CampoIp, Tinta.Papel, Tinta.Tinteiro, 4f, 22f);
        bool cursor = (int)(_t * 2f) % 2 == 0;
        Tinta.Texto(_ip + (cursor ? "|" : " "), new Vector2(360f, CampoIp.Y + 20f), Tinta.Tinteiro, Tinta.FonteMedia, alinhar: Tinta.Alinhar.Centro);

        for (int i = 0; i < Teclas.Length; i++)
        {
            var c = Tecla(i);
            string rotulo = Teclas[i] == "<" ? "Apagar" : Teclas[i];
            var cor = Teclas[i] == "<" ? "#B0707EFF" : "#5A4A8AFF";
            Tinta.Botao(c, rotulo, Color.FromHex(cor), Toque.SegurandoEm(c), fonte: Teclas[i] == "<" ? Tinta.Fonte : Tinta.FonteMedia);
        }

        Tinta.Botao(BotaoVoltarIp, "Voltar", Color.FromHex("#8C8098FF"), Toque.SegurandoEm(BotaoVoltarIp));
        bool valido = IpValido(_ip);
        Tinta.Botao(BotaoEntrar, "Entrar", Color.FromHex("#4FA83FFF"), valido && Toque.SegurandoEm(BotaoEntrar), null, valido);
    }

    private void DesenharEspera(string titulo, string ajuda)
    {
        _visual.Desenhar(Tinta, B.Especie, B.Estagio, new Vector2(360f, 620f), 300f);
        DesenharPontinhos(titulo, 700f);
        Ajuda(ajuda, 790f);
        Tinta.Botao(BotaoVoltar, "Cancelar", Color.FromHex("#8C8098FF"), Toque.SegurandoEm(BotaoVoltar));
    }

    private void DesenharErro()
    {
        _visual.Desenhar(Tinta, B.Especie, B.Estagio, new Vector2(360f, 560f), 300f, triste: true);
        var painel = new Caixa(60f, 640f, 600f, 200f);
        Tinta.Cartao(painel, Tinta.Papel, Tinta.Tinteiro, 4f, 26f);
        Tinta.Paragrafo(_erro, new Vector2(360f, painel.Y + 30f), 540f, Tinta.Tinteiro, alinhar: Tinta.Alinhar.Centro);
        Tinta.Botao(BotaoVoltar, "Voltar", Color.FromHex("#8C8098FF"), Toque.SegurandoEm(BotaoVoltar));
    }

    private void DesenharPontinhos(string texto, float y)
    {
        string pontos = new('.', 1 + (int)(_t * 2.5f) % 3);
        Tinta.TextoContornado(texto + pontos.PadRight(3), new Vector2(360f, y), Color.White, Tinta.FonteMedia, 0.85f);
    }
}
