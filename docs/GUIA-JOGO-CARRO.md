# Guia — Jogo de Carro (top-down)

Continuação de `docs/GUIA-JOGO-BASE.md` e `docs/REFERENCIA-SCRIPTS-RPG.md`, focado no que
muda pra um jogo de corrida/carro visto de cima: física de aceleração, câmera seguindo,
pista com colisão, checkpoints/volta e velocímetro no HUD.

A engine não tem motor de física rígido (sem impulso/torque real) — o script abaixo é
"arcade": aceleração + atrito + giro escalado pela velocidade, que é como praticamente todo
jogo 2D de carro top-down funciona (Micro Machines, GTA 1/2, etc.).

---

## 1. `CarController.cs`

```csharp
using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Input;
using Aurora.Runtime.Scenes;
using Silk.NET.Input;

namespace MeuJogo;

[SceneScript]
public sealed class CarController : Behavior
{
    public float Acceleration = 300f;
    public float BrakeForce = 500f;
    public float MaxSpeed = 400f;
    public float ReverseMaxSpeed = 150f;
    public float Drag = 150f;       // desaceleração quando solta W/S (atrito/ar)
    public float TurnSpeed = 3f;    // radianos/seg no MaxSpeed — escala com a velocidade atual

    public InputManager? Input; // injetado pelo Game (ver MeuJogoGame.OnUpdate)

    private float _speed; // escalar ao longo de Transform.Rotation — negativo = ré

    public float CurrentSpeed => _speed;

    public override void Update(float dt)
    {
        if (Input is null) return;

        var transform = Get<Transform>();
        if (transform is null) return;

        bool throttle = Input.IsKeyDown(Key.W) || Input.WasGamepadButtonPressed(ButtonName.A);
        bool brake = Input.IsKeyDown(Key.S);
        float steer = Input.AxisX;

        if (throttle)
            _speed += Acceleration * dt;
        else if (brake)
            _speed -= BrakeForce * dt;
        else
        {
            float drag = Drag * dt;
            _speed = _speed > 0f ? MathF.Max(0f, _speed - drag) : MathF.Min(0f, _speed + drag);
        }

        _speed = Math.Clamp(_speed, -ReverseMaxSpeed, MaxSpeed);

        // Só vira em movimento — parado o carro não gira no lugar. Ré inverte o volante.
        if (MathF.Abs(_speed) > 1f)
        {
            float turnFactor = _speed / MaxSpeed;
            transform.Rotation += steer * TurnSpeed * turnFactor * dt;
        }

        var forward = new Vector2(MathF.Cos(transform.Rotation), MathF.Sin(transform.Rotation));
        transform.Position += forward * _speed * dt;
    }
}
```

`W` acelera, `S` freia/dá ré, `A`/`D` viram. `Rotation` em radianos, `0` = sprite virado pra
**direita** (`forward = (cos, sin)`) — se seu sprite do carro olha pra cima por padrão, gira a
arte 90° ou soma `MathF.PI / 2` no `Rotation` inicial da entidade.

Injeção no `Game` (mesmo padrão do `PlayerController`, mas pra entidade `"Car"`):

```csharp
protected override void OnUpdate(float dt)
{
    if (World.TryFind("Car", out var car))
    {
        var cc = car.Get<CarController>();
        if (cc is not null && cc.Input is null)
            cc.Input = Input;
    }
}
```

---

## 2. Montando a entidade no Inspector

1. **"+ Nova"**, renomeia pra `Car`.
2. **Transform** — posição inicial na pista.
3. **SpriteRenderer** — textura do carro.
4. **Collider** — `Shape: Box`, tamanho batendo com o sprite, `IsSolid` marcado,
   `IsKinematic` **desmarcado** (precisa ser empurrado pra fora de parede — se marcar
   Kinematic o carro atravessa tudo).
5. **CarController** — ajusta `Acceleration`/`MaxSpeed`/`Drag`/`TurnSpeed` no Inspector por
   tentativa (são campos scriptáveis, dá pra tunar sem recompilar).

```
Transform
  X  100        Y  200

SpriteRenderer
  Texture   sprites/car.png

Collider
  Shape        [ Box ▾ ]
  Width  24     Height  16
  ☑ IsSolid    ☐ IsKinematic

CarController
  Acceleration      300
  BrakeForce        500
  MaxSpeed          400
  ReverseMaxSpeed   150
  Drag              150
  TurnSpeed         3
  ☑ Enabled
```

---

## 3. Pista com colisão

Duas formas, mistura à vontade:

- **Tilemap com `SolidTiles`** — pista desenhada em tiles, marca os índices de
  grama/muro/água como sólidos. Zero `Collider` extra por tile.
