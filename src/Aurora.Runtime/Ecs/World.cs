using System.Numerics;
using Aurora.Runtime.AI;
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

    private readonly List<(Entity Entity, Transform Transform, Collider Collider)> _collisionBuffer = [];
    private readonly List<(Entity Entity, Transform Transform, Tilemap Tilemap)> _tilemapBuffer = [];
    private readonly Collider _tileCollider = new() { IsKinematic = true };
    private HashSet<long> _activeTriggers = [];
    private HashSet<long> _prevTriggers = [];

    private int _nextId = 1;
    private bool _updating;
    private readonly Random _random = new();
    private NavGrid? _navGrid;

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
        _collisionBuffer.Clear();
        _tilemapBuffer.Clear();
        _activeTriggers.Clear();
        _prevTriggers.Clear();
        _navGrid = null;
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
            behavior.World = this;
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

        // Snapshot: OnDestroy pode chamar Destroy() em cascata, modificando _behaviors
        foreach (var b in _behaviors.Where(b => b.Entity.Id == id).ToArray())
            b.OnDestroy();

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

    /// <summary>Executa todos os behaviors ativos, detecta colisões e processa destruições pendentes.</summary>
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

        UpdateNavAgents(deltaTime);
        ProcessCollisions();
        UpdateParticles(deltaTime);
        UpdateHealth(deltaTime);

        _updating = false;

        if (_destroyQueue.Count > 0)
        {
            foreach (int id in _destroyQueue)
                RemoveNow(id);
            _destroyQueue.Clear();
        }
    }

    /// <summary>Nasce/envelhece/mata partículas de todo ParticleEmitter vivo.</summary>
    /// <summary>
    /// Move todo NavAgent com alvo definido: calcula caminho (A* se houver tilemap com
    /// SolidTiles, reto senão) na primeira vez e avança waypoint a waypoint.
    /// </summary>
    private void UpdateNavAgents(float deltaTime)
    {
        foreach (var (_, transform, agent) in Query<Transform, NavAgent>())
        {
            if (!agent.HasTarget)
                continue;

            if (agent.Path is null)
            {
                var grid = EnsureNavGrid();
                // A grade só é uma restrição válida dentro da própria área mapeada - fora
                // dela (ou sem tilemap na cena) não há dado de bloqueio, então anda reto.
                bool useGrid = grid is not null
                    && IsWithinGrid(grid, transform.Position) && IsWithinGrid(grid, agent.Target);

                agent.Path = useGrid
                    ? AStarPathfinder.FindPath(grid!, transform.Position, agent.Target)
                    : [agent.Target];
                agent.WaypointIndex = 0;

                if (agent.Path is null)
                {
                    agent.HasTarget = false; // destino bloqueado/inalcançável
                    continue;
                }
            }

            if (agent.WaypointIndex >= agent.Path.Count)
            {
                agent.HasTarget = false;
                continue;
            }

            var waypoint = agent.Path[agent.WaypointIndex];
            var toWaypoint = waypoint - transform.Position;
            float distance = toWaypoint.Length();

            if (distance <= agent.ArriveThreshold)
            {
                agent.WaypointIndex++;
                if (agent.WaypointIndex >= agent.Path.Count)
                    agent.HasTarget = false;
                continue;
            }

            transform.Position += toWaypoint / distance * agent.Speed * deltaTime;
        }
    }

    /// <summary>Constrói (uma vez por cena) a grade de navegação a partir do primeiro Tilemap
    /// com SolidTiles não vazio. Sem tilemap assim, NavAgent anda reto até o alvo.</summary>
    private NavGrid? EnsureNavGrid()
    {
        if (_navGrid is not null)
            return _navGrid;

        foreach (var (_, transform, tilemap) in Query<Transform, Tilemap>())
        {
            if (tilemap.SolidTiles.Count > 0)
            {
                _navGrid = NavGrid.FromTilemap(transform, tilemap);
                break;
            }
        }
        return _navGrid;
    }

    private static bool IsWithinGrid(NavGrid grid, Vector2 world)
    {
        var cell = grid.WorldToCell(world);
        return cell.X >= 0 && cell.Y >= 0 && cell.X < grid.Width && cell.Y < grid.Height;
    }

    private void UpdateParticles(float deltaTime)
    {
        foreach (var (_, transform, emitter) in Query<Transform, ParticleEmitter>())
        {
            if (emitter.Emitting && emitter.Particles.Count < emitter.MaxParticles)
            {
                emitter.SpawnAccumulator += emitter.Rate * deltaTime;
                while (emitter.SpawnAccumulator >= 1f && emitter.Particles.Count < emitter.MaxParticles)
                {
                    emitter.SpawnAccumulator -= 1f;
                    SpawnParticle(transform, emitter);
                }
            }

            for (int i = emitter.Particles.Count - 1; i >= 0; i--)
            {
                var particle = emitter.Particles[i];
                particle.Age += deltaTime;
                if (particle.Age >= particle.LifeTime)
                {
                    emitter.Particles.RemoveAt(i);
                    continue;
                }

                particle.Velocity += emitter.Gravity * deltaTime;
                particle.Position += particle.Velocity * deltaTime;
                emitter.Particles[i] = particle;
            }
        }
    }

    private void SpawnParticle(Transform transform, ParticleEmitter emitter)
    {
        float angleDeg = Lerp(emitter.AngleMin, emitter.AngleMax, (float)_random.NextDouble());
        float speed = Lerp(emitter.SpeedMin, emitter.SpeedMax, (float)_random.NextDouble());
        float angleRad = angleDeg * (MathF.PI / 180f);

        emitter.Particles.Add(new Particle
        {
            Position = transform.Position,
            Velocity = new Vector2(MathF.Cos(angleRad), MathF.Sin(angleRad)) * speed,
            LifeTime = Lerp(emitter.LifeMin, emitter.LifeMax, (float)_random.NextDouble()),
            Age = 0f,
        });
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private void UpdateHealth(float deltaTime)
    {
        foreach (var (_, health) in Query<Health>())
        {
            if (health.InvulnerabilityTimer > 0f)
                health.InvulnerabilityTimer = MathF.Max(0f, health.InvulnerabilityTimer - deltaTime);
        }
    }

    /// <summary>Aplica dano a uma entidade com Health. Não faz nada se ela não tiver Health,
    /// já estiver morta, ou estiver invencível (flag ou i-frames ativos). Retorna se o dano
    /// foi aplicado de verdade. Ao zerar Current: notifica OnDeath e destrói (se DestroyOnDeath).</summary>
    public bool Damage(Entity target, float amount, Entity? source = null)
    {
        var health = target.Get<Health>();
        if (health is null || health.IsDead || health.Invulnerable || health.InvulnerabilityTimer > 0f || amount <= 0f)
            return false;

        health.Current = MathF.Max(0f, health.Current - amount);
        health.InvulnerabilityTimer = health.InvulnerabilityAfterHit;
        NotifyDamaged(target.Id, amount, source);

        if (health.Current <= 0f)
        {
            NotifyDeath(target.Id);
            if (health.DestroyOnDeath && _alive.Contains(target.Id))
                Destroy(target.Id);
        }

        return true;
    }

    /// <summary>Cura sem passar de Max. Não faz nada se a entidade não tiver Health.</summary>
    public void Heal(Entity target, float amount)
    {
        var health = target.Get<Health>();
        if (health is null || amount <= 0f)
            return;

        health.Current = MathF.Min(health.Max, health.Current + amount);
    }

    private void ProcessCollisions()
    {
        _collisionBuffer.Clear();
        foreach (var entry in Query<Transform, Collider>())
            _collisionBuffer.Add(entry);

        // Swap sets: _activeTriggers becomes prev, _prevTriggers becomes new current
        (_prevTriggers, _activeTriggers) = (_activeTriggers, _prevTriggers);
        _activeTriggers.Clear();

        for (int i = 0; i < _collisionBuffer.Count; i++)
        {
            var (ea, ta, ca) = _collisionBuffer[i];

            for (int j = i + 1; j < _collisionBuffer.Count; j++)
            {
                var (eb, tb, cb) = _collisionBuffer[j];

                if ((ca.Mask & cb.Layer) == 0 && (cb.Mask & ca.Layer) == 0)
                    continue;

                if (!Overlap(ta.Position + ca.Offset, ca, tb.Position + cb.Offset, cb,
                        out var normal, out var depth))
                    continue;

                if (ca.IsSolid && cb.IsSolid)
                {
                    Resolve(ta, ca, tb, cb, normal, depth);
                    NotifyCollision(ea.Id, eb, new CollisionInfo(-normal, depth));
                    NotifyCollision(eb.Id, ea, new CollisionInfo(normal, depth));
                }
                else
                {
                    long key = PairKey(ea.Id, eb.Id);
                    _activeTriggers.Add(key);

                    if (!_prevTriggers.Contains(key))
                    {
                        NotifyTriggerEnter(ea.Id, eb);
                        NotifyTriggerEnter(eb.Id, ea);
                    }
                }
            }
        }

        foreach (long key in _prevTriggers)
        {
            if (_activeTriggers.Contains(key)) continue;

            int idA = (int)(key >> 32);
            int idB = (int)(key & 0xFFFF_FFFF);
            if (_alive.Contains(idA)) NotifyTriggerExit(idA, new Entity(idB, this));
            if (_alive.Contains(idB)) NotifyTriggerExit(idB, new Entity(idA, this));
        }

        ProcessTilemapCollisions();
    }

    private void ProcessTilemapCollisions()
    {
        // Build tilemap list (only maps with solid tiles)
        _tilemapBuffer.Clear();
        foreach (var entry in Query<Transform, Tilemap>())
        {
            if (entry.C2.SolidTiles.Count > 0)
                _tilemapBuffer.Add(entry);
        }

        if (_tilemapBuffer.Count == 0) return;

        for (int i = 0; i < _collisionBuffer.Count; i++)
        {
            var (entity, transform, collider) = _collisionBuffer[i];
            if (collider.IsKinematic) continue;

            Vector2 center = transform.Position + collider.Offset;

            for (int m = 0; m < _tilemapBuffer.Count; m++)
            {
                var (mapEntity, mapTransform, tilemap) = _tilemapBuffer[m];
                CheckTilemapCollision(entity, transform, collider, ref center, mapEntity, mapTransform, tilemap);
            }
        }
    }

    private void CheckTilemapCollision(
        Entity entity, Transform transform, Collider collider, ref Vector2 center,
        Entity mapEntity, Transform mapTransform, Tilemap tilemap)
    {
        float tileW = tilemap.TileWidth * mapTransform.Scale.X;
        float tileH = tilemap.TileHeight * mapTransform.Scale.Y;
        if (tileW <= 0f || tileH <= 0f) return;

        // Collider half-extents for sweep range
        float hw = collider.Shape == ColliderShape.Circle ? collider.Radius : collider.Width * 0.5f;
        float hh = collider.Shape == ColliderShape.Circle ? collider.Radius : collider.Height * 0.5f;

        Vector2 origin = mapTransform.Position;
        int minX = Math.Max(0, (int)MathF.Floor((center.X - hw - origin.X) / tileW));
        int maxX = Math.Min(tilemap.Width - 1, (int)MathF.Floor((center.X + hw - origin.X) / tileW));
        int minY = Math.Max(0, (int)MathF.Floor((center.Y - hh - origin.Y) / tileH));
        int maxY = Math.Min(tilemap.Height - 1, (int)MathF.Floor((center.Y + hh - origin.Y) / tileH));

        _tileCollider.Width = tileW;
        _tileCollider.Height = tileH;

        for (int ty = minY; ty <= maxY; ty++)
        {
            for (int tx = minX; tx <= maxX; tx++)
            {
                int tileIndex = tilemap.Tiles[ty * tilemap.Width + tx];
                if (tileIndex < 0 || !tilemap.SolidTiles.Contains(tileIndex))
                    continue;

                // Tile center in world space
                var tileCenter = origin + new Vector2((tx + 0.5f) * tileW, (ty + 0.5f) * tileH);

                if (!Overlap(center, collider, tileCenter, _tileCollider, out var normal, out var depth))
                    continue;

                // Tile is always kinematic — push entity out
                transform.Position -= normal * depth;
                center -= normal * depth;

                NotifyCollision(entity.Id, mapEntity, new CollisionInfo(-normal, depth));
            }
        }
    }

    private static void Resolve(Transform ta, Collider ca, Transform tb, Collider cb,
        Vector2 normal, float depth)
    {
        if (ca.IsKinematic && !cb.IsKinematic)
            tb.Position += normal * depth;
        else if (!ca.IsKinematic && cb.IsKinematic)
            ta.Position -= normal * depth;
        else if (!ca.IsKinematic)
        {
            ta.Position -= normal * (depth * 0.5f);
            tb.Position += normal * (depth * 0.5f);
        }
    }

    private static bool Overlap(Vector2 posA, Collider ca, Vector2 posB, Collider cb,
        out Vector2 normal, out float depth)
    {
        normal = Vector2.Zero;
        depth = 0f;

        if (ca.Shape == ColliderShape.Box && cb.Shape == ColliderShape.Box)
            return BoxBox(posA, ca, posB, cb, out normal, out depth);

        if (ca.Shape == ColliderShape.Circle && cb.Shape == ColliderShape.Circle)
            return CircleCircle(posA, ca, posB, cb, out normal, out depth);

        if (ca.Shape == ColliderShape.Box && cb.Shape == ColliderShape.Circle)
            return BoxCircle(posA, ca, posB, cb, out normal, out depth);

        // CircleBox: delegate to BoxCircle with args swapped, then flip normal
        if (BoxCircle(posB, cb, posA, ca, out normal, out depth))
        {
            normal = -normal;
            return true;
        }
        return false;
    }

    private static bool BoxBox(Vector2 posA, Collider a, Vector2 posB, Collider b,
        out Vector2 normal, out float depth)
    {
        normal = Vector2.Zero;
        depth = 0f;

        float dx = posB.X - posA.X;
        float dy = posB.Y - posA.Y;
        float ox = a.Width * 0.5f + b.Width * 0.5f - MathF.Abs(dx);
        float oy = a.Height * 0.5f + b.Height * 0.5f - MathF.Abs(dy);

        if (ox <= 0f || oy <= 0f) return false;

        if (ox < oy)
        {
            normal = new Vector2(MathF.Sign(dx), 0f);
            depth = ox;
        }
        else
        {
            normal = new Vector2(0f, MathF.Sign(dy));
            depth = oy;
        }
        return true;
    }

    private static bool CircleCircle(Vector2 posA, Collider a, Vector2 posB, Collider b,
        out Vector2 normal, out float depth)
    {
        normal = Vector2.Zero;
        depth = 0f;

        var diff = posB - posA;
        float distSq = diff.LengthSquared();
        float sumR = a.Radius + b.Radius;

        if (distSq >= sumR * sumR) return false;

        float dist = MathF.Sqrt(distSq);
        normal = dist > 1e-6f ? diff / dist : Vector2.UnitY;
        depth = sumR - dist;
        return true;
    }

    // normal convention: points from box center toward circle center
    private static bool BoxCircle(Vector2 boxPos, Collider box, Vector2 circPos, Collider circ,
        out Vector2 normal, out float depth)
    {
        normal = Vector2.Zero;
        depth = 0f;

        float halfX = box.Width * 0.5f, halfY = box.Height * 0.5f;
        float cx = Math.Clamp(circPos.X, boxPos.X - halfX, boxPos.X + halfX);
        float cy = Math.Clamp(circPos.Y, boxPos.Y - halfY, boxPos.Y + halfY);

        var diff = circPos - new Vector2(cx, cy);
        float distSq = diff.LengthSquared();

        if (distSq >= circ.Radius * circ.Radius) return false;

        if (distSq < 1e-12f)
        {
            // Circle center inside box: push out along shortest axis
            var d = circPos - boxPos;
            float overX = halfX - MathF.Abs(d.X);
            float overY = halfY - MathF.Abs(d.Y);
            if (overX < overY)
            {
                normal = new Vector2(MathF.Sign(d.X), 0f);
                depth = circ.Radius + overX;
            }
            else
            {
                normal = new Vector2(0f, MathF.Sign(d.Y));
                depth = circ.Radius + overY;
            }
        }
        else
        {
            float dist = MathF.Sqrt(distSq);
            normal = diff / dist;
            depth = circ.Radius - dist;
        }
        return true;
    }

    private void NotifyCollision(int entityId, Entity other, CollisionInfo info)
    {
        for (int i = 0; i < _behaviors.Count; i++)
        {
            var b = _behaviors[i];
            if (b.Entity.Id == entityId && b.Enabled)
                b.OnCollision(other, info);
        }
    }

    private void NotifyTriggerEnter(int entityId, Entity other)
    {
        for (int i = 0; i < _behaviors.Count; i++)
        {
            var b = _behaviors[i];
            if (b.Entity.Id == entityId && b.Enabled)
                b.OnTriggerEnter(other);
        }
    }

    private void NotifyTriggerExit(int entityId, Entity other)
    {
        for (int i = 0; i < _behaviors.Count; i++)
        {
            var b = _behaviors[i];
            if (b.Entity.Id == entityId && b.Enabled)
                b.OnTriggerExit(other);
        }
    }

    private void NotifyDamaged(int entityId, float amount, Entity? source)
    {
        for (int i = 0; i < _behaviors.Count; i++)
        {
            var b = _behaviors[i];
            if (b.Entity.Id == entityId && b.Enabled)
                b.OnDamaged(amount, source);
        }
    }

    private void NotifyDeath(int entityId)
    {
        for (int i = 0; i < _behaviors.Count; i++)
        {
            var b = _behaviors[i];
            if (b.Entity.Id == entityId && b.Enabled)
                b.OnDeath();
        }
    }

    private static long PairKey(int idA, int idB)
        => idA < idB ? ((long)idA << 32) | (uint)idB : ((long)idB << 32) | (uint)idA;

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

        DrawParticles(batch);
        DrawLights(batch);
    }

    /// <summary>Desenha as partículas de todo emissor, por camada (não interoperam com o
    /// z-order de sprites individuais — desenhadas depois deles, cada emissor em bloco).</summary>
    private void DrawParticles(SpriteBatch batch)
    {
        var emitters = Query<Transform, ParticleEmitter>()
            .Select(e => e.Item3)
            .Where(e => e.Particles.Count > 0)
            .OrderBy(e => e.Layer);

        foreach (var emitter in emitters)
        {
            var texture = emitter.Texture ?? batch.WhitePixel;
            foreach (var particle in emitter.Particles)
            {
                float t = particle.LifeTime > 0f ? particle.Age / particle.LifeTime : 1f;
                float size = Lerp(emitter.SizeStart, emitter.SizeEnd, t);
                if (size <= 0f)
                    continue;

                var color = new Color(
                    Lerp(emitter.ColorStart.R, emitter.ColorEnd.R, t),
                    Lerp(emitter.ColorStart.G, emitter.ColorEnd.G, t),
                    Lerp(emitter.ColorStart.B, emitter.ColorEnd.B, t),
                    Lerp(emitter.ColorStart.A, emitter.ColorEnd.A, t));

                batch.Draw(texture, particle.Position, new Vector2(size, size),
                    new Vector2(0.5f, 0.5f), 0f, color);
            }
        }
    }

    private void DrawLights(SpriteBatch batch)
    {
        foreach (var (_, transform, light) in Query<Transform, Light2D>())
        {
            if (light.Enabled)
                batch.DrawGlow(transform.Position, light.Radius, light.Color.WithAlpha(light.Intensity));
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
