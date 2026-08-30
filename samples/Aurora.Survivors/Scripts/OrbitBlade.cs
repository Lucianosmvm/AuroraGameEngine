using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace Survivors;

/// <summary>
/// Arma orbital: lâminas girando em volta do jogador, que machucam quem encostar. Fica sempre no
/// jogador, mas só existe de verdade quando <see cref="PlayerStats.OrbitBlades"/> passa de zero —
/// é assim que um upgrade "desbloqueia uma arma" em vez de só somar número.
///
/// <para>O dano de contato é do componente nativo ContactDamage, no prefab da lâmina; este script
/// só cria/destrói as lâminas, posiciona e mantém o dano em dia com a ficha do jogador.</para>
/// </summary>
[SceneScript]
public sealed class OrbitBlade : Behavior
{
    public string Prefab = "prefabs/lamina.json";

    /// <summary>Distância das lâminas até o jogador, em pixels.</summary>
    public float Radius = 64f;

    /// <summary>Graus por segundo da órbita.</summary>
    public float RotationSpeed = 170f;

    private readonly List<Entity> _laminas = [];
    private float _angulo;

    public override void Update(float deltaTime)
    {
        var stats = Get<PlayerStats>();
        if (World is null || stats is null || Get<Transform>() is not { } dono)
            return;

        // Lâmina destruída por fora (troca de cena, script seu) sai da lista antes da contagem,
        // senão o script acharia que ela ainda existe e a órbita ficaria com buraco.
        _laminas.RemoveAll(lamina => !lamina.IsAlive);

        int desejadas = Math.Max(0, stats.OrbitBlades);
        while (_laminas.Count < desejadas)
        {
            if (World.Spawn(Prefab, dono.Position) is not { } nova)
                break;
            _laminas.Add(nova);
        }

        while (_laminas.Count > desejadas)
        {
            _laminas[^1].Destroy();
            _laminas.RemoveAt(_laminas.Count - 1);
        }

        if (_laminas.Count == 0)
            return;

        _angulo += RotationSpeed * MathF.PI / 180f * deltaTime;
        float dano = stats.OrbitDamage * stats.DamageMultiplier;

        for (int i = 0; i < _laminas.Count; i++)
        {
            float angulo = _angulo + MathF.Tau * i / _laminas.Count;
            var posicao = dono.Position + new Vector2(MathF.Cos(angulo), MathF.Sin(angulo)) * Radius;

            if (_laminas[i].Get<Transform>() is { } transform)
            {
                transform.Position = posicao;
                transform.Rotation = angulo;
            }

            if (_laminas[i].Get<ContactDamage>() is { } contato)
                contato.Damage = dano;
        }
    }

    /// <summary>Jogador destruído leva as lâminas junto — senão elas ficariam girando em volta
    /// de um ponto vazio pelo resto da partida.</summary>
    public override void OnDestroy()
    {
        foreach (var lamina in _laminas)
        {
            if (lamina.IsAlive)
                lamina.Destroy();
        }
        _laminas.Clear();
    }
}
