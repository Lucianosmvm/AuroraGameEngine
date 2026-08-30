namespace Survivors;

/// <summary>Uma melhoria oferecida ao subir de nível durante a partida.</summary>
public sealed class Upgrade
{
    /// <summary>Chave interna — é por ela que o <see cref="RunManager"/> conta quantas vezes a
    /// melhoria já foi escolhida nesta partida.</summary>
    public required string Id { get; init; }

    public required string Nome { get; init; }

    /// <summary>Uma linha explicando o efeito, mostrada embaixo do botão.</summary>
    public required string Descricao { get; init; }

    /// <summary>Quantas vezes pode ser escolhida numa partida. No teto, sai do sorteio.</summary>
    public int MaxNivel { get; init; } = 5;

    /// <summary>O efeito. Mexe SÓ na ficha do jogador — é o que mantém arma, inimigo e HUD fora
    /// desta lista.</summary>
    public required Action<PlayerStats> Aplicar { get; init; }
}

/// <summary>
/// Catálogo de melhorias de level up. Pra criar uma nova, acrescente um item nesta lista: o
/// sorteio, a tela de escolha e a contagem de nível já funcionam sozinhos.
/// </summary>
public static class UpgradeCatalog
{
    public static readonly IReadOnlyList<Upgrade> Todos =
    [
        new()
        {
            Id = "dano", Nome = "Lâmina Afiada", Descricao = "+20% de dano em todas as armas",
            MaxNivel = 8, Aplicar = s => s.DamageMultiplier += 0.20f,
        },
        new()
        {
            Id = "cadencia", Nome = "Gatilho Rápido", Descricao = "+18% de velocidade de ataque",
            MaxNivel = 8, Aplicar = s => s.FireRateMultiplier += 0.18f,
        },
        new()
        {
            Id = "projetil", Nome = "Projétil Extra", Descricao = "+1 projétil por disparo",
            MaxNivel = 4, Aplicar = s => s.ProjectileCount += 1,
        },
        new()
        {
            Id = "orbital", Nome = "Lâmina Orbital", Descricao = "+1 lâmina girando em volta de você",
            MaxNivel = 4, Aplicar = s => s.OrbitBlades += 1,
        },
        new()
        {
            Id = "botas", Nome = "Botas Leves", Descricao = "+12% de velocidade de movimento",
            MaxNivel = 5, Aplicar = s => s.MoveSpeed *= 1.12f,
        },
        new()
        {
            Id = "vida", Nome = "Coração Robusto", Descricao = "+25 de vida máxima (e cura o mesmo tanto)",
            MaxNivel = 6, Aplicar = s => s.MaxHealth += 25f,
        },
        new()
        {
            Id = "armadura", Nome = "Couraça", Descricao = "Reduz em 8% todo dano recebido",
            MaxNivel = 5, Aplicar = s => s.Armor = MathF.Min(0.8f, s.Armor + 0.08f),
        },
        new()
        {
            Id = "regen", Nome = "Regeneração", Descricao = "+0,6 de vida por segundo",
            MaxNivel = 4, Aplicar = s => s.RegenPerSecond += 0.6f,
        },
        new()
        {
            Id = "ima", Nome = "Ímã", Descricao = "+40 de raio de coleta",
            MaxNivel = 4, Aplicar = s => s.PickupRadius += 40f,
        },
        new()
        {
            Id = "sabedoria", Nome = "Sabedoria", Descricao = "+15% de XP por gema",
            MaxNivel = 4, Aplicar = s => s.XpMultiplier += 0.15f,
        },
        new()
        {
            Id = "balistica", Nome = "Balística", Descricao = "+25% de velocidade dos projéteis",
            MaxNivel = 3, Aplicar = s => s.ProjectileSpeed *= 1.25f,
        },
    ];
}
