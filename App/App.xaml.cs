using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
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
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Sonderfall: Dieser Prozess wurde nur gestartet, um EINE oder
            // mehrere Admin-pflichtige Storage-Kategorien elevated zu löschen
            // (per "runas" aus der normalen, nicht elevierten App heraus).
            // Dann läuft keine UI, nur die Löschung, danach beendet sich der
            // Prozess sofort mit einem Exitcode (0 = alles erfolgreich).
            var cmdArgs = Environment.GetCommandLineArgs();
            var deleteIndex = Array.IndexOf(cmdArgs, "--delete-storage");

            if (deleteIndex >= 0 && deleteIndex + 1 < cmdArgs.Length)
            {
                var keys = cmdArgs[deleteIndex + 1].Split(';', StringSplitOptions.RemoveEmptyEntries);

                // BUGFIX (Deadlock): ".GetAwaiter().GetResult()" direkt auf dem
                // UI-Thread aufzurufen, während DeleteCategoryAsync() intern
                // "await Task.Run(...)" nutzt, blockiert für immer - die
                // Fortsetzung versucht, auf den UI-Thread zurückzuspringen,
                // der aber gerade blockiert wartet. Fix: die komplette Schleife
                // läuft jetzt selbst in einem Task.Run, dessen Continuations
                // keinen UI-Sync-Context mehr eingefangen haben.
                bool allSucceeded = Task.Run(() =>
                {
                    var allCategories = StorageService.GetCategoryDefinitions();
                    bool succeeded = true;

                    foreach (var key in keys)
                    {
                        var category = allCategories.FirstOrDefault(c => c.Key == key);
                        if (category == null)
                        {
                            Logger.Log($"Elevierte Löschung: Kategorie '{key}' nicht gefunden.");
                            succeeded = false;
                            continue;
                        }

                        var (success, message) = StorageService.DeleteCategoryAsync(category).GetAwaiter().GetResult();
                        Logger.Log($"Elevierte Löschung '{category.Name}': {(success ? "OK" : "FEHLER")} - {message}");
                        if (!success) succeeded = false;
                    }

                    return succeeded;
                }).GetAwaiter().GetResult();

                Environment.Exit(allSucceeded ? 0 : 1);
                return;
            }

            _window = new MainWindow();
            _window.Activate();
        }
    }
}