using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace Bichinhos.Droid;

/// <summary>
/// As notificações do Android para o jogo: canal, permissão e o alarme que acorda o
/// <see cref="LembreteReceiver"/> com o app fechado.
///
/// <para>O alarme é <c>SetAndAllowWhileIdle</c>: o Android pode atrasá-lo alguns minutos para
/// poupar bateria, mas não exige a permissão de "alarme exato" (que o Android 14 nega por padrão).
/// Para "ele está com fome" meia hora de folga não faz diferença. O alarme se perde ao reiniciar
/// o celular; ele volta a ser agendado na próxima vez que o app sair da tela.</para>
/// </summary>
public sealed class PlataformaAndroid : IPlataforma
{
    public const string Canal = "cuidados";

    private const int CodigoPermissao = 4101;
    private const int CodigoAlarme = 7001;
    internal const int IdNotificacao = 1001;

    private readonly Activity _activity;

    public PlataformaAndroid(Activity activity)
    {
        _activity = activity;
        CriarCanal(activity);
    }

    /// <summary>No Android 8+ toda notificação precisa de canal. Criar de novo é inofensivo, então
    /// o receiver também chama (ele pode rodar com o app morto).</summary>
    internal static void CriarCanal(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;

        var canal = new NotificationChannel(Canal, "Cuidados com o bichinho", NotificationImportance.Default)
        {
            Description = "Avisos de que o bichinho precisa de você.",
        };
        (context.GetSystemService(Context.NotificationService) as NotificationManager)?.CreateNotificationChannel(canal);
    }

    public void PedirPermissaoDeNotificacao()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return;

        _activity.RunOnUiThread(() =>
        {
            if (_activity.CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
                _activity.RequestPermissions([Manifest.Permission.PostNotifications], CodigoPermissao);
        });
    }

    public void AgendarLembrete(DateTime quandoLocal, string titulo, string texto)
    {
        if (_activity.GetSystemService(Context.AlarmService) is not AlarmManager alarmes)
            return;

        long ms = new DateTimeOffset(quandoLocal).ToUnixTimeMilliseconds();
        alarmes.SetAndAllowWhileIdle(AlarmType.RtcWakeup, ms, Intencao(_activity, titulo, texto));
    }

    public void CancelarLembretes()
    {
        (_activity.GetSystemService(Context.AlarmService) as AlarmManager)?.Cancel(Intencao(_activity, "", ""));
        (_activity.GetSystemService(Context.NotificationService) as NotificationManager)?.CancelAll();
    }

    /// <summary>A mesma intenção para agendar e cancelar: o Android as considera iguais pelo
    /// destino e pelo código, e ignora os extras (por isso cancelar pode mandar texto vazio).</summary>
    private static PendingIntent Intencao(Context contexto, string titulo, string texto)
    {
        var intent = new Intent(contexto, typeof(LembreteReceiver));
        intent.PutExtra(LembreteReceiver.ExtraTitulo, titulo);
        intent.PutExtra(LembreteReceiver.ExtraTexto, texto);
        return PendingIntent.GetBroadcast(contexto, CodigoAlarme, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }
}
