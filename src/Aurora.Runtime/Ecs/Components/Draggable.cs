using System.Numerics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Arrasta a entidade pelo mouse/toque — o dedo "gruda" no ponto onde pegou (sem o sprite pular
/// pro centro do cursor) e solta verificando se caiu perto de um alvo válido. Mesmo componente
/// serve desktop e Android: toque entra pelo mesmo <c>Input.MousePosition</c>/<c>IsMouseDown</c>
/// que o clique esquerdo (ver InputManager.SetPointer).
///
/// <para>Não pede <see cref="Collider"/> nem <see cref="SpriteRenderer"/>: o hitbox de pegar é
/// <see cref="Width"/>/<see cref="Height"/>, centrado no <see cref="Transform"/> — funciona com
/// sprite, texto ou nada visível.</para>
///
/// <para>Zona de soltura é qualquer entidade com <see cref="Transform"/> a até
/// <see cref="DropRadius"/> pixels de onde a arrastada foi solta e que combine com
/// <see cref="DropTarget"/> — a mesma sintaxe de alvo usada em Damage/Teleport/etc
/// (<see cref="Tags.Matches"/>): vazio = qualquer lugar serve, <c>#etiqueta</c> = só quem tem a
/// etiqueta, texto = prefixo do nome. Não precisa de Collider na zona, só um Transform marcando
/// o slot.</para>
/// </summary>
public sealed class Draggable : Behavior
{
    /// <summary>Largura do hitbox de pegar (pixels), centrado no Transform.</summary>
    public float Width = 64f;

    /// <summary>Altura do hitbox de pegar (pixels), centrado no Transform.</summary>
    public float Height = 96f;

    /// <summary>Filtro da zona de soltura: vazio (padrão) = qualquer lugar serve, a entidade fica
    /// onde foi solta. <c>#etiqueta</c> ou prefixo de nome exige soltar perto de uma entidade que
    /// combine — soltar fora conta como inválido (ver <see cref="ReturnIfInvalid"/>).</summary>
    public string DropTarget = "";

    /// <summary>Distância máxima (pixels) do centro da entidade arrastada até a zona-alvo pra
    /// contar como "soltou em cima". Só importa quando <see cref="DropTarget"/> não é vazio.</summary>
    public float DropRadius = 32f;

    /// <summary>True (padrão): soltar fora de uma zona válida devolve a entidade pra posição de
    /// onde foi pega — o normal pra carta que só pode ir num slot certo. Desligue pra deixar cair
    /// em qualquer canto mesmo sem achar zona (então <see cref="DropTarget"/> vira só uma forma de
    /// saber ONDE caiu, sem travar o solto).</summary>
    public bool ReturnIfInvalid = true;

    /// <summary>Switch do GameState ligado quando solta numa zona válida — um EventTrigger
    /// SwitchOn em outra entidade reage a isto sem nenhum script. Vazio = não mexe em switch.</summary>
    public string? DropSwitch;

    /// <summary>Nome da entidade-zona onde foi solto com sucesso, só no frame da soltura (vazio
    /// no resto do tempo, inclusive durante o arrasto). Leitura pra script que prefira checar por
    /// código em vez de <see cref="DropSwitch"/>.</summary>
    public string DroppedOn { get; private set; } = "";

    /// <summary>True enquanto o dedo/mouse está segurando esta entidade.</summary>
    public bool IsDragging { get; private set; }

    private Vector2 _grabOffset;
    private Vector2 _startPosition;

    public override void Update(float deltaTime)
    {
        if (Get<Transform>() is not { } transform || World is null)
            return;

        // DroppedOn só vale no frame exato da soltura — resetado aqui pra não sobrar "true" pro
        // frame seguinte, quando ninguém mais está olhando o resultado.
        DroppedOn = "";

        if (World.Input is not { } input)
            return;

        Vector2 pointer = input.MousePosition;

        if (!IsDragging)
        {
            // Diálogo bloqueante (ShowMessage/ShowChoice) trava arrastar igual trava os
            // controllers de movimento — sem isso daria pra rearranjar as cartas com a caixa
            // de texto aberta na tela.
            if (!input.WasMouseClicked() || World.Dialogue?.ShouldBlockPlayer == true)
                return;

            if (MathF.Abs(pointer.X - transform.Position.X) > Width / 2f
                || MathF.Abs(pointer.Y - transform.Position.Y) > Height / 2f)
                return;

            IsDragging = true;
            _startPosition = transform.Position;
            _grabOffset = transform.Position - pointer;
            return;
        }

        if (input.IsMouseDown())
        {
            transform.Position = pointer + _grabOffset;
            return;
        }

        IsDragging = false;

        var target = DropTarget.Length > 0 ? FindDropTarget(transform.Position) : null;
        bool valid = DropTarget.Length == 0 || target is not null;

        if (valid)
        {
            DroppedOn = target?.Name ?? "";
            if (!string.IsNullOrEmpty(DropSwitch))
                World.State?.SetSwitch(DropSwitch, true);
        }
        else if (ReturnIfInvalid)
        {
            transform.Position = _startPosition;
        }
    }

    private Entity? FindDropTarget(Vector2 position)
    {
        Entity? best = null;
        float bestDistanceSquared = DropRadius * DropRadius;

        foreach (var (candidate, candidateTransform) in World!.Query<Transform>())
        {
            if (candidate.Id == Entity.Id || !Tags.Matches(candidate, DropTarget))
                continue;

            float distanceSquared = Vector2.DistanceSquared(position, candidateTransform.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                best = candidate;
            }
        }

        return best;
    }
}
