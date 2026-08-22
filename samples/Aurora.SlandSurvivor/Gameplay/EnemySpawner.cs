using System.Numerics;
using Aurora.SlandSurvivor.Worlds;

namespace Aurora.SlandSurvivor.Gameplay;

/// <summary>
/// Decide quando e onde nasce bicho. Regras do gênero: superfície só depois do anoitecer,
/// caverna a qualquer hora, sempre fora da tela e nunca dentro da rocha.
///
/// <para>O sorteio é por COLUNA, não por ponto solto ao redor do jogador: escolhe um x a uma
/// distância que já está fora da tela e procura o chão daquela coluna. Sorteando pontos num
/// anel, quase todo candidato caía no céu ou dentro da pedra, e em terreno acidentado dava
/// para passar a noite inteira sem nascer nada.</para>
/// </summary>
public sealed class EnemySpawner
{
    private const int MinTiles = 23;   // ~368 px: já fora da tela com zoom 2
    private const int MaxTiles = 40;   // ~640 px

    private readonly Random _random = new();
    private float _timer;

    /// <summary>Teto de inimigos vivos ao mesmo tempo.</summary>
    public int MaxAlive { get; set; } = 9;

    /// <summary>Intervalo entre tentativas de nascimento, em segundos.</summary>
    public float Interval { get; set; } = 1.4f;

    public void Update(SurvivorGame game, TileWorld tiles, float deltaTime)
    {
        _timer -= deltaTime;
        if (_timer > 0f)
            return;

        _timer = Interval;

        if (game.PlayerTransform is not { } player || game.EnemyCount >= MaxAlive)
            return;

        int playerX = Math.Clamp(TileWorld.ToTile(player.Position.X), 0, tiles.Width - 1);
        int playerY = TileWorld.ToTile(player.Position.Y);
        bool underground = playerY > tiles.SkyTop(playerX) + 18;

        if (!underground && game.IsDaytime)
            return;                                        // dia claro na superfície: nada nasce

        for (int attempt = 0; attempt < 10; attempt++)
        {
            int offset = _random.Next(MinTiles, MaxTiles + 1) * (_random.Next(2) == 0 ? -1 : 1);
            int x = playerX + offset;
            if (x < 4 || x >= tiles.Width - 4)
                continue;

            var kind = PickKind(underground);

            if (TryFindSpot(tiles, x, playerY, underground, kind == EnemyKind.Bat, out int y))
            {
                game.SpawnEnemy(kind, TileWorld.TileCenter(x, y) + new Vector2(0f, -2f));
                return;
            }
        }
    }

    /// <summary>
    /// Procura na coluna uma linha onde o bicho caiba: na superfície é logo acima do chão;
    /// na caverna, o primeiro bolsão de ar perto da altura do jogador.
    /// </summary>
    private static bool TryFindSpot(TileWorld tiles, int x, int playerY, bool underground,
        bool flying, out int y)
    {
        if (!underground)
        {
            y = tiles.SkyTop(x) - 1;
            return Fits(tiles, x, y, flying);
        }

        for (int distance = 0; distance <= 14; distance++)
        {
            for (int direction = -1; direction <= 1; direction += 2)
            {
                y = playerY + distance * direction;
                if (y > 2 && y < tiles.Height - 2 && Fits(tiles, x, y, flying))
                    return true;
            }
        }

        y = 0;
        return false;
    }

    /// <summary>Voador precisa só de ar; quem anda precisa de dois tiles livres com chão embaixo.</summary>
    private static bool Fits(TileWorld tiles, int x, int y, bool flying)
    {
        if (tiles.IsSolid(x, y) || tiles.IsSolid(x, y - 1) || tiles.IsLiquid(x, y))
            return false;

        return flying || tiles.IsSolid(x, y + 1);
    }

    private EnemyKind PickKind(bool underground)
    {
        double roll = _random.NextDouble();

        if (underground)
            return roll < 0.45 ? EnemyKind.Bat : roll < 0.8 ? EnemyKind.Slime : EnemyKind.Zombie;

        return roll < 0.55 ? EnemyKind.Slime : EnemyKind.Zombie;
    }
}
