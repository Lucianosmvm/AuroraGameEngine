namespace Aurora.Runtime.Ecs;

/// <summary>
/// Identificador leve de uma entidade. Todos os dados vivem no <see cref="World"/>;
/// esta struct só carrega o id e um atalho para a API do mundo.
/// </summary>
public readonly struct Entity : IEquatable<Entity>
{
    public int Id { get; }

    private readonly World _world;

    internal Entity(int id, World world)
    {
        Id = id;
        _world = world;
    }

    public string Name => _world.GetName(Id);
    public bool IsAlive => _world.IsAlive(Id);

    public T Add<T>(T component) where T : class, IComponent => _world.Add(Id, component);
    public T? Get<T>() where T : class, IComponent => _world.Get<T>(Id);
    public bool Has<T>() where T : class, IComponent => _world.Get<T>(Id) is not null;
    public void Destroy() => _world.Destroy(Id);

    public bool Equals(Entity other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is Entity e && Equals(e);
    public override int GetHashCode() => Id;
    public override string ToString() => $"{Name}#{Id}";

    public static bool operator ==(Entity a, Entity b) => a.Id == b.Id;
    public static bool operator !=(Entity a, Entity b) => a.Id != b.Id;
}
