using System;

namespace WinVora
{
    internal static class NotificationService
    {
        private static bool _notificationsUnavailable;

        public static void ShowUpdateSummary(
            int successful,
            int failed,
            int cancelled,
            int restartRequired,
            int unverified)
        {
            if (_notificationsUnavailable) return;

            bool en = Localization.CurrentLanguage == "en";
            string body = en
                ? $"Successful: {successful} · Failed: {failed} · Not confirmed: {unverified} · Cancelled: {cancelled} · Restart required: {restartRequired}"
                : $"Erfolgreich: {successful} · Fehlgeschlagen: {failed} · Nicht bestätigt: {unverified} · Abgebrochen: {cancelled} · Neustart erforderlich: {restartRequired}";

            try
            {
                var manager = Microsoft.Windows.AppNotifications.AppNotificationManager.Default;
                manager.Register();
                var notification = new Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder()
                    .AddText(en ? "WinVora updates completed" : "WinVora-Updates abgeschlossen")
                    .AddText(body)
                    .BuildNotification();
                manager.Show(notification);
            }
            catch (Exception ex)
            {
                _notificationsUnavailable = true;
                // Auf einzelnen unpackaged Debug-/Runtime-Installationen fehlt
                // die Windows-App-SDK-Ressourcen-DLL. Die Meldung soll den
                // Verlauf nicht bei jedem Update erneut überfluten.
                Logger.LogErrorOnce("Windows-Benachrichtigung konnte nicht angezeigt werden", ex);
            }
        }
    }
}
