using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace CaminhosDaFe;

/// <summary>
/// Ovelha do rebanho. Três estados:
/// <list type="bullet">
/// <item><b>Pastando</b> (perdida): anda à toa perto de onde nasceu e aceita o "[E] Chamar".</item>
/// <item><b>Seguindo</b>: vai atrás do Davi contornando cerca e rocha (NavAgent) até pisar no
/// curral.</item>
/// <item><b>No curral</b>: conta como recolhida e passa a pastar só lá dentro.</item>
/// </list>
/// As ovelhas do rebanho nascem com <see cref="Perdida"/> = false e já começam no curral.
/// </summary>
[SceneScript]
public sealed class Ovelha : Behavior, IInteracao
{
    public bool Perdida = true;

    /// <summary>Até onde ela se afasta do ponto onde nasceu enquanto pasta.</summary>
    public float RaioPasto = 28f;

    public float VelocidadePasto = 18f;

    /// <summary>Seguindo, ela para a esta distância do Davi (pra não ficar empurrando ele).</summary>
    public float DistanciaSeguir = 18f;

    /// <summary>A esta distância da cerca a ovelha que segue o Davi entra sozinha.</summary>
    public float DistanciaEntrar = 80f;

    public string NomeJogador = "Player";

    /// <summary>Segundos restantes do balão "Bééé!" — desenhado pelo CaminhosGame.</summary>
    public float Balindo { get; private set; }

    public bool NoCurral => _estado == Estado.NoCurral;

    private enum Estado { Pastando, Seguindo, NoCurral }

    private Estado _estado;
    private Vector2 _casa;
    private Vector2 _destino;
    private float _pausa;
    private float _repath;
    private float _andando;
    private Vector2 _ultimaPosicao;

    public override void Start()
    {
        var posicao = Get<Transform>()?.Position ?? Vector2.Zero;
        _casa = posicao;
        _destino = posicao;
        _ultimaPosicao = posicao;
        _estado = Perdida ? Estado.Pastando : Estado.NoCurral;
        _pausa = 0.5f + Random.Shared.NextSingle() * 2f;

        if (Get<Interagivel>() is { } interagivel)
            interagivel.Ativo = Perdida;
    }

    public void Interagir(Entity quem)
    {
        if (_estado != Estado.Pastando)
            return;

        _estado = Estado.Seguindo;
        Balindo = 1.4f;

        if (Get<Interagivel>() is { } interagivel)
            interagivel.Ativo = false;
    }

    public override void Update(float deltaTime)
    {
        if (World is null || Get<Transform>() is not { } transform)
            return;

        Balindo = MathF.Max(0f, Balindo - deltaTime);

        switch (_estado)
        {
            case Estado.Pastando:
                Pastar(transform, deltaTime, null);
                break;
            case Estado.Seguindo:
                Seguir(transform, deltaTime);
                break;
            case Estado.NoCurral:
                Pastar(transform, deltaTime, Curral.Da(World));
                break;
        }

        Animar(transform.Position, deltaTime);
    }

    private void Pastar(Transform transform, float deltaTime, Curral? curral)
    {
        if (_pausa > 0f)
        {
            _pausa -= deltaTime;
            if (_pausa <= 0f)
            {
                _destino = curral is not null
                    ? curral.PontoAleatorio(12f)
                    : _casa + new Vector2(Random.Shared.NextSingle() * 2f - 1f, Random.Shared.NextSingle() * 2f - 1f) * RaioPasto;
            }
            return;
        }

        var delta = _destino - transform.Position;
        float distancia = delta.Length();
        float passo = VelocidadePasto * deltaTime;
        _andando += deltaTime;

        // Destino atrás de uma rocha: a colisão segura a ovelha no lugar. Depois de alguns
        // segundos tentando, ela desiste e escolhe outro canto, em vez de marchar parada.
        if (distancia <= 1.5f || _andando > 4f)
        {
            _pausa = 1.5f + Random.Shared.NextSingle() * 3.5f;
            _andando = 0f;
            return;
        }

        transform.Position += delta / distancia * MathF.Min(passo, distancia);
    }

    private void Seguir(Transform transform, float deltaTime)
    {
        var nav = Get<NavAgent>();
        var curral = Curral.Da(World);

        if (curral is not null && curral.Contem(transform.Position, 6f))
        {
            EntrarNoCurral(nav);
            return;
        }

        if (nav is null || !World!.TryFind(NomeJogador, out var jogador) || jogador.Get<Transform>() is not { } alvo)
            return;

        nav.Enabled = true;
        _repath -= deltaTime;

        // Perto do curral ela entra sozinha (o A* acha a porteira): o Davi só precisa trazê-la
        // até ali, sem ter que entrar junto e sair de novo.
        if (curral is not null && curral.DistanciaAte(transform.Position) < DistanciaEntrar)
        {
            if (_repath <= 0f)
            {
                nav.SetTarget(curral.Centro);
                _repath = 0.5f;
            }
            return;
        }

        if (Vector2.Distance(transform.Position, alvo.Position) <= DistanciaSeguir)
        {
            nav.Stop();
            return;
        }

        if (_repath <= 0f || !nav.HasTarget)
        {
            nav.SetTarget(alvo.Position);
            _repath = 0.3f;
        }
    }

    private void EntrarNoCurral(NavAgent? nav)
    {
        if (nav is not null)
        {
            nav.Stop();
            nav.Enabled = false;
        }

        _estado = Estado.NoCurral;
        _pausa = 0.3f;
        Balindo = 1.4f;
        Missao.OvelhaRecolhida(World);
    }

    private void Animar(Vector2 posicao, float deltaTime)
    {
        var movimento = posicao - _ultimaPosicao;
        _ultimaPosicao = posicao;

        bool andando = deltaTime > 0f && movimento.LengthSquared() > 0.0004f;
        Get<Animator>()?.Play(andando ? "andar" : "parado");

        if (MathF.Abs(movimento.X) > 0.05f && Get<SpriteRenderer>() is { } sprite)
            sprite.FlipX = movimento.X < 0f;
    }
}
