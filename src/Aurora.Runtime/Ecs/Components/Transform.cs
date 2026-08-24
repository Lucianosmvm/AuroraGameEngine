using System.Numerics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>Posição, rotação (radianos) e escala de uma entidade no mundo 2D.</summary>
public sealed class Transform : IComponent
{
    /// <summary>
    /// Posição em coordenadas de MUNDO — inclusive quando a entidade tem <see cref="Parent"/>.
    ///
    /// <para>Uma hierarquia clássica guardaria posição local e resolveria a de mundo na hora de
    /// desenhar. Aqui não: renderização, colisão, câmera, partícula, IA e rede leem este campo
    /// direto como mundo, em quase uma centena de lugares. Trocar o significado exigiria acertar
    /// todos eles, e qualquer um esquecido erraria a posição em silêncio. Em vez disso, o pai
    /// EMPURRA o filho (ver <see cref="World.UpdateHierarchy"/>): o filho é levado junto pelo
    /// movimento do pai, e continua legível/gravável em mundo por qualquer sistema.</para>
    /// </summary>
    public Vector2 Position;

    public float Rotation;
    public Vector2 Scale = Vector2.One;

    /// <summary>
    /// Nome da entidade que carrega esta. Null = solta no mundo.
    ///
    /// <para>Por NOME, e não por id: id não sobrevive a recarregar a cena nem ao save, e nome já
    /// é a identidade que o resto da engine usa (World.TryFind, NavAgent.Follow, PlayerEntityName).
    /// Pai inexistente não é erro — o filho só fica parado onde está, que é o comportamento certo
    /// quando o pai morre no meio do jogo.</para>
    /// </summary>
    public string? Parent;

    /// <summary>Girar o pai gira o filho em volta dele (órbita), além de girar o próprio filho.
    /// Desligue pra coisa que acompanha a posição mas tem que ficar de pé — barra de vida sobre
    /// o inimigo, marcador de alvo.</summary>
    public bool InheritRotation = true;

    /// <summary>Estado do pai no frame anterior, pra calcular o quanto ele andou/girou. Nulo até
    /// o primeiro frame com pai válido: no frame de estreia o filho NÃO é movido, senão ele
    /// pularia da posição onde o designer o colocou na cena para cima do pai.</summary>
    internal Vector2? LastParentPosition;

    internal float LastParentRotation;

    public Transform()
    {
    }

    public Transform(float x, float y)
    {
        Position = new Vector2(x, y);
    }

    public Transform(Vector2 position)
    {
        Position = position;
    }
}
