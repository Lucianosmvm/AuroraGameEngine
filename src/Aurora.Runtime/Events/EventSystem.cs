using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.UI;

namespace Aurora.Runtime.Events;

/// <summary>
/// Interpreta os <see cref="EventTrigger"/> do mundo a cada frame: verifica gatilhos
/// e executa as ações em sequência (Wait suspende até o tempo passar).
/// </summary>
public sealed class EventSystem
{
    private readonly World _world;
    private readonly GameState _state;
    private readonly List<(Entity Entity, Transform Transform, EventTrigger Trigger)> _buffer = [];

    /// <summary>Entidade considerada "o jogador" para gatilhos PlayerTouch.</summary>
    public string PlayerEntityName { get; set; } = "Player";

    /// <summary>
    /// Quando presente, ShowMessage/ShowChoice abrem a caixa de diálogo e a sequência
    /// de ações pausa até o jogador dispensar (modelo RPG Maker).
    /// </summary>
    public DialogueSystem? Dialogue { get; set; }

    /// <summary>ShowMessage entrega o texto aqui — a camada de UI do jogo decide como exibir.</summary>
    public event Action<string>? MessageShown;

    private bool _sceneStartFired;

    public EventSystem(World world, GameState state)
    {
        _world = world;
        _state = state;
    }

    public void Update(float deltaTime)
    {
        // Snapshot: ações podem destruir entidades no meio da varredura.
        _buffer.Clear();
        foreach (var entry in _world.Query<Transform, EventTrigger>())
            _buffer.Add(entry);

        Vector2? playerPosition = _world.TryFind(PlayerEntityName, out var player)
            ? player.Get<Transform>()?.Position
            : null;

        foreach (var (entity, transform, trigger) in _buffer)
        {
            if (trigger.Running)
            {
                Advance(entity, trigger, deltaTime);
                continue;
            }

            if (trigger.Once && trigger.Fired)
                continue;

            if (ShouldFire(trigger, transform, playerPosition))
            {
                trigger.Fired = true;
                trigger.Running = true;
                trigger.ActionIndex = 0;
                trigger.WaitTimer = 0f;
                Advance(entity, trigger, deltaTime);
            }
        }

        _sceneStartFired = true;
    }

    private bool ShouldFire(EventTrigger trigger, Transform transform, Vector2? playerPosition)
        => trigger.Trigger switch
        {
            "SceneStart" => !_sceneStartFired,
            "PlayerTouch" => playerPosition is { } player
                && Vector2.Distance(player, transform.Position) <= trigger.Radius,
            "SwitchOn" => trigger.Switch is not null && _state.GetSwitch(trigger.Switch),
            _ => false,
        };

    private void Advance(Entity self, EventTrigger trigger, float deltaTime)
    {
        if (trigger.WaitTimer > 0f)
        {
            trigger.WaitTimer -= deltaTime;
            if (trigger.WaitTimer > 0f)
                return;
        }

        if (trigger.WaitingDialogue)
        {
            if (Dialogue?.IsActive == true)
                return;
            trigger.WaitingDialogue = false;
        }

        while (trigger.ActionIndex < trigger.Actions.Count)
        {
            var action = trigger.Actions[trigger.ActionIndex];
            trigger.ActionIndex++;

            if (action.Type == "Wait")
            {
                trigger.WaitTimer = action.Seconds;
                if (trigger.WaitTimer > 0f)
                    return; // Retoma no próximo frame, após o tempo passar.
                continue;
            }

            Execute(self, action);

            // Diálogo aberto: pausa a sequência até o jogador dispensar.
            if (action.Type is "ShowMessage" or "ShowChoice" && Dialogue?.IsActive == true)
            {
                trigger.WaitingDialogue = true;
                return;
            }
        }

        trigger.Running = false;
    }

    private void Execute(Entity self, EventAction action)
    {
        switch (action.Type)
        {
            case "SetVariable" when action.Name is not null:
                if (string.Equals(action.Op, "Add", StringComparison.OrdinalIgnoreCase))
                    _state.AddVariable(action.Name, action.Value);
                else
                    _state.SetVariable(action.Name, action.Value);
                break;

            case "SetSwitch" when action.Name is not null:
                _state.SetSwitch(action.Name, action.On);
                break;

            case "Teleport":
            {
                var target = ResolveTarget(self, action.Name);
                var transform = target?.Get<Transform>();
                if (transform is not null)
                    transform.Position = new Vector2(action.X, action.Y);
                break;
            }

            case "Destroy":
                ResolveTarget(self, action.Name)?.Destroy();
                break;

            case "ShowMessage" when action.Text is not null:
                // Name = nome do falante (opcional).
                Dialogue?.ShowMessage(action.Text, action.Name);
                MessageShown?.Invoke(action.Text);
                break;

            case "ShowChoice" when Dialogue is not null && action.Options.Count > 0:
                Dialogue.ShowChoice(action.Text ?? "",
                    action.Options.Select(o => o.Text).ToList(),
                    index =>
                    {
                        var option = action.Options[index];
                        if (option.Switch is not null)
                            _state.SetSwitch(option.Switch, true);
                        // Name = variável que recebe o índice escolhido (opcional).
                        if (action.Name is not null)
                            _state.SetVariable(action.Name, index);
                    });
                break;
        }
    }

    private Entity? ResolveTarget(Entity self, string? name)
    {
        if (name is null || name.Equals("Self", StringComparison.OrdinalIgnoreCase))
            return self;

        return _world.TryFind(name, out var entity) ? entity : null;
    }
}
