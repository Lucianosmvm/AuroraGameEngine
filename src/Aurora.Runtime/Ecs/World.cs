using System.Numerics;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.Ecs;

/// <summary>
/// Contêiner de entidades e componentes. Armazenamento por tipo em dicionários —
/// simples de evoluir; trocar por sparse sets quando a contagem de entidades exigir.
/// </summary>
public sealed class World
{
    private readonly Dictionary<Type, Dictionary<int, IComponent>> _stores = new();
    private readonly Dictionary<int, string> _names = new();
    private readonly HashSet<int> _alive = new();
    private readonly List<Behavior> _behaviors = new();
    private readonly List<int> _destroyQueue = new();
    private readonly List<(int Layer, Transform Transform, IComponent Renderable)> _renderList = new();

    private int _nextId = 1;
    private bool _updating;

    public int EntityCount => _alive.Count;

    /// <summary>Remove todas as entidades e reseta o estado. Chamado ao carregar uma nova cena.</summary>
    public void Clear()
    {
        _alive.Clear();
        _names.Clear();
        _stores.Clear();
        _behaviors.Clear();
        _destroyQueue.Clear();
        _renderList.Clear();
        _nextId = 1;
    }

    public Entity CreateEntity(string name = "Entity")
    {
        int id = _nextId++;
        _alive.Add(id);
        _names[id] = name;
        return new Entity(id, this);
    }

    public bool IsAlive(int id) => _alive.Contains(id);

    public string GetName(int id) => _names.TryGetValue(id, out var name) ? name : "<destruída>";

    /// <summary>Todas as entidades vivas, em ordem de criação.</summary>
    public IEnumerable<Entity> Entities => _alive.OrderBy(id => id).Select(id => new Entity(id, this));

    /// <summary>Primeira entidade com o nome dado (nomes não são únicos).</summary>
    public bool TryFind(string name, out Entity entity)
    {
        foreach (var (id, entityName) in _names)
        {
            if (entityName == name && _alive.Contains(id))
            {
                entity = new Entity(id, this);
                return true;
            }
        }

        entity = default;
        return false;
    }

    /// <summary>Todos os componentes de uma entidade (serialização de cenas).</summary>
    public IEnumerable<IComponent> GetComponents(int entityId)
    {
        foreach (var store in _stores.Values)
        {
            if (store.TryGetValue(entityId, out var component))
                yield return component;
        }
    }

    public T Add<T>(int entityId, T component) where T : class, IComponent
    {
        if (!_alive.Contains(entityId))
            throw new InvalidOperationException($"Entidade {entityId} não existe ou foi destruída.");

        // Armazena pelo tipo concreto para que Get<PlayerController>() funcione com subclasses de Behavior.
        var type = component.GetType();
        if (!_stores.TryGetValue(type, out var store))
            _stores[type] = store = new Dictionary<int, IComponent>();

        store[entityId] = component;

        if (component is Behavior behavior)
        {
            behavior.Entity = new Entity(entityId, this);
            _behaviors.Add(behavior);
        }

        return component;
    }

    public T? Get<T>(int entityId) where T : class, IComponent
        => _stores.TryGetValue(typeof(T), out var store) && store.TryGetValue(entityId, out var c)
            ? (T)c
            : null;

    public void Destroy(Entity entity) => Destroy(entity.Id);

    /// <summary>Destruição durante Update é adiada para o fim do frame.</summary>
    public void Destroy(int id)
    {
        if (_updating)
            _destroyQueue.Add(id);
        else
            RemoveNow(id);
    }

    private void RemoveNow(int id)
    {
        if (!_alive.Remove(id))
            return;

        _names.Remove(id);
        foreach (var store in _stores.Values)
            store.Remove(id);
        _behaviors.RemoveAll(b => b.Entity.Id == id);
    }

    public IEnumerable<(Entity Entity, T1 C1)> Query<T1>()
        where T1 : class, IComponent
    {
        if (!_stores.TryGetValue(typeof(T1), out var s1))
            yield break;

        foreach (var (id, c1) in s1)
            yield return (new Entity(id, this), (T1)c1);
    }

