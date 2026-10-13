using Android.App;
using Android.Content;
using Android.OS;

namespace Bichinhos.Droid;

/// <summary>
/// Acorda quando o alarme dispara (com o app fechado) e mostra a notificação. Tocar nela abre o
/// jogo. O atributo faz o .NET Android registrar o receiver no manifesto sozinho.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class LembreteReceiver : BroadcastReceiver
{
    internal const string ExtraTitulo = "titulo";
    internal const string ExtraTexto = "texto";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
            return;

        string titulo = intent.GetStringExtra(ExtraTitulo) ?? "Seu bichinho precisa de você";
        string texto = intent.GetStringExtra(ExtraTexto) ?? "Volte para cuidar dele.";

        PlataformaAndroid.CriarCanal(context);

        PendingIntent? abrir = null;
        if (context.PackageManager?.GetLaunchIntentForPackage(context.PackageName ?? "") is { } abrirJogo)
        {
            abrir = PendingIntent.GetActivity(context, 0, abrirJogo,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        }

        // A notificação exige um ícone pequeno. Usa o do app e, se o app não tiver, um do sistema:
        // sem ícone válido o Android derruba a notificação em silêncio.
        int icone = context.ApplicationInfo?.Icon ?? 0;
        if (icone == 0)
            icone = Android.Resource.Drawable.IcDialogInfo;

#pragma warning disable CA1422 // construtor sem canal só vale antes do Android 8 (minSdk é 24)
        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(context, PlataformaAndroid.Canal)
            : new Notification.Builder(context);
#pragma warning restore CA1422

        builder.SetSmallIcon(icone)
            .SetContentTitle(titulo)
            .SetContentText(texto)
            .SetAutoCancel(true)
            .SetContentIntent(abrir);

        (context.GetSystemService(Context.NotificationService) as NotificationManager)
            ?.Notify(PlataformaAndroid.IdNotificacao, builder.Build());
    }
}
