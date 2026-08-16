namespace Aurora.Editor.Models;

/// <summary>
/// Templates prontos para o botão "+ Novo…" do painel SCRIPTS — cobrem os scripts mais comuns
/// de um jogo (movimento, arma, inimigo, item, magia) pra quem está começando não precisar ir
/// copiar da documentação (<c>docs/REFERENCIA-SCRIPTS-RPG.md</c>) toda vez. Usa placeholders
/// __NAMESPACE__/__CLASSNAME__ em vez de interpolação porque o corpo dos templates tem chaves
/// de código C# de verdade (conflitaria com <c>$"""..."""</c>).
/// </summary>
public static class ScriptTemplates
{
    public sealed record Template(string Id, string DisplayName, string DefaultClassName, string Source)
    {
        /// <summary>ComboBox do painel SCRIPTS não usa ItemTemplate — renderiza via ToString().</summary>
        public override string ToString() => DisplayName;
    }

    public static readonly IReadOnlyList<Template> All =
    [
        new("Movement", "Movimento (Character Controller)", "CharacterController", MovementSource),
        new("Weapon", "Arma (ataque corpo-a-corpo)", "MeleeWeapon", WeaponSource),
        new("Enemy", "Inimigo (perseguidor)", "EnemyAI", EnemySource),
        new("Item", "Item / Coletável", "Item", ItemSource),
        new("Magic", "Magia / Ataque à distância", "RangedAttack", MagicSource),
        new("Empty", "Vazio", "MeuScript", EmptySource),
    ];

    /// <summary>Gera o arquivo final substituindo os placeholders — <paramref name="className"/>
    /// vem do nome do arquivo escolhido pelo usuário (sanitizado), não do DefaultClassName do
    /// template, pra classe sempre bater com o nome do arquivo.</summary>
    public static string Build(Template template, string @namespace, string className)
        => template.Source.Replace("__NAMESPACE__", @namespace).Replace("__CLASSNAME__", className);

    private const string EmptySource = """
        using Aurora.Runtime.Ecs;
        using Aurora.Runtime.Ecs.Components;
        using Aurora.Runtime.Scenes;

        namespace __NAMESPACE__;

        [SceneScript]
        public sealed class __CLASSNAME__ : Behavior
        {
            public override void Start()
            {
            }

            public override void Update(float deltaTime)
            {
            }
        }
        """;

    private const string MovementSource = """
        using System.Numerics;
        using Aurora.Runtime.Ecs;
        using Aurora.Runtime.Ecs.Components;
        using Aurora.Runtime.Scenes;

        namespace __NAMESPACE__;

        // Movimento em 8 direções (WASD/setas ou analógico esquerdo — a InputManager já combina
        // os dois em AxisX/AxisY). Precisa de Transform na entidade; SpriteRenderer e Animator são
        // opcionais (flip horizontal e parâmetro "Speed" só são setados se existirem).
        [SceneScript]
        public sealed class __CLASSNAME__ : Behavior
        {
            public float Speed = 200f;

            public override void Update(float deltaTime)
            {
                var input = World?.Input;
                var transform = Get<Transform>();
                if (input is null || transform is null)
                    return;

                var move = new Vector2(input.AxisX, input.AxisY);
                if (move.LengthSquared() > 0f)
                {
                    move = Vector2.Normalize(move);
                    transform.Position += move * Speed * deltaTime;

                    var sprite = Get<SpriteRenderer>();
                    if (sprite is not null && move.X != 0f)
                        sprite.FlipX = move.X < 0f;
                }

                Get<Animator>()?.SetFloat("Speed", move.Length() * Speed);
            }
        }
        """;

    private const string WeaponSource = """
        using System.Numerics;
        using Aurora.Runtime.Ecs;
        using Aurora.Runtime.Ecs.Components;
        using Aurora.Runtime.Scenes;
        using Silk.NET.Input;

        namespace __NAMESPACE__;

        // Arma corpo-a-corpo: aperta AttackKey (nome de Silk.NET.Input.Key, ex.: "Space", "E", "J")
        // e causa Damage em qualquer entidade com Health dentro de Range à frente (usa o FlipX do
        // SpriteRenderer pra saber o lado). Coloque este script na mesma entidade do personagem
        // que ataca.
        [SceneScript]
        public sealed class __CLASSNAME__ : Behavior
        {
            public float Damage = 15f;
            public float Range = 28f;
            public float Cooldown = 0.4f;
            public string AttackKey = "Space";

            private float _cooldownTimer;

            public override void Update(float deltaTime)
            {
                _cooldownTimer -= deltaTime;

                var input = World?.Input;
                var transform = Get<Transform>();
                if (World is null || input is null || transform is null)
                    return;
                if (_cooldownTimer > 0f)
                    return;
                if (!Enum.TryParse<Key>(AttackKey, ignoreCase: true, out var key) || !input.WasKeyPressed(key))
                    return;

                _cooldownTimer = Cooldown;

                bool facingLeft = Get<SpriteRenderer>()?.FlipX ?? false;
                var attackPoint = transform.Position + new Vector2(facingLeft ? -Range : Range, 0f);

                foreach (var (target, _) in World.Query<Health>())
                {
                    if (target.Id == Entity.Id)
                        continue;

                    var targetTransform = target.Get<Transform>();
                    if (targetTransform is not null && Vector2.Distance(targetTransform.Position, attackPoint) <= Range)
                        World.Damage(target, Damage, Entity);
                }
            }
        }
        """;

