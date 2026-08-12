using System;

namespace WinVora
{
    internal static class NotificationService
    {
        public static void ShowUpdateSummary(int successful, int failed, int cancelled, int restartRequired)
        {
            bool en = Localization.CurrentLanguage == "en";
            string body = en
                ? $"Successful: {successful} · Failed: {failed} · Cancelled: {cancelled} · Restart required: {restartRequired}"
                : $"Erfolgreich: {successful} · Fehlgeschlagen: {failed} · Abgebrochen: {cancelled} · Neustart erforderlich: {restartRequired}";

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
                Logger.LogError("Windows-Benachrichtigung konnte nicht angezeigt werden", ex);
            }
        }
    }
}
