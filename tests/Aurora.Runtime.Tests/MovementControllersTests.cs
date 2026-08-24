using System.Numerics;
using Aurora.Runtime.Assets;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.UI;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Os três tipos de movimento: top-down (livre / 8 / 4 direções), plataforma e veículo. O que se
/// prende aqui é a diferença que faz cada um ser aquele gênero — a diagonal que some nas 4
/// direções, o coyote time que salva o pulo na beirada, a inércia que separa nave de carro.
/// </summary>
public class MovementControllersTests : IDisposable
{
    private const float Tolerance = 0.01f;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "aurora-mov-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>Mundo com um joystick de UI — é o caminho de input testável sem janela.</summary>
    private (World World, UiJoystick Stick) BuildWithStick()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "Hud.json"), """
            {
              "Scene": "Hud", "UI": true,
              "Objects": [ { "Name": "Stick", "Components": [ { "Type": "UiJoystick" } ] } ]
            }
            """);

        var ui = new UIManager();
        ui.Load("Hud.json", new AssetManager(null!, new FileAssetSource(_root)));

        return (new World { UI = ui }, ui.Find<UiJoystick>("Hud", "Stick")!);
    }

    private static TopDownController TopDown(string movement) => new()
    {
        Movement = movement,
        Speed = 100f,
        UseKeyboard = false,
        JoystickScreen = "Hud",
        JoystickName = "Stick",
    };

    // ---------- Top-down: livre / 8 / 4 direções ----------

    [Fact]
    public void LivreAndaEmQualquerAngulo()
    {
        var (world, stick) = BuildWithStick();
        stick.Value = Vector2.Normalize(new Vector2(1f, 0.35f));

        var player = world.CreateEntity("Player");
        player.Add(new Transform());
        player.Add(TopDown("Free"));

        for (int i = 0; i < 100; i++)
            world.Update(0.01f);

        // Mantém o ângulo do empurrão em vez de travar numa das oito.
        var position = player.Get<Transform>()!.Position;
        Assert.True(position.Y > 5f && position.Y < 45f, $"Ângulo travou: y={position.Y}");
    }

    [Fact]
    public void OitoDirecoesTravaNoMultiploDe45()
    {
        var (world, stick) = BuildWithStick();
        stick.Value = Vector2.Normalize(new Vector2(1f, 0.35f));   // ~19°, entre 0 e 45

        var player = world.CreateEntity("Player");
        player.Add(new Transform());
        player.Add(TopDown("EightWay"));

        for (int i = 0; i < 100; i++)
            world.Update(0.01f);

        // Arredonda pra 0° — anda reto pra direita, sem componente vertical.
        var position = player.Get<Transform>()!.Position;
        Assert.Equal(100f, position.X, 1f);
        Assert.Equal(0f, position.Y, 1f);
    }

    [Fact]
    public void QuatroDirecoesNaoTemDiagonal()
    {
        // O andar de RPG de grade: com os dois eixos empurrados, o maior vence e o outro zera.
        var (world, stick) = BuildWithStick();
        stick.Value = Vector2.Normalize(new Vector2(1f, 0.9f));

        var player = world.CreateEntity("Player");
        player.Add(new Transform());
        player.Add(TopDown("FourWay"));

        for (int i = 0; i < 100; i++)
            world.Update(0.01f);

        Assert.Equal(0f, player.Get<Transform>()!.Position.Y, 1f);
        Assert.True(player.Get<Transform>()!.Position.X > 50f);
    }

    [Fact]
    public void EmpurraoParcialContinuaValendoNasQuatroDirecoes()
    {
        // Travar a direção não pode travar a intensidade: no analógico, meio empurrão é meio passo.
        var (world, stick) = BuildWithStick();
        stick.Value = new Vector2(0.5f, 0f);

        var player = world.CreateEntity("Player");
        player.Add(new Transform());
        player.Add(TopDown("FourWay"));

        for (int i = 0; i < 100; i++)
            world.Update(0.01f);

        Assert.Equal(50f, player.Get<Transform>()!.Position.X, 1f);
    }

    // ---------- Plataforma ----------

    /// <summary>Personagem sobre um chão sólido largo.</summary>
    private static (World World, Entity Player, PlatformerController Controller) BuildPlatformer(
        PlatformerController controller)
    {
        var world = new World();

        var ground = world.CreateEntity("Chao");
        ground.Add(new Transform(new Vector2(0f, 40f)));
        ground.Add(new Collider { Width = 4000f, Height = 40f, IsKinematic = true });

        var player = world.CreateEntity("Player");
        player.Add(new Transform(new Vector2(0f, 0f)));
        player.Add(new Collider { Width = 16f, Height = 24f });
        player.Add(controller);

        return (world, player, controller);
    }

    [Fact]
    public void CaiEParaNoChao()
    {
        var (world, player, controller) = BuildPlatformer(new PlatformerController { UseKeyboard = false });

        for (int i = 0; i < 120; i++)
            world.Update(1f / 60f);

        Assert.True(controller.IsGrounded, "Não encostou no chão.");
        Assert.Equal(0f, controller.Velocity.Y, 1f);
    }

    [Fact]
    public void NaoAtravessaOChaoNaQuedaLonga()
    {
        // MaxFallSpeed existe pra isso: sem teto, a queda pula o chão entre dois frames.
        var (world, player, _) = BuildPlatformer(new PlatformerController { UseKeyboard = false });
        player.Get<Transform>()!.Position = new Vector2(0f, -3000f);

        for (int i = 0; i < 600; i++)
            world.Update(1f / 60f);

        Assert.True(player.Get<Transform>()!.Position.Y < 60f,
            $"Atravessou o chão: y={player.Get<Transform>()!.Position.Y}");
    }

    [Fact]
    public void PuloSobeEVolta()
    {
        var (world, player, controller) = BuildPlatformer(new PlatformerController { UseKeyboard = false });

        for (int i = 0; i < 120; i++)
            world.Update(1f / 60f);       // aterrissa

        float chao = player.Get<Transform>()!.Position.Y;
        controller.RequestJump();

        float maisAlto = chao;
        for (int i = 0; i < 60; i++)
        {
            world.Update(1f / 60f);
            maisAlto = MathF.Min(maisAlto, player.Get<Transform>()!.Position.Y);
        }

        Assert.True(maisAlto < chao - 30f, $"Mal saiu do chão: subiu {chao - maisAlto}px");

        for (int i = 0; i < 180; i++)
            world.Update(1f / 60f);

        Assert.True(controller.IsGrounded, "Não voltou pro chão.");
    }

    [Fact]
    public void CoyoteTimeDeixaPularLogoDepoisDeSairDaBeirada()
    {
        // O ajuste que mais muda a sensação: sem ele o jogador jura que apertou e o jogo ignora.
        var (world, _, controller) = BuildPlatformer(new PlatformerController
        {
            UseKeyboard = false,
            CoyoteTime = 0.15f,
        });

        for (int i = 0; i < 120; i++)
            world.Update(1f / 60f);

        Assert.True(controller.CanJump);

        // Some com o chão: agora está no ar, mas dentro da janela.
        world.Entities.First(e => e.Name == "Chao").Destroy();
        world.Update(1f / 60f);
        world.Update(1f / 60f);

        Assert.False(controller.IsGrounded);
        Assert.True(controller.CanJump, "Perdeu o pulo assim que saiu da beirada.");
    }

    [Fact]
    public void PuloBufferizadoSaiAoAterrissar()
    {
        var (world, player, controller) = BuildPlatformer(new PlatformerController
        {
            UseKeyboard = false,
            JumpBufferTime = 0.5f,
        });

        player.Get<Transform>()!.Position = new Vector2(0f, -60f);
        world.Update(1f / 60f);

        controller.RequestJump();          // apertado no ar, antes de tocar o chão

        bool subiu = false;
        for (int i = 0; i < 120; i++)
        {
            world.Update(1f / 60f);
            if (controller.Velocity.Y < -100f)
                subiu = true;
        }

        Assert.True(subiu, "O pulo apertado antes de aterrissar foi perdido.");
    }

    // ---------- Veículo ----------


    /// <summary>Veículo dirigido pelo joystick de UI — Y pra cima acelera, X vira.</summary>
    private (World World, Entity Vehicle, VehicleController Controller, UiJoystick Stick) BuildVehicle(
        VehicleController controller)
    {
        var (world, stick) = BuildWithStick();

        controller.UseKeyboard = false;
        controller.JoystickScreen = "Hud";
        controller.JoystickName = "Stick";

        var vehicle = world.CreateEntity("Veiculo");
        vehicle.Add(new Transform());
        vehicle.Add(controller);

        return (world, vehicle, controller, stick);
    }

    [Fact]
    public void AceleraNaDirecaoDoBico()
    {
        var (world, vehicle, _, stick) = BuildVehicle(new VehicleController { MaxSpeed = 200f });
        vehicle.Get<Transform>()!.Rotation = MathF.PI / 2f;   // bico pra baixo
        stick.Value = new Vector2(0f, -1f);                   // acelerador a fundo

        for (int i = 0; i < 60; i++)
            world.Update(1f / 60f);

        var position = vehicle.Get<Transform>()!.Position;
        Assert.True(position.Y > 50f, $"Não andou pra onde aponta: {position}");
        Assert.Equal(0f, position.X, 1f);
    }

    /// <summary>Acelera apontado pra direita, depois vira o bico pra baixo e solta o acelerador —
    /// o que sobra é só a inércia, que é onde carro e nave se separam.</summary>
    private VehicleController RunDriftCase(string mode)
    {
        var (world, vehicle, controller, stick) = BuildVehicle(new VehicleController
        {
            Mode = mode,
            MaxSpeed = 200f,
            Grip = 0.9f,
            TurnRequiresMovement = false,
        });

        stick.Value = new Vector2(0f, -1f);

        for (int i = 0; i < 60; i++)
            world.Update(1f / 60f);

        vehicle.Get<Transform>()!.Rotation = MathF.PI / 2f;
        stick.Value = Vector2.Zero;

        for (int i = 0; i < 20; i++)
            world.Update(1f / 60f);

        return controller;
    }

    [Fact]
    public void NaveDerrapaEOCarroCorrigeORumo()
    {
        // A única diferença real entre os dois modos — e é ela que define o gênero.
        var nave = RunDriftCase("Ship");
        var carro = RunDriftCase("Car");

        Assert.True(MathF.Abs(nave.Velocity.X) > MathF.Abs(nave.Velocity.Y),
            $"A nave deveria continuar deslizando pro rumo antigo: {nave.Velocity}");

        Assert.True(MathF.Abs(carro.Velocity.Y) > MathF.Abs(carro.Velocity.X),
            $"O carro deveria ter alinhado a velocidade ao bico novo: {carro.Velocity}");
    }

    [Fact]
    public void ReRespeitaOTetoMenorQueAFrente()
    {
        var (world, _, controller, stick) = BuildVehicle(new VehicleController
        {
            MaxSpeed = 300f,
            ReverseSpeed = 80f,
            Drag = 0f,
            TurnRequiresMovement = false,
        });

        stick.Value = new Vector2(0f, 1f);   // puxa pra trás = ré

        for (int i = 0; i < 180; i++)
            world.Update(1f / 60f);

        Assert.True(controller.ForwardSpeed < 0f, "Não engatou a ré.");
        Assert.True(MathF.Abs(controller.ForwardSpeed) <= 81f,
            $"Ré passou do teto: {controller.ForwardSpeed}");
    }

    [Fact]
    public void CarroParadoNaoPivotaNoLugar()
    {
        // TurnRequiresMovement é o que separa carro de boneco girando: sem velocidade, o volante
        // não faz nada.
        var (world, vehicle, _, stick) = BuildVehicle(new VehicleController
        {
            TurnRequiresMovement = true,
        });

        stick.Value = new Vector2(1f, 0f);   // só volante, sem acelerador

        for (int i = 0; i < 120; i++)
            world.Update(1f / 60f);

        Assert.Equal(0f, vehicle.Get<Transform>()!.Rotation, Tolerance);
    }

    [Fact]
    public void NaveGiraParadaPorqueNaoTemPneu()
    {
        var (world, vehicle, _, stick) = BuildVehicle(new VehicleController
        {
            Mode = "Ship",
            TurnRequiresMovement = false,
        });

        stick.Value = new Vector2(1f, 0f);

        for (int i = 0; i < 60; i++)
            world.Update(1f / 60f);

        Assert.True(MathF.Abs(vehicle.Get<Transform>()!.Rotation) > 0.5f,
            "A nave tem que poder girar parada.");
    }

    [Fact]
    public void OBicoApontaProLadoDoHeading()
    {
        var (world, vehicle, controller, _) = BuildVehicle(new VehicleController());
        vehicle.Get<Transform>()!.Rotation = 0f;
        world.Update(1f / 60f);

        Assert.Equal(1f, controller.Heading.X, Tolerance);
        Assert.Equal(0f, controller.Heading.Y, Tolerance);
    }
}
