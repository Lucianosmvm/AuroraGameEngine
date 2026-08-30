using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace Survivors;

/// <summary>
/// A cola entre a ficha (<see cref="PlayerStats"/>) e os componentes nativos: aplica velocidade
/// e vida máxima, regenera, segura o jogador dentro da arena e espelha a vida nas variáveis que
/// a HUD lê. Sem ele os upgrades mudariam números que ninguém consulta.
/// </summary>
[SceneScript]
public sealed class PlayerRunner : Behavior
{
    /// <summary>Metade da largura da arena em pixels — o jogador não passa daqui. Casa com o
    /// Tilemap de scenes/arena.json (64 tiles de 32px = 2048, metade = 1024, menos uma folga).</summary>
    public float ArenaHalfWidth = 1000f;

    /// <summary>Metade da altura da arena, mesma ideia.</summary>
    public float ArenaHalfHeight = 1000f;

    public override void Start()
    {
        var stats = Get<PlayerStats>();
        if (stats is null || World?.State is null)
            return;

        // Melhorias compradas na loja entram ANTES da vida ser aplicada: quem comprou +vida
        // precisa nascer já com ela cheia, não com a vida da cena.
        MetaShop.AplicarEm(stats, World.State);

        if (Get<Health>() is { } health)
        {
            health.Max = stats.MaxHealth;
            health.Current = stats.MaxHealth;
        }
    }

    public override void Update(float deltaTime)
    {
        var stats = Get<PlayerStats>();
        var health = Get<Health>();
        if (stats is null || World is null)
            return;

        if (Get<TopDownController>() is { } controller)
            controller.Speed = stats.MoveSpeed;

        if (health is not null)
        {
            if (stats.RegenPerSecond > 0f && health.Current < health.Max)
                World.Heal(Entity, stats.RegenPerSecond * deltaTime);

            // A HUD lê variável do GameState, não componente (ver UiBar.Variable) — este é o
            // ponto único onde vida vira número de tela.
            World.State?.SetVariable("Vida", MathF.Round(health.Current));
            World.State?.SetVariable("VidaMax", MathF.Round(health.Max));
            World.State?.SetVariable("VidaPct", health.Max > 0f ? health.Current / health.Max * 100f : 0f);
        }

        if (Get<Transform>() is { } transform)
        {
            transform.Position = new Vector2(
                Math.Clamp(transform.Position.X, -ArenaHalfWidth, ArenaHalfWidth),
                Math.Clamp(transform.Position.Y, -ArenaHalfHeight, ArenaHalfHeight));
        }
    }

    /// <summary>
    /// Armadura devolvendo parte do dano já aplicado. O World.Damage não tem gancho de "antes do
    /// dano", então reduzir de verdade exigiria reimplementar dano e i-frames — curar a fração
    /// blindada no mesmo frame dá o mesmo resultado na vida e mantém knockback, i-frames e
    /// OnDeath nativos funcionando.
    /// </summary>
    public override void OnDamaged(float amount, Entity? source)
    {
        var stats = Get<PlayerStats>();
        if (stats is null || stats.Armor <= 0f)
            return;

        World?.Heal(Entity, amount * Math.Clamp(stats.Armor, 0f, 0.8f));
    }
}
