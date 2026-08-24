using System.Numerics;
using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Clima da cena: chuva, neve, tempestade, névoa, cinzas. Um componente numa entidade vazia e a
/// cena inteira ganha o efeito — sem montar emissor de partícula na mão, sem calcular onde as
/// gotas nascem, sem script.
///
/// <para>Não desenha nada por conta própria: configura um <see cref="ParticleEmitter"/> e um
/// <see cref="GlobalTint"/> na mesma entidade (criando-os se não existirem) e os mantém colados
/// na câmera. Reaproveitar as duas peças que já existiam é o que faz o clima herdar de graça o
/// z-order por camada, o teto de partículas e a composição de tinta.</para>
///
/// <para>Trocar <see cref="Kind"/> ou <see cref="Intensity"/> em jogo (pela ação de evento
/// <c>SetWeather</c>, por script, ou no inspector com o jogo rodando) reconfigura na hora.</para>
/// </summary>
public sealed class Weather : Behavior
{
    /// <summary>
    /// None | Rain | Storm | Snow | Fog | Wind | Sandstorm | Ash. No editor isto é uma lista, não
    /// um campo de texto — decorar os nomes válidos não deveria ser parte de fazer um jogo.
    /// Nome desconhecido cai em None, com aviso: erro de digitação não pode virar cena sem clima
    /// e sem explicação.
    /// </summary>
    public string Kind = "Rain";

    /// <summary>0 = parado, 1 = o preset cheio. Escala a quantidade de partículas e a força da
    /// tinta juntas, que é o que faz "a chuva vai apertando" ser um número só.</summary>
    public float Intensity = 1f;

    /// <summary>Deslocamento horizontal em pixels/s. Negativo sopra pra esquerda. Some com a
    /// queda do preset, então vento forte inclina a chuva.</summary>
    public float Wind;

    /// <summary>Relâmpagos: clarão de tela cheia em intervalos aleatórios. Ligado por padrão no
    /// preset Storm.</summary>
    public bool Lightning;

    public float LightningMinInterval = 5f;
    public float LightningMaxInterval = 16f;

    /// <summary>Som tocado a cada relâmpago (ex.: "sounds/trovao.wav"). Vazio = mudo.</summary>
    public string ThunderSound = "";

    /// <summary>Textura das partículas, relativa a Assets. Vazio = quadradinho colorido, que já
    /// serve pra neve e cinza; chuva fica melhor com um risco vertical.</summary>
    public string Texture = "";

    /// <summary>Camada de desenho das partículas. Alto por padrão: clima passa por cima do
    /// cenário e dos personagens.</summary>
    public int Layer = 100;

    /// <summary>Quanto a área de nascimento passa da borda da tela, em pixels. A folga existe pra
    /// partícula empurrada pelo vento não aparecer do nada no meio da tela.</summary>
    public float Margin = 120f;

    /// <summary>True enquanto o clarão do relâmpago está na tela.</summary>
    public bool IsFlashing => _flashTimer > 0f;

    private const float FlashDuration = 0.12f;

    private string _appliedType = "";
    private float _appliedIntensity = -1f;
    private float _appliedWind = float.NaN;
    private string _appliedTexture = "";

    private float _lightningTimer;
    private float _flashTimer;
    private readonly Random _random = new();

    private Color _baseTint = Color.FromBytes(0, 0, 0);
    private float _baseTintIntensity;

    public override void Start()
    {
        // Cria o que falta: a ideia é "arrasta um Weather numa entidade vazia e funciona". Exigir
        // que o autor monte ParticleEmitter + GlobalTint na mão devolveria justamente o trabalho
        // que este componente existe pra tirar.
        if (Get<Transform>() is null)
            Entity.Add(new Transform());

        if (Get<ParticleEmitter>() is null)
            Entity.Add(new ParticleEmitter());

        if (Get<GlobalTint>() is null)
            Entity.Add(new GlobalTint { Intensity = 0f });

        ScheduleNextLightning();
    }

    public override void Update(float deltaTime)
    {
        if (World is null)
            return;

        if (_appliedType != Kind || _appliedIntensity != Intensity
            || _appliedWind != Wind || _appliedTexture != Texture)
            Apply();

        FollowCamera();
        UpdateLightning(deltaTime);
    }

    /// <summary>Troca o clima em jogo. É o que a ação de evento <c>SetWeather</c> chama.</summary>
    public void Set(string type, float intensity)
    {
        Kind = type;
        Intensity = Math.Clamp(intensity, 0f, 1f);
    }

