using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Scenes;

namespace FruitNinja;

/// <summary>
/// Uma fruta no ar: sobe, gira, cai. É lançada pelo <see cref="Lancador"/> e cortada pela
/// <see cref="Lamina"/>.
///
/// <para>Não usa <c>Collider</c> nem a física da engine de propósito. O corte do Fruit Ninja é
/// um teste de SEGMENTO (o traço do dedo entre um frame e o outro) contra círculo, e o passo de
/// colisão da engine testa sobreposição na posição já atualizada — um dedo rápido passaria por
/// cima da fruta sem nunca sobrepor nada. Integrar a mão aqui é meia dúzia de linhas e acerta
/// o gesto rápido, que é o jogo inteiro.</para>
/// </summary>
[SceneScript]
public sealed class Fruta : Behavior
{
    /// <summary>Id da ficha em <c>database/frutas.json</c>.</summary>
    public string Id = "";

    public float VelX;
    public float VelY;

    /// <summary>Rotação em radianos por segundo.</summary>
    public float Giro = 2f;

    /// <summary>Raio de acerto em pixels de mundo, já com o tamanho e o <c>RaioCorte</c> da
    /// ficha aplicados. A lâmina ainda multiplica pelo alcance dela.</summary>
    public float Raio = 52f;

    /// <summary>Ficha da fruta. Resolvida uma vez no Start — buscar no catálogo a cada frame
    /// com dez frutas no ar seria varredura à toa.</summary>
    public FrutaDef Def { get; private set; } = new();

    /// <summary>Já foi fatiada neste frame? Protege contra o mesmo traço contar a fruta duas
    /// vezes quando dois dedos passam juntos.</summary>
    public bool Cortada { get; private set; }

    public override void Start()
    {
        Def = CatalogoFrutas.Atual.Get(Id) ?? new FrutaDef { Id = Id, Nome = Id };
    }

    public override void Update(float deltaTime)
    {
        if (World is null || Get<Transform>() is not { } transform)
            return;

        // A escala de tempo é o poder Congelar. World.Paused não serve: ele é tudo ou nada, e
        // aqui a tela precisa continuar viva, só mais devagar.
        float dt = deltaTime * (Partida.Atual?.EscalaTempo ?? 1f);

        VelY += Arena.Gravidade * dt;
        transform.Position += new Vector2(VelX, VelY) * dt;
        transform.Rotation += Giro * dt;

        if (transform.Position.Y < Arena.LimiteDeSaida)
            return;

        // Saiu por baixo sem ser cortada: é a "escapada" que custa uma vida.
        if (!Cortada)
            Partida.Atual?.Escapou(Def, transform.Position);

        Entity.Destroy();
    }

    /// <summary>
    /// Fatia a fruta. <paramref name="direcao"/> é o sentido do traço, normalizado: as duas
    /// metades saem perpendiculares a ele, que é o que faz o corte "obedecer" ao gesto em vez
    /// de sempre abrir na horizontal.
    /// </summary>
    public void Cortar(Vector2 direcao)
    {
        if (Cortada || World is null || Get<Transform>() is not { } transform)
            return;

        Cortada = true;
        var posicao = transform.Position;

        if (Def.Tipo == TipoDeFruta.Bomba)
        {
            Explodir(posicao);
            Entity.Destroy();
            return;
        }

        // O talho do sprite é VERTICAL (a arte é a metade esquerda de uma imagem cortada ao
        // meio). Pra o corte obedecer ao gesto, a metade é girada até esse talho ficar em cima
        // da direção do traço: com esta rotação, o eixo X local da metade vira exatamente a
        // perpendicular do golpe — que é para onde cada uma tem que voar.
        float anguloDoCorte = MathF.Atan2(-direcao.X, direcao.Y);
        var normal = new Vector2(direcao.Y, -direcao.X);

        // A metade não espelhada ocupa o lado -X da arte, então ela sai para -normal.
        CriarMetade(posicao, anguloDoCorte, -normal, espelhada: false);
        CriarMetade(posicao, anguloDoCorte, normal, espelhada: true);

        Espirro.Criar(World, posicao, Color.FromHex(Def.CorSuco), Def.Gotas);

        int pontos = Partida.Atual?.Cortou(Def) ?? 0;
        if (pontos > 0)
            Partida.Atual?.Anunciar($"+{pontos}", posicao, "#FFFFFFCC");

        Entity.Destroy();
    }

    private void CriarMetade(Vector2 posicao, float rotacao, Vector2 empurrao, bool espelhada)
    {
        if (World?.Assets is null || Def.SpriteMetade.Length == 0)
            return;

        float tamanho = Def.Tamanho;
        var entidade = World.CreateEntity($"{Def.Id}_metade");

        entidade.Add(new Transform(posicao) { Rotation = rotacao });
        entidade.Add(new SpriteRenderer(World.Assets.LoadTexture(Def.SpriteMetade), layer: 12)
        {
            Size = new Vector2(tamanho, tamanho),
            FlipX = espelhada,
        });

        // A metade herda o voo da fruta e ganha o empurrão do corte — sem herdar, as duas
        // metades cairiam retas e o golpe pareceria não ter força nenhuma.
        entidade.Add(new Metade
        {
            VelX = VelX + empurrao.X * 190f,
            VelY = VelY * 0.75f + empurrao.Y * 190f,
            Giro = Giro + (espelhada ? 3.4f : -3.4f),
        });
    }

    private void Explodir(Vector2 posicao)
    {
        if (World is not null)
            Espirro.Criar(World, posicao, Color.FromBytes(255, 150, 40), 40, forca: 2.4f);

        Partida.Atual?.Explodiu(Def, posicao);
    }
}
