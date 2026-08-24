using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Montaria e veículo: entrar, sair e a transferência de controle. O que se prende aqui é o que
/// separa "existe um carro na cena" de "o jogador está dirigindo" — e as saídas sujas, que são
/// onde esse tipo de mecânica costuma deixar o jogador travado ou invisível.
/// </summary>
public class RideableTests
{
    private const float Tolerance = 0.01f;

    /// <summary>Jogador e uma montaria perto dele, cada um com o seu controlador.</summary>
    private static (World World, Entity Rider, Entity Mount, Rideable Ride) Build(
        Rideable rideable, float distance = 10f)
    {
        var world = new World();

        var rider = world.CreateEntity("Player");
        rider.Add(new Transform());
        rider.Add(new SpriteRenderer());
        rider.Add(new Collider { Width = 16f, Height = 16f });
        rider.Add(new TopDownController { UseKeyboard = false });

        var mount = world.CreateEntity("Cavalo");
        mount.Add(new Transform(new Vector2(distance, 0f)));
        mount.Add(new TopDownController { UseKeyboard = false });
        mount.Add(rideable);

        world.Update(1f / 60f);
        return (world, rider, mount, rideable);
    }

    [Fact]
    public void MontariaNasceSemControleProprio()
    {
        // Sem isto o cavalo sai andando junto com o jogador, lendo o mesmo input — é o bug que
        // este componente existe pra impedir.
        var (_, _, mount, _) = Build(new Rideable());

        Assert.False(mount.Get<TopDownController>()!.Enabled);
    }

    [Fact]
    public void MontarTransfereOControle()
    {
        var (_, rider, mount, ride) = Build(new Rideable());

        Assert.True(ride.TryMount());

        Assert.False(rider.Get<TopDownController>()!.Enabled, "O jogador continuou se movendo sozinho.");
        Assert.True(mount.Get<TopDownController>()!.Enabled, "A montaria não ganhou o controle.");
        Assert.True(ride.IsRidden);
    }

    [Fact]
    public void DesmontarDevolveOControle()
    {
        var (_, rider, mount, ride) = Build(new Rideable());
        ride.TryMount();

        ride.Dismount();

        Assert.True(rider.Get<TopDownController>()!.Enabled);
        Assert.False(mount.Get<TopDownController>()!.Enabled);
        Assert.False(ride.IsRidden);
    }

    [Fact]
    public void OPassageiroAndaColadoNoAssento()
    {
        // É o que faz a câmera seguindo "Player" continuar funcionando montado, sem mexer nela.
        var (world, rider, mount, ride) = Build(new Rideable { SeatOffsetX = 0f, SeatOffsetY = -12f });
        ride.TryMount();

        mount.Get<Transform>()!.Position = new Vector2(500f, 300f);
        world.Update(1f / 60f);

        var position = rider.Get<Transform>()!.Position;
        Assert.Equal(500f, position.X, Tolerance);
        Assert.Equal(288f, position.Y, Tolerance);
    }

    [Fact]
    public void LongeDemaisNaoMonta()
    {
        var (_, _, _, ride) = Build(new Rideable { Range = 30f }, distance: 200f);

        Assert.False(ride.TryMount());
        Assert.False(ride.IsRidden);
    }

    [Fact]
    public void OCarroEscondeOMotoristaEOCavaloNao()
    {
        var (_, riderCarro, _, carro) = Build(new Rideable { HideRiderWhileRiding = true });
        carro.TryMount();
        Assert.False(riderCarro.Get<SpriteRenderer>()!.Visible);

        var (_, riderCavalo, _, cavalo) = Build(new Rideable { HideRiderWhileRiding = false });
        cavalo.TryMount();
        Assert.True(riderCavalo.Get<SpriteRenderer>()!.Visible);
    }

    [Fact]
    public void SairDevolveOSpriteEOColisorComoEstavam()
    {
        // Montar não pode deixar sequela: jogador invisível ou atravessando parede depois de
        // descer é o tipo de bug que só aparece três fases depois.
        var (_, rider, _, ride) = Build(new Rideable { HideRiderWhileRiding = true });

        ride.TryMount();
        ride.Dismount();

        Assert.True(rider.Get<SpriteRenderer>()!.Visible);
        Assert.True(rider.Get<Collider>()!.IsSolid);
        Assert.False(rider.Get<Collider>()!.IsKinematic);
    }

    [Fact]
    public void AoSairOJogadorELargadoAoLadoENaoEmCima()
    {
        // Largar em cima prende os dois num empurra-empurra de colisão.
        var (world, rider, mount, ride) = Build(new Rideable { ExitOffsetX = 30f, ExitOffsetY = 0f });
        ride.TryMount();

        // Move a montaria DEPOIS de montar: o jogador vai junto, então a saída é relativa a onde
        // ela parou, não a onde a cena começou.
        mount.Get<Transform>()!.Position = new Vector2(100f, 0f);
        world.Update(1f / 60f);

        ride.Dismount();

        Assert.Equal(130f, rider.Get<Transform>()!.Position.X, Tolerance);
    }

    [Fact]
    public void NaoDaPraMontarDuasCoisasAoMesmoTempo()
    {
        var world = new World();

        var rider = world.CreateEntity("Player");
        rider.Add(new Transform());
        rider.Add(new TopDownController { UseKeyboard = false });

        var cavalo = world.CreateEntity("Cavalo");
        cavalo.Add(new Transform(new Vector2(10f, 0f)));
        var rideCavalo = new Rideable();
        cavalo.Add(rideCavalo);

        var carro = world.CreateEntity("Carro");
        carro.Add(new Transform(new Vector2(-10f, 0f)));
        var rideCarro = new Rideable();
        carro.Add(rideCarro);

        world.Update(1f / 60f);

        Assert.True(rideCavalo.TryMount());
        Assert.False(rideCarro.TryMount(), "Entrou no carro sem descer do cavalo.");
    }

    [Fact]
    public void MontariaDestruidaComAlguemEmCimaLiberaOJogador()
    {
        // Carro que explode, montaria que sai da cena: o pior desfecho é o jogador ficar
        // invisível e sem controle pra sempre.
        var (world, rider, mount, ride) = Build(new Rideable { HideRiderWhileRiding = true });
        ride.TryMount();

        mount.Destroy();
        world.Update(1f / 60f);

        Assert.True(rider.Get<TopDownController>()!.Enabled, "O jogador ficou sem controle.");
        Assert.True(rider.Get<SpriteRenderer>()!.Visible, "O jogador ficou invisível.");
    }

    [Fact]
    public void PassageiroMortoLiberaAMontaria()
    {
        var (world, rider, _, ride) = Build(new Rideable());
        ride.TryMount();

        rider.Destroy();
        world.Update(1f / 60f);

        Assert.False(ride.IsRidden, "A montaria ficou com um dono fantasma.");
    }

    [Fact]
    public void MontarDuasVezesNaoEmpilha()
    {
        var (_, _, _, ride) = Build(new Rideable());

        Assert.True(ride.TryMount());
        Assert.False(ride.TryMount());
    }
}