    /// <summary>
    /// Preset de um tipo de clima: os números que fazem chuva parecer chuva.
    ///
    /// <para><c>Horizontal</c> marca os climas que sopram de lado (vento, tempestade de areia):
    /// neles a direção de emissão vem do <see cref="Wind"/> em vez do ângulo fixo, senão um vento
    /// pra direita teria as folhas voando pra esquerda. <c>DefaultWind</c> é o sopro usado quando
    /// o autor deixou Wind em zero — "vento" sem vento nenhum não seria nada na tela.</para>
    /// </summary>
    private readonly record struct Preset(
        float Rate, float SpeedMin, float SpeedMax, float AngleMin, float AngleMax,
        float GravityY, float SizeStart, float SizeEnd, float LifeMin, float LifeMax,
        Color Color, Color TintColor, float TintIntensity, bool Lightning, int MaxParticles,
        bool Horizontal = false, float DefaultWind = 0f, float AngleSpread = 0f);

    private static Preset? For(string type) => type.ToLowerInvariant() switch
    {
        // Ângulos em graus, medidos como no ParticleEmitter (0 = direita, 90 = baixo).
        "rain" => new Preset(
            Rate: 420f, SpeedMin: 620f, SpeedMax: 780f, AngleMin: 86f, AngleMax: 94f,
            GravityY: 300f, SizeStart: 3f, SizeEnd: 3f, LifeMin: 0.7f, LifeMax: 1f,
            Color: Color.FromBytes(170, 200, 235), TintColor: Color.FromBytes(20, 30, 60),
            TintIntensity: 0.18f, Lightning: false, MaxParticles: 900),

        "storm" => new Preset(
            Rate: 800f, SpeedMin: 780f, SpeedMax: 980f, AngleMin: 78f, AngleMax: 92f,
            GravityY: 420f, SizeStart: 3f, SizeEnd: 3f, LifeMin: 0.6f, LifeMax: 0.9f,
            Color: Color.FromBytes(180, 205, 240), TintColor: Color.FromBytes(10, 14, 34),
            TintIntensity: 0.42f, Lightning: true, MaxParticles: 1400),

        "snow" => new Preset(
            Rate: 140f, SpeedMin: 30f, SpeedMax: 70f, AngleMin: 60f, AngleMax: 120f,
            GravityY: 12f, SizeStart: 4f, SizeEnd: 4f, LifeMin: 3.5f, LifeMax: 6f,
            Color: Color.White, TintColor: Color.FromBytes(200, 214, 235),
            TintIntensity: 0.1f, Lightning: false, MaxParticles: 700),

        "fog" => new Preset(
            Rate: 14f, SpeedMin: 6f, SpeedMax: 18f, AngleMin: 0f, AngleMax: 20f,
            GravityY: 0f, SizeStart: 190f, SizeEnd: 240f, LifeMin: 7f, LifeMax: 12f,
            Color: Color.FromBytes(210, 214, 220).WithAlpha(0.16f), TintColor: Color.FromBytes(190, 195, 205),
            TintIntensity: 0.12f, Lightning: false, MaxParticles: 90),

        "ash" => new Preset(
            Rate: 90f, SpeedMin: 20f, SpeedMax: 55f, AngleMin: 65f, AngleMax: 115f,
            GravityY: 18f, SizeStart: 3f, SizeEnd: 2f, LifeMin: 4f, LifeMax: 7f,
            Color: Color.FromBytes(90, 88, 84), TintColor: Color.FromBytes(60, 45, 35),
            TintIntensity: 0.22f, Lightning: false, MaxParticles: 500),

        "wind" => new Preset(
            Rate: 70f, SpeedMin: 320f, SpeedMax: 520f, AngleMin: 0f, AngleMax: 0f,
            GravityY: 10f, SizeStart: 5f, SizeEnd: 4f, LifeMin: 1.6f, LifeMax: 2.8f,
            Color: Color.FromBytes(150, 140, 110).WithAlpha(0.55f), TintColor: Color.FromBytes(120, 110, 85),
            TintIntensity: 0.05f, Lightning: false, MaxParticles: 260,
            Horizontal: true, DefaultWind: -220f, AngleSpread: 14f),

        "sandstorm" => new Preset(
            Rate: 900f, SpeedMin: 520f, SpeedMax: 900f, AngleMin: 0f, AngleMax: 0f,
            GravityY: 40f, SizeStart: 4f, SizeEnd: 3f, LifeMin: 1f, LifeMax: 1.8f,
            Color: Color.FromBytes(196, 164, 108).WithAlpha(0.8f), TintColor: Color.FromBytes(168, 134, 74),
            TintIntensity: 0.45f, Lightning: false, MaxParticles: 1500,
            Horizontal: true, DefaultWind: -520f, AngleSpread: 20f),

        "none" or "" => null,
        _ => null,
    };

    private void Apply()
    {
        bool typeChanged = _appliedType != Kind;

        _appliedType = Kind;
        _appliedIntensity = Intensity;
        _appliedWind = Wind;
        _appliedTexture = Texture;

        var emitter = Get<ParticleEmitter>();
        var tint = Get<GlobalTint>();
        if (emitter is null || tint is null)
            return;

        var preset = For(Kind);

        if (preset is null)
        {
            if (typeChanged && Kind.Length > 0
                && !Kind.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    $"[Weather] Tipo '{Kind}' não existe — use None, Rain, Storm, Snow, Fog ou Ash. Clima desligado.");
            }

            emitter.Emitting = false;
            tint.Intensity = 0f;
            _baseTintIntensity = 0f;
            return;
        }

