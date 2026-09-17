using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;
using Silk.NET.Input;

namespace CaminhosDaFe;

/// <summary>
/// O jogador. Quem anda é o <see cref="TopDownController"/> nativo e quem arremessa é a
/// <see cref="Funda"/>; aqui fica a cola: animação por direção (4 poses), a tecla E de interagir,
/// o piscar ao levar dano e as variáveis de vida que a HUD lê.
/// </summary>
[SceneScript]
public sealed class Davi : Behavior
{
    public float Velocidade = 72f;

    /// <summary>Tecla de interação. E no teclado; A no controle.</summary>
    public string TeclaInteragir = "E";

    /// <summary>O interagível que receberia o E agora — a HUD desenha a dica em cima dele.</summary>
    public Entity? AlvoProximo { get; private set; }

    private string _direcao = "baixo";
    private float _piscar;

    public override void Update(float deltaTime)
    {
        if (World is null || Get<Transform>() is not { } transform)
            return;

        var vida = Get<Health>();
        var controle = Get<TopDownController>();
        var funda = Get<Funda>();

        if (controle is not null)
        {
            controle.Enabled = vida is not { IsDead: true };
            controle.Speed = Velocidade * (funda is { Mirando: true } ? funda.LentidaoMirando : 1f);
        }

        Animar(controle, funda);
        Piscar(deltaTime);
        ProcurarInteragivel(transform.Position);

        if (AlvoProximo is { } alvo && ApertouInteragir() && World.Dialogue?.IsActive != true)
            alvo.Get<Interagivel>()?.Acionar(Entity);

        if (vida is not null && World.State is { } state)
        {
            state.SetVariable("Vida", MathF.Ceiling(vida.Current));
            state.SetVariable("VidaMax", vida.Max);
            state.SetVariable("VidaPct", vida.Max > 0f ? vida.Current / vida.Max * 100f : 0f);
        }
    }

    public override void OnDamaged(float amount, Entity? source) => _piscar = 0.6f;

    private bool ApertouInteragir()
    {
        var input = World!.Input;
        if (input is null)
            return false;

        return (Enum.TryParse<Key>(TeclaInteragir, true, out var tecla) && input.WasKeyPressed(tecla))
            || input.WasGamepadButtonPressed(ButtonName.A);
    }

    private void Animar(TopDownController? controle, Funda? funda)
    {
        bool arremessando = funda is not null && (funda.Mirando || funda.DesdeArremesso < 0.2f);
        var olhar = arremessando ? funda!.Direcao : controle?.Velocity ?? Vector2.Zero;

        if (olhar.LengthSquared() > 0.01f)
        {
            if (MathF.Abs(olhar.X) > MathF.Abs(olhar.Y))
            {
                _direcao = "lado";
                if (Get<SpriteRenderer>() is { } sprite)
                    sprite.FlipX = olhar.X < 0f;
            }
            else
            {
                _direcao = olhar.Y < 0f ? "cima" : "baixo";
            }
        }

        bool andando = controle is not null && controle.Velocity.LengthSquared() > 1f;
        string clipe = arremessando ? "arremesso" : andando ? "andar" : "parado";
        Get<Animator>()?.Play($"{clipe}_{_direcao}");
    }

    private void Piscar(float deltaTime)
    {
        if (Get<SpriteRenderer>() is not { } sprite)
            return;

        _piscar = MathF.Max(0f, _piscar - deltaTime);
        bool vermelho = _piscar > 0f && (int)(_piscar * 12f) % 2 == 0;
        sprite.Color = vermelho
            ? new Aurora.Runtime.Graphics.Color(1f, 0.45f, 0.45f, 1f)
            : Aurora.Runtime.Graphics.Color.White;
    }

    private void ProcurarInteragivel(Vector2 posicao)
    {
        var pes = posicao + new Vector2(0f, 5f);
        AlvoProximo = null;
        float melhor = float.MaxValue;

        foreach (var (entidade, interagivel) in World!.Query<Interagivel>())
        {
            if (!interagivel.Ativo || entidade.Get<Transform>() is not { } t)
                continue;

            float distancia = Vector2.Distance(pes, t.Position);
            if (distancia <= interagivel.Raio && distancia < melhor)
            {
                melhor = distancia;
                AlvoProximo = entidade;
            }
        }
    }
}
