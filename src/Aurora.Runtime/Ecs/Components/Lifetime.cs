namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Destrói a entidade sozinha: por tempo, ou quando a animação sem loop termina. É o que
/// impede efeito instanciado (corte, explosão, faísca, número de dano) de virar lixo parado no
/// cenário pra sempre.
///
/// <para>As duas condições valem juntas — o que vier primeiro mata. Com
/// <see cref="DestroyOnAnimationEnd"/> ligado, <see cref="Seconds"/> vira rede de segurança pro
/// caso do clipe estar em loop ou da entidade não ter <see cref="Animator"/>.</para>
/// </summary>
public sealed class Lifetime : Behavior
{
    /// <summary>Segundos até se destruir. 0 = sem limite de tempo, o que só faz sentido junto
    /// com <see cref="DestroyOnAnimationEnd"/>.</summary>
    public float Seconds = 2f;

    /// <summary>Destrói quando o <see cref="Animator"/> marca IsFinished — o último quadro de um
    /// clipe com <c>Loop = false</c>. Sem isso o corte fica piscando pra sempre.</summary>
    public bool DestroyOnAnimationEnd;

    /// <summary>Idade em segundos desde que nasceu.</summary>
    public float Age { get; private set; }

    public override void Update(float deltaTime)
    {
        Age += deltaTime;

        bool byTime = Seconds > 0f && Age >= Seconds;
        bool byAnimation = DestroyOnAnimationEnd && Get<Animator>() is { IsFinished: true };

        if (byTime || byAnimation)
            Entity.Destroy();
    }
}