        var p = preset.Value;
        float scale = Math.Clamp(Intensity, 0f, 1f);

        emitter.Emitting = scale > 0f;
        emitter.Rate = p.Rate * scale;
        emitter.MaxParticles = Math.Max(1, (int)(p.MaxParticles * scale));
        emitter.SpeedMin = p.SpeedMin;
        emitter.SpeedMax = p.SpeedMax;

        if (p.Horizontal)
        {
            // Clima que sopra de lado: quem manda na direção é o vento, não um ângulo fixo.
            float wind = Wind != 0f ? Wind : p.DefaultWind;
            float center = wind < 0f ? 180f : 0f;

            emitter.AngleMin = center - p.AngleSpread;
            emitter.AngleMax = center + p.AngleSpread;
            emitter.Gravity = new Vector2(wind, p.GravityY);
        }
        else
        {
            emitter.AngleMin = p.AngleMin;
            emitter.AngleMax = p.AngleMax;
            emitter.Gravity = new Vector2(Wind, p.GravityY);
        }
        emitter.SizeStart = p.SizeStart;
        emitter.SizeEnd = p.SizeEnd;
        emitter.LifeMin = p.LifeMin;
        emitter.LifeMax = p.LifeMax;
        emitter.ColorStart = p.Color;
        emitter.ColorEnd = p.Color;
        emitter.Layer = Layer;

        if (Texture.Length > 0)
            emitter.Texture = World?.Assets?.LoadTexture(Texture);
        else
            emitter.Texture = null;

        _baseTint = p.TintColor;
        _baseTintIntensity = p.TintIntensity * scale;
        tint.Color = _baseTint;
        tint.Intensity = _baseTintIntensity;

        // Só o preset decide o padrão do relâmpago, e só quando o TIPO muda: assim quem desligou
        // o raio numa tempestade à mão não vê a decisão ser desfeita ao mexer na intensidade.
        if (typeChanged)
            Lightning = p.Lightning;
    }

    /// <summary>
    /// Cola a entidade na câmera e dimensiona a área de nascimento pelo que está visível. Sem
    /// isso o clima seria uma poça de partículas parada num canto do mapa — a chuva precisa
    /// nascer acima do que o jogador está vendo, onde quer que ele esteja.
    /// </summary>
    private void FollowCamera()
    {
        if (Get<Transform>() is not { } transform || Get<ParticleEmitter>() is not { } emitter)
            return;

        if (World?.Camera is not { } camera)
            return;

        var (min, max) = camera.GetVisibleBounds();
        float width = max.X - min.X + Margin * 2f;
        float height = max.Y - min.Y + Margin * 2f;

        emitter.SpawnAreaWidth = width;
        emitter.SpawnAreaHeight = height;

        // Meia tela acima do centro: as partículas caem, então nascer no meio faria metade
        // aparecer já dentro do campo de visão.
        transform.Position = camera.Position - new Vector2(0f, height * 0.5f);
    }

    private void UpdateLightning(float deltaTime)
    {
        if (Get<GlobalTint>() is not { } tint)
            return;

        if (_flashTimer > 0f)
        {
            _flashTimer -= deltaTime;

            if (_flashTimer <= 0f)
            {
                tint.Color = _baseTint;
                tint.Intensity = _baseTintIntensity;
            }
            else
            {
                ApplyFlash(tint);
            }

            return;
        }

        if (!Lightning || Intensity <= 0f)
            return;

        _lightningTimer -= deltaTime;
        if (_lightningTimer > 0f)
            return;

        _flashTimer = FlashDuration;

        // Acende no MESMO frame em que o raio cai. Deixar pro frame seguinte dava um frame de
        // "está relampejando" com a tela ainda escura — e o trovão saía antes da luz.
        ApplyFlash(tint);
        ScheduleNextLightning();

        if (ThunderSound.Length > 0)
            World?.Audio?.Play(ThunderSound);
    }

    /// <summary>Clarão decaindo do branco até o tint normal — um corte seco parece bug de
    /// render, não raio.</summary>
    private void ApplyFlash(GlobalTint tint)
    {
        float t = _flashTimer / FlashDuration;
        tint.Color = Color.White;
        tint.Intensity = _baseTintIntensity + (0.85f - _baseTintIntensity) * t;
    }

    private void ScheduleNextLightning()
    {
        float min = MathF.Max(0.2f, LightningMinInterval);
        float max = MathF.Max(min, LightningMaxInterval);
        _lightningTimer = min + (float)_random.NextDouble() * (max - min);
    }
}
