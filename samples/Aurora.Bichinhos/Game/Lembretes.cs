namespace Bichinhos;

/// <summary>
/// O que o jogo precisa pedir ao sistema operacional e que não existe no PC: avisos com o jogo
/// fechado. Só o Android implementa (ver <c>Bichinhos.Droid.PlataformaAndroid</c>); no desktop
/// <see cref="BichinhosGame.Plataforma"/> fica nula e nada acontece.
/// </summary>
public interface IPlataforma
{
    /// <summary>Pede ao jogador a permissão de mostrar notificações (Android 13+). Pode ser
    /// chamada de qualquer thread.</summary>
    void PedirPermissaoDeNotificacao();

    /// <summary>Agenda UMA notificação, trocando a que já estava agendada.</summary>
    void AgendarLembrete(DateTime quandoLocal, string titulo, string texto);

    /// <summary>Desfaz o agendamento e limpa a notificação que já tenha aparecido.</summary>
    void CancelarLembretes();
}

/// <summary>
/// Quando e o que avisar quando o bichinho fica com fome. É regra pura (sem Android) para dar
/// para testar: o celular só recebe "avise nesta hora com este texto".
///
/// <para>Regras: avisa quando a fome cruza <see cref="LimiteFome"/> (o mesmo ponto em que o
/// balão de comida aparece na casa); se o jogador sai com o bicho já com fome, lembra uma hora
/// depois; nunca avisa de madrugada (22h às 8h), e sim às 8h; e não agenda aviso para mais de 24 h,
/// o teto do tempo fora que o jogo conta.</para>
/// </summary>
public static class Lembretes
{
    public const float LimiteFome = 30f;

    public static readonly TimeSpan SilencioComeca = new(22, 0, 0);
    public static readonly TimeSpan SilencioTermina = new(8, 0, 0);

    /// <summary>Se já está com fome ao sair, quanto esperar antes de lembrar.</summary>
    public static readonly TimeSpan JaComFome = TimeSpan.FromHours(1);

    /// <summary>Antes disso o aviso só atrapalha (acabou de sair do jogo).</summary>
    public static readonly TimeSpan Minimo = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan Maximo = TimeSpan.FromHours(Progresso.MaxHorasFora);

    /// <summary>Hora (local) do aviso de fome, ou null se não vale a pena agendar.</summary>
    public static DateTime? QuandoAvisarFome(Bicho bicho, DateTime agoraLocal)
    {
        double horas = bicho.HorasAteFome(LimiteFome);
        var espera = horas <= 0 ? JaComFome : TimeSpan.FromHours(horas);

        if (espera < Minimo)
            espera = Minimo;
        if (espera > Maximo)
            return null;

        return AjustarParaHorarioSilencioso(agoraLocal + espera);
    }

    /// <summary>Joga para as 8h quem cairia de madrugada.</summary>
    public static DateTime AjustarParaHorarioSilencioso(DateTime quando)
    {
        var hora = quando.TimeOfDay;
        if (hora >= SilencioComeca)
            return quando.Date.AddDays(1) + SilencioTermina;
        if (hora < SilencioTermina)
            return quando.Date + SilencioTermina;
        return quando;
    }

    public static (string Titulo, string Texto) TextoFome(Bicho bicho)
        => ($"{bicho.Nome} está com fome!", "Volte para dar comida pra ele.");
}
