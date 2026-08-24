using System.Numerics;
using Aurora.Runtime.UI;
using Silk.NET.Input;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Montaria ou veículo em que o jogador entra e sai. Serve pro cavalo, pro carro, pro barco, pro
/// mecha — a diferença entre eles é só qual controlador de movimento a entidade tem.
///
/// <para>É a peça que faltava entre "existe um veículo na cena" e "o jogador está dirigindo":
/// sem ela um <see cref="VehicleController"/> lê o input direto e o carro sai andando sozinho
/// junto com o personagem. O que este componente faz é <b>transferir o controle</b> — desliga o
/// controlador do passageiro, liga o da montaria, e devolve na saída.</para>
///
/// <para>A câmera não precisa de nada: como o passageiro anda junto, colado no assento, um
/// <c>CameraController</c> seguindo "Player" continua funcionando montado.</para>
/// </summary>
public sealed class Rideable : Behavior
{
    /// <summary>Tecla de entrar/sair, pelo nome do enum (E, F, Enter…). Vazio = só por botão de
    /// toque ou por código.</summary>
    public string InteractKey = "E";

    /// <summary>Distância máxima, em pixels, pra poder montar. Depois de montado não importa.</summary>
    public float Range = 40f;

    /// <summary>Quem pode montar. É o nome da entidade, não um prefixo — montaria é coisa de um
    /// dono só, e um prefixo deixaria qualquer inimigo com nome parecido entrar no carro.</summary>
    public string RiderName = "Player";

    /// <summary>Onde o passageiro fica em relação ao centro da montaria. No cavalo, um pouco
    /// acima; no carro costuma ser o centro (com o passageiro escondido).</summary>
    public float SeatOffsetX;
    public float SeatOffsetY = -8f;

    /// <summary>Esconde o sprite do passageiro enquanto montado. Ligue no carro (o motorista some
    /// dentro), desligue no cavalo (ele aparece montado).</summary>
    public bool HideRiderWhileRiding;

    /// <summary>Onde o passageiro é largado ao sair, em relação à montaria. Zero larga em cima
    /// dela, o que costuma prender os dois num empurra-empurra de colisão.</summary>
    public float ExitOffsetX = 24f;
    public float ExitOffsetY;

    /// <summary>Tela e botão de UI que fazem o mesmo que a tecla — o botão de "entrar" do celular.</summary>
    public string InteractUiScreen = "";
    public string InteractUiButton = "";

    /// <summary>
    /// Tecla do assobio: chama a montaria de longe e ela vem sozinha até o dono. Vazio (padrão)
    /// desliga o chamado — sem isso, uma cena que já usa a mesma tecla pra outra coisa mudaria
    /// de comportamento só por atualizar a engine.
    /// </summary>
    public string CallKey = "";

    /// <summary>Alcance do assobio em pixels. 0 = o mapa inteiro.</summary>
    public float CallRange;

    /// <summary>Velocidade da vinda. Ignorado se houver <see cref="NavAgent"/> — lá quem manda é
    /// o agente, e aí a montaria contorna parede em vez de encostar nela.</summary>
    public float CallSpeed = 90f;

    /// <summary>A que distância do dono ela para de vir. Deixe maior que o <see cref="Range"/>
    /// de montar seria inútil: ela pararia longe demais pra você entrar.</summary>
    public float CallArriveDistance = 28f;

    /// <summary>Tela e botão de UI do assobio, pro celular.</summary>
    public string CallUiScreen = "";
    public string CallUiButton = "";

    /// <summary>Se está sendo montada agora.</summary>
    public bool IsRidden { get; private set; }

    /// <summary>Se está vindo atender ao chamado.</summary>
    public bool IsComing { get; private set; }

    /// <summary>Id do passageiro, quando montada.</summary>
    public int RiderId { get; private set; }

    private Vector2 Seat => new(SeatOffsetX, SeatOffsetY);

    // Estado do passageiro guardado na entrada e devolvido na saída — montar não pode deixar
    // sequelas em quem desmontou.
    private bool _riderSpriteWasVisible;
    private bool _riderColliderWasSolid;
    private bool _riderColliderWasKinematic;

    // Sem isso, o mesmo aperto de tecla que desmonta faria uma montaria vizinha (ou esta mesma,
    // noutra ordem de Update) montar de volta no mesmo frame.
    private float _cooldown;
    private float _repathTimer;

    public override void Start()
    {
        SetMovementEnabled(Entity, false);
        SetIdleAiEnabled(Entity, true);
    }

    public override void Update(float deltaTime)
    {
        if (World is null)
            return;

        _cooldown = MathF.Max(0f, _cooldown - deltaTime);

        if (IsRidden)
            KeepRiderSeated();
        else if (IsComing)
            UpdateComing(deltaTime);

        if (_cooldown > 0f)
            return;

        if (InteractPressed())
        {
            if (IsRidden)
                Dismount();
            else
                TryMount();

            return;
        }

        // O chamado só vale desmontado: em cima dela, "vir" não quer dizer nada.
        if (!IsRidden && CallPressed())
            Call();
    }

