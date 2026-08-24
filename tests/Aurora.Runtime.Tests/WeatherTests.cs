using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Events;
using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Clima de cena. O contrato é "põe o componente numa entidade vazia e funciona": ele monta o
/// emissor e a tinta sozinho, acompanha a câmera e reage a troca de tipo em jogo.
/// </summary>
public class WeatherTests
{
    private const float Tolerance = 0.01f;

    private static (World World, Entity Entity, Weather Weather) Build(Weather weather)
    {
        var world = new World { Camera = new Camera2D() };
        world.Camera!.SetViewport(800, 600);

        var entity = world.CreateEntity("Clima");
        entity.Add(weather);

        world.Update(1f / 60f);
        return (world, entity, weather);
    }

    [Fact]
    public void MontaEmissorETintaSozinho()
    {
        // Exigir que o autor adicione ParticleEmitter + GlobalTint na mão devolveria o trabalho
        // que o componente existe pra tirar.
        var (_, entity, _) = Build(new Weather { Kind = "Rain" });

        Assert.NotNull(entity.Get<ParticleEmitter>());
        Assert.NotNull(entity.Get<GlobalTint>());
        Assert.NotNull(entity.Get<Transform>());
    }

    [Fact]
    public void ChuvaEmiteParticulasCaindo()
    {
        var (_, entity, _) = Build(new Weather { Kind = "Rain", Intensity = 1f });

        var emitter = entity.Get<ParticleEmitter>()!;
        Assert.True(emitter.Emitting);
        Assert.True(emitter.Rate > 0f);
        Assert.True(emitter.Gravity.Y > 0f, "Chuva tem que cair.");
    }

    [Fact]
    public void IntensidadeZeroDesligaSemPrecisarTrocarOTipo()
    {
        var (_, entity, _) = Build(new Weather { Kind = "Rain", Intensity = 0f });

        Assert.False(entity.Get<ParticleEmitter>()!.Emitting);
        Assert.Equal(0f, entity.Get<GlobalTint>()!.Intensity, Tolerance);
    }

    [Fact]
    public void IntensidadeEscalaParticulasETinta()
    {
        var (_, cheio, _) = Build(new Weather { Kind = "Storm", Intensity = 1f });
        var (_, fraco, _) = Build(new Weather { Kind = "Storm", Intensity = 0.25f });

        Assert.True(cheio.Get<ParticleEmitter>()!.Rate > fraco.Get<ParticleEmitter>()!.Rate);
        Assert.True(cheio.Get<GlobalTint>()!.Intensity > fraco.Get<GlobalTint>()!.Intensity);
    }

    [Fact]
    public void VentoInclinaAQueda()
    {
        var (_, entity, _) = Build(new Weather { Kind = "Rain", Wind = -250f });

        Assert.Equal(-250f, entity.Get<ParticleEmitter>()!.Gravity.X, Tolerance);
    }

    [Fact]
    public void TipoDesconhecidoDesligaEmVezDeQuebrar()
    {
        var (_, entity, _) = Build(new Weather { Kind = "furacao_de_sapos" });

        Assert.False(entity.Get<ParticleEmitter>()!.Emitting);
    }

    [Fact]
    public void TempestadeLigaORaioEChuvaNao()
    {
        // O preset decide o padrão: quem escolhe "Storm" espera trovoada sem configurar mais nada.
        var (_, _, storm) = Build(new Weather { Kind = "Storm" });
        var (_, _, rain) = Build(new Weather { Kind = "Rain" });

        Assert.True(storm.Lightning);
        Assert.False(rain.Lightning);
    }

    [Fact]
    public void OClimaAcompanhaACamera()
    {
        // Sem seguir, o clima viraria uma poça de partículas parada num canto do mapa.
        var (world, entity, _) = Build(new Weather { Kind = "Snow" });

        world.Camera!.Position = new Vector2(4000f, 2000f);
        world.Update(1f / 60f);

        var position = entity.Get<Transform>()!.Position;
        Assert.Equal(4000f, position.X, 1f);
        Assert.True(position.Y < 2000f, "A neve tem que nascer acima do que está visível.");
    }

    [Fact]
    public void AreaDeNascimentoCobreATelaComFolga()
    {
        var (_, entity, _) = Build(new Weather { Kind = "Rain", Margin = 100f });

        var emitter = entity.Get<ParticleEmitter>()!;
        Assert.True(emitter.SpawnAreaWidth > 800f, "Precisa passar da borda pro vento não revelar o nascimento.");
        Assert.True(emitter.SpawnAreaHeight > 600f);
    }

    [Fact]
    public void TrocarOTipoEmJogoReconfiguraNaHora()
    {
        var (world, entity, weather) = Build(new Weather { Kind = "Rain" });
        float chuva = entity.Get<ParticleEmitter>()!.Rate;

        weather.Set("Snow", 1f);
        world.Update(1f / 60f);

        Assert.True(entity.Get<ParticleEmitter>()!.Rate < chuva, "Neve cai bem mais rala que chuva.");
    }

    [Fact]
    public void AcaoDeEventoSetWeatherTrocaOClimaDaCena()
    {
        var world = new World { Camera = new Camera2D() };
        var events = new EventSystem(world, new GameState());

        var sky = world.CreateEntity("Clima");
        sky.Add(new Weather { Kind = "Rain" });
        world.Update(1f / 60f);

        var trigger = world.CreateEntity("Gatilho");
        trigger.Add(new Transform());
        trigger.Add(new EventTrigger
        {
            Trigger = "SceneStart",
            Actions = [new EventAction { Type = "SetWeather", Name = "Storm", Value = 1f }],
        });

        events.Update(1f / 60f);

        Assert.Equal("Storm", sky.Get<Weather>()!.Kind);
    }

    [Fact]
    public void RaioAcendeEVoltaSozinho()
    {
        var (world, entity, _) = Build(new Weather
        {
            Kind = "Storm",
            Lightning = true,
            LightningMinInterval = 0.2f,
            LightningMaxInterval = 0.2f,
        });

        var weather = entity.Get<Weather>()!;
        float normal = entity.Get<GlobalTint>()!.Intensity;

        // Até o raio cair.
        for (int i = 0; i < 60 && !weather.IsFlashing; i++)
            world.Update(0.02f);

        Assert.True(weather.IsFlashing, "O relâmpago não caiu no intervalo pedido.");
        Assert.True(entity.Get<GlobalTint>()!.Intensity > normal, "O clarão tem que clarear a tela.");

        // E até passar.
        for (int i = 0; i < 60 && weather.IsFlashing; i++)
            world.Update(0.02f);

        Assert.False(weather.IsFlashing);
        Assert.Equal(normal, entity.Get<GlobalTint>()!.Intensity, Tolerance);
    }

    [Fact]
    public void SemRaioNaoAcendeNunca()
    {
        var (world, entity, _) = Build(new Weather
        {
            Kind = "Rain",
            Lightning = false,
            LightningMinInterval = 0.1f,
            LightningMaxInterval = 0.1f,
        });

        for (int i = 0; i < 300; i++)
            world.Update(0.02f);

        Assert.False(entity.Get<Weather>()!.IsFlashing);
    }
}