- **Muro avulso** (curva de pista, obstáculo solto) — entidade com `Transform` + `Collider`
  (`IsSolid: true`, `IsKinematic: true`).

Carro (`IsKinematic: false`) colidindo com parede (`IsKinematic: true`) é empurrado pra fora
automaticamente pelo `World` — não precisa tratar `OnCollision` na mão só pra "não atravessar
parede". Use `OnCollision` só se quiser reação extra (ex.: reduzir `_speed` na batida — não
exposto publicamente hoje, dá pra adicionar um método `Brake()`/campo interno se quiser esse
efeito).

---

## 4. Câmera seguindo o carro

Entidade de câmera (ou a própria `Car`, se preferir) com `CameraController`:

```
Camera
  Follow        Car
  FollowSpeed   6
  Zoom          1.5
```

`FollowSpeed` mais alto = câmera gruda mais rápido no carro; baixo = mais suave/atrasada
(bom pra sensação de velocidade).

---

## 5. Checkpoints e contador de volta

**Não use `EventTrigger` com `Trigger: PlayerTouch` aqui** — ele reavalia distância todo
frame; com `Once: false` dispararia a ação a cada frame enquanto o carro estiver dentro do
raio (voltas contadas aos montes). O jeito certo pra "entrou na zona" (dispara 1x por
passagem) é `Collider` trigger + `OnTriggerEnter` num script, que só dispara na borda de
entrada:

```csharp
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace MeuJogo;

[SceneScript]
public sealed class CheckpointGate : Behavior
{
    public string RequiredEntityName = "Car";

    public GameState? State; // injetado pelo Game

    public override void OnTriggerEnter(Entity other)
    {
        if (other.Name != RequiredEntityName) return;
        State?.AddVariable("Lap", 1);
    }
}
```

Checkpoint no Inspector:

```
Transform
  X  400        Y  200

Collider
  Shape       [ Box ▾ ]
  ☐ IsSolid                 (trigger, não bloqueia o carro)

CheckpointGate
  RequiredEntityName   Car
```

Injeção no `Game.OnUpdate` (mesmo padrão de sempre — busca todos, injeta quem falta):

```csharp
foreach (var (_, gate) in World.Query<CheckpointGate>())
    gate.State ??= State;
```

---

## 6. HUD — velocímetro e voltas

`CarController.CurrentSpeed` (só-leitura, adicionado no script acima) precisa ser
sincronizado com `GameState` pra aparecer no HUD via token — nenhum componente nativo lê
campo de script direto:

```csharp
protected override void OnUpdate(float dt)
{
    if (World.TryFind("Car", out var car))
    {
        var cc = car.Get<CarController>();
        if (cc is not null)
        {
            if (cc.Input is null) cc.Input = Input;
            State.SetVariable("Speed", MathF.Abs(cc.CurrentSpeed));
        }
    }
}
```

Tela de UI:

```
UiText   Text: "Velocidade: {Var:Speed}"    (canto superior)
UiText   Text: "Volta: {Var:Lap}"           (canto superior, abaixo)
```

---

## 7. Poeira/fumaça do pneu (efeito visual, seção 4/10 da referência de scripts)

`ParticleEmitter` na própria entidade `Car`, ligado/desligado por código conforme freia ou
curva fechada — não é campo scriptável (`Emitting` é `bool`, mas o toggle depende de lógica
de jogo, não de valor fixo no Inspector), então mexe direto no `Update` do `CarController`
(ou num script à parte que só olha `CurrentSpeed`):

```csharp
var dust = Get<ParticleEmitter>();
if (dust is not null)
    dust.Emitting = brake && MathF.Abs(_speed) > 50f;
```

Adiciona um `ParticleEmitter` na entidade `Car` (cor terrosa, `Rate` baixo, `SpeedMin`/`Max`
baixos, `LifeMin`/`Max` curtos) — funciona igual ao exemplo de `HitEffect` do guia base,
só que ligado nos pneus em vez de instanciado por impacto.

---

## 8. Checklist

- [ ] `CarController.cs` compilando, `Car` na cena com `Transform`+`SpriteRenderer`+`Collider`(não-kinemático)+`CarController`
- [ ] `MeuJogoGame.OnUpdate` injetando `Input` na `Car`
- [ ] Pista com `Tilemap.SolidTiles` e/ou paredes `Collider{IsSolid:true, IsKinematic:true}`
- [ ] `Camera.Follow = "Car"`
- [ ] Checkpoints com `Collider{IsSolid:false}` + `CheckpointGate` (não `EventTrigger PlayerTouch`)
- [ ] `GameState` sincronizado (`Speed`, `Lap`) todo frame pro HUD
- [ ] (Opcional) `ParticleEmitter` de poeira ligado/desligado por código