    /// <summary>
    /// Chama a montaria: ela larga o que estava fazendo e vem até o dono. Devolve se atendeu —
    /// fora de alcance, ou já montada, não atende.
    /// </summary>
    public bool Call()
    {
        if (IsRidden || World is null)
            return false;

        if (!World.TryFind(RiderName, out var rider) || rider.Get<Transform>() is not { } riderTransform)
            return false;

        if (Get<Transform>() is not { } mine)
            return false;

        if (CallRange > 0f && Vector2.Distance(riderTransform.Position, mine.Position) > CallRange)
            return false;

        IsComing = true;
        _cooldown = 0.3f;

        // Cala a IA enquanto vem: senão o destino do pasto briga com o do chamado e ela fica
        // indo e voltando entre os dois.
        SetIdleAiEnabled(Entity, false);

        // Mas o NavAgent volta a ligar: aqui ele não é "a IA dela decidindo pra onde ir", é o
        // meio de transporte que o chamado usa. Desligado junto, a montaria recebia o destino e
        // ficava parada pra sempre esperando alguém movê-la.
        if (Get<NavAgent>() is { } agent)
            agent.Enabled = true;

        _repathTimer = 0f;
        return true;
    }

    /// <summary>Cancela a vinda e devolve a montaria ao que fazia.</summary>
    public void StopComing()
    {
        if (!IsComing)
            return;

        IsComing = false;
        Get<NavAgent>()?.Stop();
        SetIdleAiEnabled(Entity, true);
    }

    private void UpdateComing(float deltaTime)
    {
        if (World is null || Get<Transform>() is not { } mine)
            return;

        if (!World.TryFind(RiderName, out var rider) || rider.Get<Transform>() is not { } riderTransform)
        {
            StopComing();   // dono sumiu da cena: para de vir em vez de andar pro último ponto
            return;
        }

        var goal = riderTransform.Position;

        if (Vector2.Distance(mine.Position, goal) <= CallArriveDistance)
        {
            StopComing();
            return;
        }

        if (Get<NavAgent>() is { } agent)
        {
            // Reaponta de tempos em tempos: o dono anda enquanto ela vem, e recalcular todo
            // frame gastaria A* à toa.
            _repathTimer -= deltaTime;
            if (_repathTimer <= 0f)
            {
                _repathTimer = 0.25f;
                agent.SetTarget(goal);
            }

            return;
        }

        var delta = goal - mine.Position;
        float distance = delta.Length();
        if (distance <= 0.0001f)
            return;

        var direction = delta / distance;
        mine.Position += direction * MathF.Min(CallSpeed * deltaTime, distance);

        if (MathF.Abs(direction.X) > 0.001f && Get<SpriteRenderer>() is { } sprite)
            sprite.FlipX = direction.X < 0f;
    }

    private bool CallPressed()
    {
        var input = World?.Input;

        if (CallKey.Length > 0 && input is not null
            && Enum.TryParse<Key>(CallKey, ignoreCase: true, out var key)
            && input.WasKeyPressed(key))
            return true;

        return CallUiButton.Length > 0
            && World?.UI?.Find<UiButton>(CallUiScreen, CallUiButton) is { Clicked: true };
    }

    public override void OnDestroy()
    {
        // Montaria destruída com alguém em cima (explodiu, saiu da cena) não pode deixar o
        // jogador invisível e sem controle pra sempre.
        if (IsRidden)
            Dismount();
    }

    /// <summary>Monta se o passageiro estiver por perto e livre. Devolve se conseguiu.</summary>
    public bool TryMount()
    {
        if (IsRidden || World is null)
            return false;

        if (!World.TryFind(RiderName, out var rider) || rider.Get<Transform>() is not { } riderTransform)
            return false;

        if (Get<Transform>() is not { } mine)
            return false;

        if (Vector2.Distance(riderTransform.Position, mine.Position) > Range)
            return false;

        // Já está em cima de outra coisa: sair de um cavalo direto pra dentro de um carro sem
        // desmontar deixaria o primeiro achando que ainda tem dono.
        if (IsRiderBusy(rider.Id))
            return false;

        IsRidden = true;
        IsComing = false;      // chegou: o chamado cumpriu o papel
        RiderId = rider.Id;
        _cooldown = 0.3f;

        SetMovementEnabled(rider, false);
        SetMovementEnabled(Entity, true);

        // Cala a IA da montaria: sem isto o cavalo tenta continuar pastando (ou patrulhando)
        // enquanto você o cavalga, e os dois movimentos brigam pelo mesmo Transform.
        SetIdleAiEnabled(Entity, false);

        if (rider.Get<SpriteRenderer>() is { } sprite)
        {
            _riderSpriteWasVisible = sprite.Visible;
            if (HideRiderWhileRiding)
                sprite.Visible = false;
        }

        if (rider.Get<Collider>() is { } collider)
        {
            // Passageiro colado no assento com colisor ativo empurraria a própria montaria.
            _riderColliderWasSolid = collider.IsSolid;
            _riderColliderWasKinematic = collider.IsKinematic;
            collider.IsSolid = false;
            collider.IsKinematic = true;
        }

        KeepRiderSeated();
        return true;
    }

