using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Scenes;

namespace CaminhosDaFe;

/// <summary>
/// O lobo da Missão 02. Dorme na toca até a <see cref="Missao"/> ligar o switch
/// <see cref="SwitchDespertar"/>; aí desce rondando rumo ao curral e, se enxergar o Davi, caça.
///
/// <para>O ataque é telegrafado de propósito: ele para, se encolhe (pose de bote) e só então
/// salta em linha reta. É essa pausa que dá ao jogador a chance de sair da frente ou acertar uma
/// pedra — sem ela o combate vira troca de dano parado.</para>
/// </summary>
[SceneScript]
public sealed class Lobo : Behavior
{
    public string SwitchDespertar = "LoboAcordado";
    public string NomeJogador = "Player";

    /// <summary>Distância em que ele nota o Davi.</summary>
    public float Visao = 110f;

    public float VelocidadeRonda = 34f;
    public float VelocidadeCaca = 58f;

    /// <summary>A esta distância ele começa o bote.</summary>
    public float DistanciaBote = 34f;

    public float TempoPreparo = 0.45f;
    public float VelocidadeBote = 190f;
    public float DuracaoBote = 0.22f;
    public float Dano = 15f;
    public float Empurrao = 12f;

    /// <summary>Segundos se afastando depois de um bote (acertando ou não).</summary>
    public float TempoRecuo = 0.9f;

    private enum Estado { Dormindo, Rondando, Cacando, Preparando, Saltando, Recuando, Atordoado, Morto }

    private Estado _estado = Estado.Dormindo;
    private float _timer;
    private float _repath;
    private float _piscar;
    private bool _acertou;
    private Vector2 _direcaoBote;
    private Vector2 _ultimaPosicao;

    public override void Start() => _ultimaPosicao = Get<Transform>()?.Position ?? Vector2.Zero;

    public override void Update(float deltaTime)
    {
        if (World is null || Get<Transform>() is not { } transform)
            return;

        var nav = Get<NavAgent>();
        var jogador = World.TryFind(NomeJogador, out var j) && j.Get<Health>() is { IsDead: false } ? j : (Entity?)null;
        var posJogador = jogador?.Get<Transform>()?.Position;
        float distancia = posJogador is { } pj ? Vector2.Distance(transform.Position, pj) : float.MaxValue;

        _timer -= deltaTime;

        switch (_estado)
        {
            case Estado.Dormindo:
                if (World.State?.GetSwitch(SwitchDespertar) == true)
                    _estado = Estado.Rondando;
                break;

            case Estado.Rondando:
                Mover(nav, VelocidadeRonda);
                if (distancia < Visao)
                {
                    _estado = Estado.Cacando;
                    _repath = 0f;
                }
                else if (!nav!.HasTarget || _repath <= 0f)
                {
                    // Sem o Davi à vista, ronda o curral: é o rebanho que ele quer.
                    var curral = Curral.Da(World);
                    var alvo = curral is not null ? curral.Centro + new Vector2(90f, -70f) : transform.Position;
                    nav.SetTarget(alvo + new Vector2(Random.Shared.NextSingle() * 60f - 30f, Random.Shared.NextSingle() * 40f - 20f));
                    _repath = 3f;
                }
                _repath -= deltaTime;
                break;

            case Estado.Cacando:
                Mover(nav, VelocidadeCaca);
                _repath -= deltaTime;
                if (posJogador is null || distancia > Visao * 1.8f)
                {
                    _estado = Estado.Rondando;
                }
                else if (distancia < DistanciaBote)
                {
                    Parar(nav);
                    _estado = Estado.Preparando;
                    _timer = TempoPreparo;
                    _direcaoBote = Vector2.Normalize(posJogador.Value - transform.Position);
                }
                else if (_repath <= 0f)
                {
                    nav!.SetTarget(posJogador.Value);
                    _repath = 0.25f;
                }
                break;

            case Estado.Preparando:
                // Ajusta a mira enquanto se encolhe — mas trava no fim, senão não dá pra desviar.
                if (posJogador is { } p && _timer > TempoPreparo * 0.4f && distancia > 1f)
                    _direcaoBote = Vector2.Normalize(p - transform.Position);
                if (_timer <= 0f)
                {
                    _estado = Estado.Saltando;
                    _timer = DuracaoBote;
                    _acertou = false;
                }
                break;

            case Estado.Saltando:
                transform.Position += _direcaoBote * VelocidadeBote * deltaTime;
                if (!_acertou && jogador is { } alvoBote && distancia < 13f)
                {
                    _acertou = true;
                    World.Damage(alvoBote, Dano, Entity);
                    if (alvoBote.Get<Transform>() is { } t)
                        t.Position += _direcaoBote * Empurrao;
                }
                if (_timer <= 0f)
                {
                    _estado = Estado.Recuando;
                    _timer = TempoRecuo;
                }
                break;

            case Estado.Recuando:
                if (posJogador is { } pr && distancia > 1f)
                    transform.Position -= Vector2.Normalize(pr - transform.Position) * VelocidadeRonda * deltaTime;
                if (_timer <= 0f)
                    _estado = Estado.Cacando;
                break;

            case Estado.Atordoado:
                if (_timer <= 0f)
                    _estado = Estado.Cacando;
                break;

            case Estado.Morto:
                if (Get<SpriteRenderer>() is { } sprite)
                    sprite.Color = new Color(1f, 1f, 1f, MathF.Max(0f, _timer));
                if (_timer <= 0f)
                    Entity.Destroy();
                break;
        }

        Animar(transform.Position, deltaTime);
    }

    public override void OnDamaged(float amount, Entity? source)
    {
        _piscar = 0.25f;

        // Pedra interrompe o bote: acertar no meio do preparo é a jogada boa.
        if (_estado is Estado.Morto or Estado.Saltando)
            return;

        Parar(Get<NavAgent>());
        _estado = Estado.Atordoado;
        _timer = 0.35f;
    }

    public override void OnDeath()
    {
        Parar(Get<NavAgent>());
        _estado = Estado.Morto;
        _timer = 1f;
        World?.Remove<Collider>(Entity.Id);
        Missao.LoboDerrotado(World);
    }

    private static void Mover(NavAgent? nav, float velocidade)
    {
        if (nav is null)
            return;
        nav.Enabled = true;
        nav.Speed = velocidade;
    }

    private static void Parar(NavAgent? nav)
    {
        if (nav is null)
            return;
        nav.Stop();
        nav.Enabled = false;
    }

    private void Animar(Vector2 posicao, float deltaTime)
    {
        var movimento = posicao - _ultimaPosicao;
        _ultimaPosicao = posicao;

        string clipe = _estado switch
        {
            Estado.Preparando or Estado.Saltando => "bote",
            Estado.Dormindo or Estado.Atordoado or Estado.Morto => "parado",
            _ => movimento.LengthSquared() > 0.0004f ? "andar" : "parado",
        };
        Get<Animator>()?.Play(clipe);

        if (Get<SpriteRenderer>() is not { } sprite)
            return;

        float olharX = _estado is Estado.Preparando or Estado.Saltando ? _direcaoBote.X : movimento.X;
        if (MathF.Abs(olharX) > 0.05f && _estado != Estado.Recuando)
            sprite.FlipX = olharX < 0f;

        if (_estado == Estado.Morto)
            return;

        _piscar = MathF.Max(0f, _piscar - deltaTime);
        sprite.Color = _piscar > 0f ? new Color(1f, 0.4f, 0.4f, 1f)
            : _estado == Estado.Preparando ? new Color(1f, 0.85f, 0.75f, 1f)
            : Color.White;
    }
}
