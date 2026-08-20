using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Windows.AppLifecycle;
using WinAppInstance = Microsoft.Windows.AppLifecycle.AppInstance;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinVora
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        private WinAppInstance? _mainInstance;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();

            // Fängt Fehler ab, die sonst zu einem stillen Absturz ohne jede
            // Erklärung führen würden - wichtig gerade in der Testphase.
            this.UnhandledException += (_, e) =>
            {
                Logger.LogError("UnhandledException", e.Exception);
                // e.Handled bewusst nicht gesetzt: die App soll sich weiterhin
                // wie gewohnt verhalten (nicht "verschlucken"), nur eben geloggt.
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    Logger.LogError("AppDomain.UnhandledException", ex);
            };

            Logger.Log("App gestartet.");
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var cmdArgs = Environment.GetCommandLineArgs();
            if (ElevatedActionService.IsHelperInvocation(cmdArgs))
            {
                int exitCode = Task.Run(() => ElevatedActionService.ExecuteHelperAsync(cmdArgs))
                    .GetAwaiter().GetResult();
                Environment.Exit(exitCode);
                return;
            }

            _mainInstance = WinAppInstance.FindOrRegisterForKey("WinVora.Main");
            if (!_mainInstance.IsCurrent)
            {
                await _mainInstance.RedirectActivationToAsync(
                    WinAppInstance.GetCurrent().GetActivatedEventArgs());
                Environment.Exit(0);
                return;
            }

            _mainInstance.Activated += MainInstance_Activated;

            _window = new MainWindow();
            _window.Activate();
        }

        private void MainInstance_Activated(object? sender, AppActivationArguments args)
        {
            Window? window = _window;
            if (window == null) return;
            window.DispatcherQueue.TryEnqueue(() =>
            {
                window.AppWindow.Show();
                window.Activate();
            });
        }
    }
}
