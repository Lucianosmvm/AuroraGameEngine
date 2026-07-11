using Aurora.Runtime.Ecs.Components;

namespace Aurora.Runtime.Ecs;

/// <summary>
/// Componente de script: lógica por entidade executada a cada frame pelo <see cref="World"/>.
/// Herde e sobrescreva <see cref="Start"/> / <see cref="Update"/>.
/// </summary>
public abstract class Behavior : IComponent
{
    public Entity Entity { get; internal set; }
    public bool Enabled { get; set; } = true;

    internal bool Started;

    /// <summary>Chamado uma vez, no primeiro frame em que o behavior está ativo.</summary>
    public virtual void Start() { }

    /// <summary>Chamado a cada frame com o delta em segundos.</summary>
    public virtual void Update(float deltaTime) { }

    /// <summary>Chamado após resolução de colisão sólida. Normal aponta para fora da outra entidade.</summary>
    public virtual void OnCollision(Entity other, CollisionInfo info) { }

    /// <summary>Chamado no primeiro frame em que esta entidade sobrepõe um trigger.</summary>
    public virtual void OnTriggerEnter(Entity other) { }

    /// <summary>Chamado no frame em que a sobreposição com o trigger termina.</summary>
    public virtual void OnTriggerExit(Entity other) { }

    protected T? Get<T>() where T : class, IComponent => Entity.Get<T>();
}
