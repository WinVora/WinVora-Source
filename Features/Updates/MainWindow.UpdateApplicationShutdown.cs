using Microsoft.UI.Xaml.Controls;
using System.Threading;
using System.Threading.Tasks;

namespace WinVora
{
    internal enum ApplicationCloseDecision { Closed, Cancelled, Failed }

    public sealed partial class MainWindow
    {
        private async Task<ApplicationCloseDecision> EnsureApplicationClosedForUpdateAsync(
            string packageId,
            string packageName,
            CancellationToken cancellationToken)
        {
            ApplicationShutdownState state = await UpdateApplicationShutdownService
                .TryCloseGracefullyForUpdateAsync(packageId, packageName, cancellationToken);
            if (state == ApplicationShutdownState.Closed) return ApplicationCloseDecision.Closed;
            if (state == ApplicationShutdownState.Failed) return ApplicationCloseDecision.Failed;

            var dialog = CommonUiBuilder.CreateConfirmation(
                RootGrid.XamlRoot,
                Localization.T("Update.ForceCloseTitle"),
                Localization.F("Update.ForceCloseMessage", packageName),
                Localization.T("Update.ForceCloseAction"),
                Localization.T("Common.Cancel"));
            dialog.DefaultButton = ContentDialogButton.Close;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return ApplicationCloseDecision.Cancelled;

            return UpdateApplicationShutdownService.ForceCloseForUpdate(packageId, packageName)
                ? ApplicationCloseDecision.Closed
                : ApplicationCloseDecision.Failed;
        }
    }
}
