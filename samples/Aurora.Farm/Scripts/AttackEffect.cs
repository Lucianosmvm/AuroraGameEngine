using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace AuroraFarm;

/// <summary>
/// Vida curta de um efeito instanciado: acompanha quem lançou e se destrói quando a animação
/// termina. Sem isso, cada clique deixaria uma entidade parada no cenário pra sempre.
///
/// <para>Quem cria e configura este script é o <see cref="PlayerAttack"/>; ele não é feito
/// pra ser colocado à mão numa entidade da cena.</para>
/// </summary>
[SceneScript]
public sealed class AttackEffect : Behavior
{
    /// <summary>Quem lançou o efeito. Null (ou entidade já morta) = o efeito fica parado onde
    /// nasceu — nullable de propósito, porque um <c>Entity</c> default não aponta pra World
    /// nenhum e estoura ao ler IsAlive.</summary>
    public Entity? Owner;

    /// <summary>Deslocamento em relação ao dono, em coordenadas de mundo.</summary>
    public Vector2 Offset;

    /// <summary>
    /// Rede de segurança em segundos: se a entidade não tiver Animator (ou o clipe estiver em
    /// loop), ela morre por tempo em vez de vazar pra sempre.
    /// </summary>
    public float MaxLife = 1.5f;

    private float _age;

    public override void Update(float deltaTime)
    {
        _age += deltaTime;

        var transform = Get<Transform>();
        if (transform is null || World is null)
            return;

        if (Owner is { IsAlive: true } owner && owner.Get<Transform>() is { } ownerTransform)
            transform.Position = ownerTransform.Position + Offset;

        // O Animator marca IsFinished no último quadro de um clipe com Loop = false.
        bool finished = Get<Animator>() is { IsFinished: true };

        if (finished || _age >= MaxLife)
            World.Destroy(Entity);
    }
}
