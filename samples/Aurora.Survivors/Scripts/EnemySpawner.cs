using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace Survivors;

/// <summary>
/// Diretor de spawn: faz inimigos nascerem em volta do jogador, fora da tela, cada vez mais
/// rápido e mais fortes conforme a partida avança — a curva de dificuldade do jogo mora aqui.
///
/// <para>Pra ter mais de um tipo de inimigo, o caminho mais curto é <c>Prefab</c> apontar pra uma
/// TABELA DE SPAWN (Assets/database/spawns.json): onde se escreve um prefab também se pode
/// escrever o id de uma tabela, e a engine sorteia por peso e condição sozinha. O outro caminho é
/// duplicar esta entidade na cena com outro prefab, outro intervalo e outro
/// <see cref="StartAfterSeconds"/>.</para>
/// </summary>
[SceneScript]
public sealed class EnemySpawner : Behavior
{
    /// <summary>Prefab (ou id de tabela de spawn) do inimigo.</summary>
    public string Prefab = "prefabs/morcego.json";

    public string TargetName = "Player";

    /// <summary>Só começa a agir depois deste tempo de partida — é o que escalona a entrada de
    /// cada tipo de inimigo quando você duplicar o spawner.</summary>
    public float StartAfterSeconds;

    /// <summary>Segundos entre nascimentos no começo da partida.</summary>
    public float StartInterval = 1.1f;

    /// <summary>Piso do intervalo: por mais que a partida avance, não nasce mais rápido que isso.</summary>
    public float MinInterval = 0.18f;

    /// <summary>A cada tantos segundos, o intervalo cai pela metade. Menor = fica difícil antes.</summary>
    public float IntervalHalfLife = 75f;

    /// <summary>Distância do jogador em que o inimigo nasce. Precisa passar da meia-diagonal da
    /// tela (734px numa tela de 1280x720 com zoom 1), senão o bicho aparece do nada nos cantos.</summary>
    public float SpawnDistance = 780f;

    /// <summary>Teto de inimigos vivos ao mesmo tempo, pra partida longa não virar slideshow.</summary>
    public int MaxAlive = 160;

    /// <summary>Quanto a vida do inimigo cresce por minuto de partida (0.5 = +50% por minuto).</summary>
    public float HealthPerMinute = 0.5f;

    /// <summary>Quanto a velocidade do inimigo cresce por minuto de partida.</summary>
    public float SpeedPerMinute = 0.06f;

    /// <summary>Horda: a cada tantos segundos nasce um anel inteiro de uma vez. 0 desliga.</summary>
    public float HordeEvery = 45f;

    /// <summary>Quantos inimigos vêm na horda.</summary>
    public int HordeAmount = 18;

    /// <summary>Tempo de partida em segundos — público pra HUD/depuração.</summary>
    public float Elapsed { get; private set; }

    private float _proximoSpawn;
    private int _hordasFeitas;

    public override void Update(float deltaTime)
    {
        if (World is null)
            return;

        Elapsed += deltaTime;
        if (Elapsed < StartAfterSeconds)
            return;

        if (HordeEvery > 0f && Elapsed >= (_hordasFeitas + 1) * HordeEvery)
        {
            _hordasFeitas++;
            NascerAnel(HordeAmount);
        }

        _proximoSpawn -= deltaTime;
        if (_proximoSpawn > 0f)
            return;

        _proximoSpawn = IntervaloAtual();
        Nascer(Random.Shared.NextSingle() * MathF.Tau);
    }

    /// <summary>Meia-vida: o intervalo cai pela metade a cada <see cref="IntervalHalfLife"/>
    /// segundos, com piso em <see cref="MinInterval"/>. Curva suave — sem degrau de "ficou
    /// impossível de uma vez" —, e um número só pra você regular o jogo inteiro.</summary>
    private float IntervaloAtual()
    {
        float minutos = Elapsed / MathF.Max(1f, IntervalHalfLife);
        return MathF.Max(MinInterval, StartInterval * MathF.Pow(0.5f, minutos));
    }

    private void NascerAnel(int quantidade)
    {
        for (int i = 0; i < quantidade; i++)
            Nascer(MathF.Tau * i / quantidade);
    }

    private void Nascer(float angulo)
    {
        if (World is null || !World.TryFind(TargetName, out var jogador)
            || jogador.Get<Transform>() is not { } centro)
            return;

        // A contagem só acontece na hora de nascer (não todo frame): varrer a cena inteira 60
        // vezes por segundo pra saber quantos morcegos existem seria o custo mais caro do jogo.
        if (ContarVivos() >= MaxAlive)
            return;

        var posicao = centro.Position
            + new Vector2(MathF.Cos(angulo), MathF.Sin(angulo)) * SpawnDistance;

        if (World.Spawn(Prefab, posicao) is not { } inimigo)
            return;

        float minutos = Elapsed / 60f;

        if (inimigo.Get<Health>() is { } vida)
        {
            vida.Max *= 1f + HealthPerMinute * minutos;
            vida.Current = vida.Max;
        }

        if (inimigo.Get<EnemyChaser>() is { } perseguidor)
            perseguidor.Speed *= 1f + SpeedPerMinute * minutos;
    }

    private int ContarVivos()
    {
        int total = 0;
        foreach (var (entity, _) in World!.Query<Health>())
        {
            if (Tags.Matches(entity, "#inimigo"))
                total++;
        }
        return total;
    }
}