    public IEnumerable<(Entity Entity, T1 C1, T2 C2)> Query<T1, T2>()
        where T1 : class, IComponent
        where T2 : class, IComponent
    {
        if (!_stores.TryGetValue(typeof(T1), out var s1) || !_stores.TryGetValue(typeof(T2), out var s2))
            yield break;

        foreach (var (id, c1) in s1)
        {
            if (s2.TryGetValue(id, out var c2))
                yield return (new Entity(id, this), (T1)c1, (T2)c2);
        }
    }

    /// <summary>Executa todos os behaviors ativos e processa destruições pendentes.</summary>
    public void Update(float deltaTime)
    {
        _updating = true;

        for (int i = 0; i < _behaviors.Count; i++)
        {
            var behavior = _behaviors[i];
            if (!behavior.Enabled || !_alive.Contains(behavior.Entity.Id))
                continue;

            if (!behavior.Started)
            {
                behavior.Started = true;
                behavior.Start();
            }

            behavior.Update(deltaTime);
        }

        _updating = false;

        if (_destroyQueue.Count > 0)
        {
            foreach (int id in _destroyQueue)
                RemoveNow(id);
            _destroyQueue.Clear();
        }
    }

    /// <summary>
    /// Desenha sprites e tilemaps intercalados por camada. Com câmera, tiles fora
    /// da tela são pulados (culling).
    /// </summary>
    public void Render(SpriteBatch batch, Camera2D? camera = null)
    {
        _renderList.Clear();

        foreach (var (_, transform, sprite) in Query<Transform, SpriteRenderer>())
        {
            if (sprite.Visible && sprite.Texture is not null)
                _renderList.Add((sprite.Layer, transform, sprite));
        }

        foreach (var (_, transform, tilemap) in Query<Transform, Tilemap>())
        {
            if (tilemap.Tileset is not null && tilemap.Width > 0 && tilemap.Height > 0)
                _renderList.Add((tilemap.Layer, transform, tilemap));
        }

        _renderList.Sort(static (a, b) => a.Layer.CompareTo(b.Layer));

        foreach (var (_, transform, renderable) in _renderList)
        {
            if (renderable is SpriteRenderer sprite)
            {
                var texture = sprite.Texture!;
                var source = sprite.SourceRect ?? new RectF(0f, 0f, texture.Width, texture.Height);
                var size = (sprite.Size ?? new Vector2(source.Width, source.Height)) * transform.Scale;
                batch.Draw(texture, transform.Position, size, sprite.Origin, transform.Rotation,
                    sprite.Color, source, sprite.FlipX, sprite.FlipY);
            }
            else if (renderable is Tilemap tilemap)
            {
                DrawTilemap(batch, camera, transform, tilemap);
            }
        }
    }

    private static void DrawTilemap(SpriteBatch batch, Camera2D? camera, Transform transform, Tilemap map)
    {
        map.EnsureSize();

        float cellWidth = map.TileWidth * transform.Scale.X;
        float cellHeight = map.TileHeight * transform.Scale.Y;
        if (cellWidth <= 0f || cellHeight <= 0f)
            return;

        int firstX = 0, firstY = 0, lastX = map.Width - 1, lastY = map.Height - 1;

        if (camera is not null)
        {
            var (min, max) = camera.GetVisibleBounds();
            firstX = Math.Max(0, (int)MathF.Floor((min.X - transform.Position.X) / cellWidth));
            firstY = Math.Max(0, (int)MathF.Floor((min.Y - transform.Position.Y) / cellHeight));
            lastX = Math.Min(map.Width - 1, (int)MathF.Ceiling((max.X - transform.Position.X) / cellWidth));
            lastY = Math.Min(map.Height - 1, (int)MathF.Ceiling((max.Y - transform.Position.Y) / cellHeight));
        }

        var cellSize = new Vector2(cellWidth, cellHeight);

        for (int y = firstY; y <= lastY; y++)
        {
            for (int x = firstX; x <= lastX; x++)
            {
                int index = map.Tiles[y * map.Width + x];
                if (index < 0)
                    continue;

                var position = transform.Position + new Vector2(x * cellWidth, y * cellHeight);
                batch.Draw(map.Tileset!, position, cellSize, Vector2.Zero, 0f,
                    Color.White, map.SourceRect(index));
            }
        }
    }
}