    /// <summary>Desce o passageiro e devolve o controle. Seguro de chamar desmontado.</summary>
    public void Dismount()
    {
        if (!IsRidden || World is null)
            return;

        IsRidden = false;
        _cooldown = 0.3f;

        SetMovementEnabled(Entity, false);
        SetIdleAiEnabled(Entity, true);

        if (!World.IsAlive(RiderId))
            return;

        var rider = World.GetEntity(RiderId);
        SetMovementEnabled(rider, true);

        if (rider.Get<SpriteRenderer>() is { } sprite)
            sprite.Visible = _riderSpriteWasVisible;

        if (rider.Get<Collider>() is { } collider)
        {
            collider.IsSolid = _riderColliderWasSolid;
            collider.IsKinematic = _riderColliderWasKinematic;
        }

        if (Get<Transform>() is { } mine && rider.Get<Transform>() is { } riderTransform)
            riderTransform.Position = mine.Position + new Vector2(ExitOffsetX, ExitOffsetY);
    }

    private void KeepRiderSeated()
    {
        if (World is null || !World.IsAlive(RiderId))
        {
            // Passageiro morreu montado: a montaria fica livre em vez de guardar um dono fantasma.
            IsRidden = false;
            SetMovementEnabled(Entity, false);
            SetIdleAiEnabled(Entity, true);
            return;
        }

        if (Get<Transform>() is { } mine
            && World.GetEntity(RiderId).Get<Transform>() is { } riderTransform)
            riderTransform.Position = mine.Position + Seat;
    }

    /// <summary>Outra montaria já está com este passageiro?</summary>
    private bool IsRiderBusy(int riderId)
    {
        foreach (var (entity, other) in World!.Query<Rideable>())
        {
            if (entity.Id != Entity.Id && other.IsRidden && other.RiderId == riderId)
                return true;
        }

        return false;
    }

    private bool InteractPressed()
    {
        var input = World?.Input;

        if (InteractKey.Length > 0 && input is not null
            && Enum.TryParse<Key>(InteractKey, ignoreCase: true, out var key)
            && input.WasKeyPressed(key))
            return true;

        return InteractUiButton.Length > 0
            && World?.UI?.Find<UiButton>(InteractUiScreen, InteractUiButton) is { Clicked: true };
    }

    /// <summary>
    /// Liga/desliga os controladores de movimento de uma entidade. Os três de uma vez porque
    /// montaria não sabe (nem precisa saber) se é cavalo com TopDownController, carro com
    /// VehicleController ou mecha de plataforma — e um jogo pode ter os três.
    /// </summary>
    private static void SetMovementEnabled(Entity entity, bool enabled)
    {
        if (entity.Get<TopDownController>() is { } topDown) topDown.Enabled = enabled;
        if (entity.Get<PlatformerController>() is { } platformer) platformer.Enabled = enabled;
        if (entity.Get<VehicleController>() is { } vehicle) vehicle.Enabled = enabled;
    }

    /// <summary>
    /// Liga/desliga o que a montaria faz SOZINHA quando ninguém está em cima — pastar, patrulhar,
    /// perseguir. É o oposto exato do controlador: um vale montado, o outro vale solto, e os dois
    /// juntos disputariam o mesmo Transform.
    ///
    /// <para>Só o <see cref="Wander"/> recebe <c>Halt()</c> além do Enabled, porque ele guarda um
    /// destino sorteado: religar sem parar antes faria o cavalo retomar a caminhada de meia hora
    /// atrás, saindo em linha reta de onde você desmontou.</para>
    /// </summary>
    private static void SetIdleAiEnabled(Entity entity, bool enabled)
    {
        if (entity.Get<PatrolPath>() is { } patrol) patrol.Enabled = enabled;
        if (entity.Get<NavAgent>() is { } agent) agent.Enabled = enabled;

        if (entity.Get<Wander>() is { } wander)
        {
            wander.Enabled = enabled;

            if (enabled)
            {
                // Passa a pastar onde ela PAROU. Sem isto, uma montaria que você cavalgou pra
                // outra ponta do mapa (ou que veio atendendo ao assobio) sairia andando de volta
                // pro ponto de nascimento, atravessando tudo pra pastar onde ninguém está.
                wander.ResetHome();
            }

            wander.Halt();
        }
    }
}
