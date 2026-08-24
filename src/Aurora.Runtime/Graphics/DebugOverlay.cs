using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;

namespace Aurora.Runtime.Graphics;

/// <summary>
/// Camada de diagnóstico ligada por <c>--debug</c> na linha de comando (o Play do editor tem
/// um checkbox pra isso). Desenha a hitbox de cada Collider onde ela REALMENTE está depois de
/// controlador, física e script mexerem, e um bloco de números no canto.
///
/// O editor já mostra hitbox no viewport, mas ali é a posição salva na cena — o que quebra
/// gameplay é quase sempre a posição em movimento: offset que não acompanha o sprite, collider
/// que ficou do tamanho do placeholder, trigger que nunca encosta. Sem isto, jogo rodando era
/// caixa preta.
/// </summary>
public sealed class DebugOverlay
{
    /// <summary>Cor de cada tipo de collider — mesma convenção do viewport do editor.</summary>
    private static readonly Color SolidColor = Color.FromBytes(80, 230, 120, 210);
    private static readonly Color TriggerColor = Color.FromBytes(255, 170, 60, 210);
    private static readonly Color KinematicColor = Color.FromBytes(120, 190, 255, 210);

    /// <summary>Janela de suavização do FPS. Sem média, o número pisca tanto que não dá pra ler;
    /// meio segundo é curto o bastante pra ainda mostrar um engasgo.</summary>
    private const float SampleWindow = 0.5f;

    private float _elapsed;
    private int _frames;
    private float _fps;

    /// <summary>Espessura da linha da hitbox em pixels de MUNDO. Fixa de propósito: com zoom
    /// de câmera a linha engrossa/afina junto com a cena, que é o comportamento esperado.</summary>
    public float LineThickness { get; set; } = 1f;

    /// <summary>Contabiliza um frame. Separado do desenho porque o FPS tem que contar mesmo
    /// nos frames em que o texto não é desenhado (sem fonte carregada, por exemplo).</summary>
    public void Tick(float deltaTime)
    {
        _elapsed += deltaTime;
        _frames++;

        if (_elapsed < SampleWindow)
            return;

        _fps = _frames / _elapsed;
        _elapsed = 0f;
        _frames = 0;
    }

    /// <summary>Contorno de cada Collider, em coordenadas de mundo (chamar dentro do passe da
    /// câmera). Só o contorno: preenchido esconderia o sprite que se está tentando conferir.</summary>
    public void DrawColliders(SpriteBatch batch, World world)
    {
        foreach (var (_, transform, collider) in world.Query<Transform, Collider>())
        {
            // Mesma conta que World usa pra testar sobreposição: centro = posição + offset, e a
            // escala do Transform NÃO entra. Desenhar diferente disso seria desenhar mentira.
            Vector2 center = transform.Position + collider.Offset;
            float halfWidth = collider.Shape == ColliderShape.Circle ? collider.Radius : collider.Width * 0.5f;
            float halfHeight = collider.Shape == ColliderShape.Circle ? collider.Radius : collider.Height * 0.5f;

            Color color = !collider.IsSolid ? TriggerColor
                : collider.IsKinematic ? KinematicColor
                : SolidColor;

            DrawOutline(batch, center - new Vector2(halfWidth, halfHeight),
                new Vector2(halfWidth * 2, halfHeight * 2), color);
        }
    }

    /// <summary>Bloco de números no canto superior esquerdo, em pixels de tela (chamar no passe
    /// de UI). Sem fonte carregada não desenha nada — quem chama decide se vale avisar.</summary>
    public void DrawStats(SpriteBatch batch, Font font, World world, string sceneName)
    {
        int colliders = 0;
        foreach (var _ in world.Query<Transform, Collider>())
            colliders++;

        string text = $"FPS {_fps:0}\nEntidades {world.Entities.Count()}\nColliders {colliders}\nCena {sceneName}";

        var size = font.MeasureText(text);
        batch.DrawRect(new Vector2(6, 6), size + new Vector2(12, 10), Color.FromBytes(0, 0, 0, 160));
        font.Draw(batch, text, new Vector2(12, 11), Color.FromBytes(120, 255, 160));
    }

    /// <summary>Quatro retângulos finos. O SpriteBatch não desenha linha nem contorno, e um
    /// retângulo cheio translúcido por cima do sprite atrapalha mais do que ajuda.</summary>
    private void DrawOutline(SpriteBatch batch, Vector2 topLeft, Vector2 size, Color color)
    {
        float t = LineThickness;

        batch.DrawRect(topLeft, new Vector2(size.X, t), color);
        batch.DrawRect(new Vector2(topLeft.X, topLeft.Y + size.Y - t), new Vector2(size.X, t), color);
        batch.DrawRect(topLeft, new Vector2(t, size.Y), color);
        batch.DrawRect(new Vector2(topLeft.X + size.X - t, topLeft.Y), new Vector2(t, size.Y), color);
    }
}