    private const string EnemySource = """
        using System.Numerics;
        using Aurora.Runtime.Ecs;
        using Aurora.Runtime.Ecs.Components;
        using Aurora.Runtime.Scenes;

        namespace __NAMESPACE__;

        // Inimigo que persegue TargetName (por padrão "Player") enquanto estiver dentro de
        // SightRange, e causa ContactDamage ao encostar. Precisa de NavAgent + Transform +
        // Collider (IsSolid: true) na entidade — o desvio de paredes/tiles sólidos é automático
        // (ver componente NavAgent na documentação).
        [SceneScript]
        public sealed class __CLASSNAME__ : Behavior
        {
            public float SightRange = 150f;
            public float ContactDamage = 10f;
            public string TargetName = "Player";

            public override void Update(float deltaTime)
            {
                if (World is null || !World.TryFind(TargetName, out var target))
                    return;

                var nav = Get<NavAgent>();
                var transform = Get<Transform>();
                if (nav is null || transform is null)
                    return;

                var targetPos = target.Get<Transform>()?.Position ?? transform.Position;
                if (Vector2.Distance(transform.Position, targetPos) <= SightRange)
                    nav.SetTarget(targetPos);
                else
                    nav.Stop();
            }

            public override void OnCollision(Entity other, CollisionInfo info)
            {
                if (other.Has<Health>())
                    World?.Damage(other, ContactDamage, Entity);
            }
        }
        """;

    private const string ItemSource = """
        using Aurora.Runtime.Ecs;
        using Aurora.Runtime.Ecs.Components;
        using Aurora.Runtime.Scenes;

        namespace __NAMESPACE__;

        // Item coletável por código — alternativa ao EventTrigger (ver painel de eventos) pra
        // quando precisar de lógica extra, tipo curar ao pegar. Precisa de Collider com
        // IsSolid: false (trigger) na entidade — quem tiver Health (o jogador) e encostar recebe
        // o item.
        [SceneScript]
        public sealed class __CLASSNAME__ : Behavior
        {
            public string ItemName = "Pocao";
            public int Amount = 1;
            public float HealAmount = 0f;
            public bool DestroyOnPickup = true;

            public override void OnTriggerEnter(Entity other)
            {
                if (!other.Has<Health>())
                    return;

                World?.Inventory?.Add(ItemName, Amount);
                if (HealAmount > 0f)
                    World?.Heal(other, HealAmount);

                if (DestroyOnPickup)
                    Entity.Destroy();
            }
        }
        """;

    private const string MagicSource = """
        using System.Numerics;
        using Aurora.Runtime.Ecs;
        using Aurora.Runtime.Ecs.Components;
        using Aurora.Runtime.Scenes;
        using Silk.NET.Input;

        namespace __NAMESPACE__;

        // Ataque à distância: aperta CastKey (nome de Silk.NET.Input.Key) e spawna um Projectile
        // (componente pronto da engine, já causa dano e se autodestrói sozinho ao tocar ou expirar
        // — ver Aurora.Runtime/Ecs/Components/Projectile.cs). O projétil nasce reusando o
        // SpriteRenderer.Texture de quem lançou como visual padrão — troque por uma textura
        // própria de magia se quiser algo diferente.
        [SceneScript]
        public sealed class __CLASSNAME__ : Behavior
        {
            public float Damage = 20f;
            public float ProjectileSpeed = 320f;
            public float Cooldown = 0.6f;
            public string CastKey = "Space";

            private float _cooldownTimer;

            public override void Update(float deltaTime)
            {
                _cooldownTimer -= deltaTime;

                var input = World?.Input;
                var transform = Get<Transform>();
                if (World is null || input is null || transform is null)
                    return;
                if (_cooldownTimer > 0f)
                    return;
                if (!Enum.TryParse<Key>(CastKey, ignoreCase: true, out var key) || !input.WasKeyPressed(key))
                    return;

                _cooldownTimer = Cooldown;

                bool facingLeft = Get<SpriteRenderer>()?.FlipX ?? false;
                var direction = new Vector2(facingLeft ? -1f : 1f, 0f);

                var projectile = World.CreateEntity("Projectile");
                projectile.Add(new Transform { Position = transform.Position });

                var casterTexture = Get<SpriteRenderer>()?.Texture;
                if (casterTexture is not null)
                    projectile.Add(new SpriteRenderer { Texture = casterTexture, Size = new Vector2(8f, 8f) });

                projectile.Add(new Collider { Width = 8f, Height = 8f, IsSolid = false });
                projectile.Add(new Projectile { Velocity = direction * ProjectileSpeed, Damage = Damage, Source = Entity });
            }
        }
        """;
}
