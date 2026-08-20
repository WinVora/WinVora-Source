using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        private int _visibleChangelogCardCount;

        private void ChangelogButton_Click(object sender, RoutedEventArgs e)
        {
            if (_changelogWindow != null)
            {
                _changelogWindow.Activate();
                WindowActivationService.ShowOwnedInFront(this, _changelogWindow);
                return;
            }

            _changelogWindow = new Window
            {
                Title = Localization.T("Changelog.WindowTitle")
            };
            var changelogWindow = _changelogWindow;
            changelogWindow.Closed += (_, __) =>
            {
                SaveSecondaryWindowPlacement(changelogWindow, settingsWindow: false);
                _changelogWindow = null;
            };

            var root = new Grid
            {
                Background = (SolidColorBrush)RootGrid.Resources["AppRootBackgroundBrush"],
                UseLayoutRounding = true
            };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RequestedTheme = _isDarkTheme ? ElementTheme.Dark : ElementTheme.Light;

            var panel = new StackPanel
            {
                Spacing = 14
            };

            var changelogHeader = new Grid { ColumnSpacing = 14, Margin = new Thickness(0, 2, 0, 6) };
            changelogHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            changelogHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            changelogHeader.Children.Add(new Border
            {
                Width = 46,
                Height = 46,
                CornerRadius = new CornerRadius(13),
                Background = (SolidColorBrush)RootGrid.Resources["AppAccentOverlay20"],
                Child = new FontIcon { Glyph = "\uE81C", FontSize = 21, Foreground = (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"] }
            });
            var changelogHeading = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            changelogHeading.Children.Add(new TextBlock { Text = Localization.T("Changelog.WindowTitle"), FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            changelogHeading.Children.Add(new TextBlock
            {
                Text = Localization.CurrentLanguage == "en" ? "What's new in WinVora" : "Das ist neu in WinVora",
                FontSize = 12,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"]
            });
            Grid.SetColumn(changelogHeading, 1);
            changelogHeader.Children.Add(changelogHeading);
            panel.Children.Add(changelogHeader);
            _visibleChangelogCardCount = 0;

            panel.Children.Add(MakeChangelogCard(
    $"Version {CurrentVersion}",
    "• Sicherheit: Systembefehle und geschützte Bereinigungen prüfen jetzt zuverlässig Timeout, Abbruch und Rückgabecode\n" +
    "• Sicherheit: Firewall, Defender und Windows-Aktivierung werden genauer und unabhängig voneinander bewertet\n" +
    "• Zuverlässigkeit: WinGet-Fehler werden auch dann erkannt, wenn keine technische Fehlermeldung ausgegeben wird\n" +
    "• Zuverlässigkeit: Updates, Speicherbereinigung und Eigenupdates lassen sich kontrolliert abbrechen\n" +
    "• Zuverlässigkeit: Eine zweite gestartete WinVora-Instanz führt zurück zum bereits geöffneten Fenster\n" +
    "• Leistung: Hardwarewerte werden vollständig im Hintergrund gemessen und blockieren die Oberfläche nicht mehr\n" +
    "• Leistung: PC-Check und Systemabfragen lesen nur noch die tatsächlich benötigten Daten\n" +
    "• Verbesserungen: Startanzeige und Fortschritt entsprechen jetzt den wirklich ausgeführten Startphasen\n" +
    "• Bugfixes: Desktop-PCs ohne Akku erzeugen keine falsche Akku-Fehlermeldung mehr\n" +
    "• Qualität: Automatische Tests prüfen Versionsvergleich, Sicherheit, WinGet, Speicherpfade und Diagnose-Anonymisierung",
    "• Security: System commands and protected cleanup now reliably verify timeouts, cancellation and exit codes\n" +
    "• Security: Firewall, Defender and Windows activation are evaluated more accurately and independently\n" +
    "• Reliability: WinGet errors are detected even when no technical error message is returned\n" +
    "• Reliability: Updates, storage cleanup and WinVora self-updates can be cancelled cleanly\n" +
    "• Reliability: Starting WinVora twice now returns to the already open window\n" +
    "• Performance: Hardware values are measured fully in the background without blocking the interface\n" +
    "• Performance: PC Check and system queries now read only the data they actually need\n" +
    "• Improvements: Startup status and progress now match the phases that are actually performed\n" +
    "• Bug fixes: Desktop PCs without a battery no longer show a false battery error\n" +
    "• Quality: Automated tests cover version comparison, security, WinGet, storage paths and diagnostic anonymization"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.8.5-beta.2",
    "• Beta: In den Einstellungen kann zwischen stabilen und Beta-Updates gewechselt werden\n" +
    "• Beta: Sichtbare Kennzeichnung, Updatekanal-Anzeige und vorbereitete Problemmeldung\n" +
    "• Übersetzung: Dashboard, Verlauf, Autostart, Dialoge und Benachrichtigungen sind vollständig auf Deutsch und Englisch verfügbar\n" +
    "• Qualität: Ein automatischer Test erkennt fehlende Übersetzungen, feste XAML-Texte und ungeschützte dynamische Meldungen\n" +
    "• Autostart: Status, Dateipfade, Signaturen und Rückmeldungen wechseln zuverlässig mit der Sprache\n" +
    "• Ladebildschirm: Die violette Wabenanimation leuchtet im Dunkelmodus jetzt genauso deutlich wie im Hellmodus\n" +
    "• Code: Doppelte WinGet-Auswertung und nicht verwendete UI-Reste wurden entfernt\n" +
    "• Sicherheit: Einstellungen werden vor Beta-Updates und der Rückkehr zu Stable automatisch gesichert\n" +
    "• Diagnose: Supportberichte werden anonymisiert als ZIP gespeichert\n" +
    "• Speicher: Eigene Ordner können auf ungewöhnliches Wachstum überwacht werden\n" +
    "• Updates: Bei Fehlern führt ein neuer Hinweis zu Reparatur- und Rollback-Optionen\n" +
    "• Neue Funktionen: Dashboard-Karten lassen sich per Drag-and-drop anordnen\n" +
    "• Neue Funktionen: Speicheranalyse sortiert nach Größe, Name oder Risiko und erklärt die Einstufung\n" +
    "• Verbesserungen: Einstellungen lassen sich einzeln pro Bereich zurücksetzen und öffnen danach automatisch neu\n" +
    "• Verbesserungen: Deinstallationen zeigen eine sichtbare Prüfung mit Countdown und erneutem Prüfen\n" +
    "• Verbesserungen: Der Verlauf merkt sich geöffnete Details und bleibt dadurch übersichtlich\n" +
    "• Leistung: WinGet und Programmliste laden erst nach dem sichtbaren Hauptfenster; Programmsymbole nur bei Bedarf\n" +
    "• Sicherheit: Ausführliche Sicherheitsdetails zeigen beim Nachladen einen klaren Status\n" +
    "• Einstellungen: Gespeichert werden nur noch vom Standard abweichende Werte\n" +
    "• Zuverlässigkeit: Laufende Aufgaben werden beim Schließen einheitlich erklärt und abgebrochen\n" +
    "• Benachrichtigungen: Falls Windows keine Meldung anzeigen kann, erscheint ein Hinweis direkt in WinVora\n" +
    "• Oberfläche: Skeleton-Lader, Tooltips, Sidebar-Scrollhinweise und kleine Fenster wurden verfeinert\n" +
    "• Barrierefreiheit: Reduzierte Bewegung stoppt Skeleton-Animationen; Tastatur- und Fokuszustände wurden vereinheitlicht\n" +
    "• Bugfixes: Titelleisten, Hellmodus-Kontraste, Systemwerte und Programmlisten wurden stabilisiert",
    "• Beta: Settings can switch between stable and beta update channels\n" +
    "• Beta: Visible badges, update-channel status and prepared issue reports\n" +
    "• Localization: Dashboard, History, Startup, dialogs and notifications are fully available in German and English\n" +
    "• Quality: An automated test detects missing translations, fixed XAML text and unprotected dynamic messages\n" +
    "• Startup: Status, file paths, signatures and feedback now switch languages reliably\n" +
    "• Loading screen: The purple honeycomb animation is now equally visible in dark and light mode\n" +
    "• Code: Duplicate WinGet parsing and unused UI remnants were removed\n" +
    "• Safety: Settings are backed up automatically before beta updates and returning to Stable\n" +
    "• Diagnostics: Anonymized support reports are saved as ZIP files\n" +
    "• Storage: Custom folders can be monitored for unusual growth\n" +
    "• Updates: Failed updates now link to repair and rollback options\n" +
    "• New features: Dashboard cards can be reordered using drag and drop\n" +
    "• New features: Storage analysis sorts by size, name or risk and explains each rating\n" +
    "• Improvements: Settings can be reset per section and reopen automatically afterwards\n" +
    "• Improvements: Uninstalls show a visible verification countdown with a retry action\n" +
    "• Improvements: History remembers expanded details while keeping the list compact\n" +
    "• Performance: WinGet and program lists load after the main window; program icons load only when needed\n" +
    "• Security: Extended security details show a clear loading state\n" +
    "• Settings: Only values that differ from the defaults are stored\n" +
    "• Reliability: Running tasks are explained and cancelled consistently when closing\n" +
    "• Notifications: WinVora shows an in-app fallback if Windows notifications are unavailable\n" +
    "• Interface: Skeleton loaders, tooltips, sidebar scroll hints and small-window layouts were refined\n" +
    "• Accessibility: Reduced motion disables skeleton animation; keyboard and focus states are consistent\n" +
    "• Bug fixes: Title bars, light-mode contrast, system values and program lists were stabilized"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.8.4.1",
    ReleaseNotes.CurrentGerman,
    ReleaseNotes.CurrentEnglish
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.8.4",
    "• Verbesserungen: Die neue Seite Veränderungen zeigt installierte, entfernte und aktualisierte Programme\n" +
    "• Verbesserungen: Autostart-Änderungen und ungewöhnliches Speicherwachstum werden erkannt\n" +
    "• Verbesserungen: Systeminformationen werden bereichsweise gespeichert und aktualisiert\n" +
    "• Verbesserungen: Programmlisten lassen sich als TXT oder CSV sichern\n" +
    "• Sicherheit: TPM, Defender, Firewall, Secure Boot und BitLocker werden unabhängig geprüft\n" +
    "• Sicherheit: Supportberichte werden vor dem Speichern angezeigt und anonymisiert\n" +
    "• Oberfläche: Zustände und Systeminformationen sind verständlicher beschriftet\n" +
    "• Bugfixes: Verlauf, TPM-Erkennung, Deinstallation und parallele Ladevorgänge wurden stabilisiert",
    "• Improvements: The new Changes page shows installed, removed and updated programs\n" +
    "• Improvements: Startup changes and unusual storage growth are detected\n" +
    "• Improvements: System information is cached and refreshed by category\n" +
    "• Improvements: Program lists can be saved as TXT or CSV\n" +
    "• Security: TPM, Defender, Firewall, Secure Boot and BitLocker are checked independently\n" +
    "• Security: Support reports are previewed and anonymized before saving\n" +
    "• Interface: States and system information use clearer wording\n" +
    "• Bug fixes: History, TPM detection, uninstall and parallel loading were stabilized"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.8.3",
    "• Verbesserungen: Update-Details zeigen Quelle, Herausgeber und weitere Programminformationen übersichtlicher\n" +
    "• Verbesserungen: Programme können dauerhaft von Updates ausgenommen und später wieder zugelassen werden\n" +
    "• Verbesserungen: Fehlgeschlagene Updates lassen sich einzeln erneut versuchen\n" +
    "• Verbesserungen: Der Verlauf bietet Suche, Datumsfilter und kopierbare Fehlerdetails\n" +
    "• Verbesserungen: Programmlisten lassen sich als TXT oder CSV exportieren\n" +
    "• Verbesserungen: Einstellungen können gesichert, importiert und aus einer Sicherung wiederhergestellt werden\n" +
    "• Sicherheit: Downloads, Papierkorb und Browserdaten besitzen eigene Schutzoptionen\n" +
    "• Sicherheit: Diagnoseberichte werden vor dem Speichern angezeigt und persönliche Angaben anonymisiert\n" +
    "• Systeminfo: Akkuzustand, BIOS, TPM, Secure Boot und Windows-Aktivierung wurden ergänzt\n" +
    "• Oberfläche: Ladephasen und Fortschritt beim Start werden verständlicher angezeigt\n" +
    "• Oberfläche: Das Dashboard passt sich kleinen Fenstern besser an\n" +
    "• Bugfixes: Sicherheitsstatus unterscheidet aktiv, nicht prüfbar und tatsächliche Probleme zuverlässiger\n" +
    "• Bugfixes: Laufende Hintergrundabfragen werden beim Schließen sauber beendet\n" +
    "• Bugfixes: TPM-Erkennung, Verlauf, Deinstallation und parallele Ladevorgänge wurden stabilisiert",
    "• Improvements: Update details present source, publisher and additional program information more clearly\n" +
    "• Improvements: Programs can be permanently excluded from updates and allowed again later\n" +
    "• Improvements: Failed updates can be retried individually\n" +
    "• Improvements: History includes search, date filters and copyable error details\n" +
    "• Improvements: Program lists can be exported as TXT or CSV\n" +
    "• Improvements: Settings can be exported, imported and restored from backups\n" +
    "• Safety: Downloads, Recycle Bin and browser data have separate protection options\n" +
    "• Safety: Diagnostic reports are previewed and personal details are anonymized\n" +
    "• System info: Battery health, BIOS, TPM, Secure Boot and Windows activation were added\n" +
    "• Interface: Startup phases and progress are explained more clearly\n" +
    "• Interface: The dashboard adapts better to small windows\n" +
    "• Bug fixes: Security status now distinguishes active, unverifiable and actual problems more reliably\n" +
    "• Bug fixes: Running background checks are cancelled cleanly when closing\n" +
    "• Bug fixes: TPM detection, history, uninstall and parallel loading were stabilized"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.8.2",
    "• Verbesserungen: Programm-Updates zeigen Download-, Installations- und Abschlussphase verständlicher\n" +
    "• Verbesserungen: Updateverlauf speichert Programm, Versionen, Zeitpunkt und Ergebnis\n" +
    "• Verbesserungen: Abschlussberichte unterscheiden erfolgreich, fehlgeschlagen, abgebrochen und Neustart erforderlich\n" +
    "• Sicherheit: Neustarts durch Installer werden nach Möglichkeit unterdrückt und klar angekündigt\n" +
    "• Oberfläche: Dashboard, Navigation, Systeminfo, Dateien und Deinstallation wurden vereinheitlicht\n" +
    "• Bugfixes: Ladebildschirm, Fenstergröße, Suchfelder und Deinstallationsstatus wurden stabilisiert",
    "• Improvements: Program updates explain download, installation and completion phases more clearly\n" +
    "• Improvements: Update history stores program, versions, time and result\n" +
    "• Improvements: Completion reports distinguish success, failure, cancellation and restart required\n" +
    "• Safety: Installer restarts are suppressed where possible and clearly announced\n" +
    "• Interface: Dashboard, navigation, system info, files and uninstall were unified\n" +
    "• Bug fixes: Loading screen, window sizing, search fields and uninstall status were stabilized"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.8.1",
    "• Neue Suche: Verfügbare Programm-Updates lassen sich jetzt schnell nach\n" +
    "  Name oder Paket durchsuchen\n" +
    "• Suche zeigt Trefferzahlen und einen freundlichen Hinweis, wenn nichts passt\n" +
    "• Der Update-Button zeigt direkt, wie viele Programme ausgewählt sind\n" +
    "• Nach abgeschlossenen Programm-Updates wird die Liste automatisch erneuert\n" +
    "• Die Programmsuche beim Deinstallieren zeigt jetzt ebenfalls Trefferzahlen\n" +
    "• Programmgrößen werden deutlich häufiger angezeigt statt nur als N/A\n" +
    "• Herausgeber, Größe und Ladehinweise auf der Winget-Seite wechseln jetzt\n" +
    "  korrekt zwischen Deutsch und Englisch\n" +
    "• Der komplette WinVora-Updatebereich ist jetzt auch auf Englisch verfügbar\n" +
    "• Heruntergeladene WinVora-Updates werden vor der Installation geprüft\n" +
    "• Beschädigte oder unvollständige Downloads werden automatisch entfernt\n" +
    "• Die Bereinigung geschützter Windows-Dateien wurde sicherer gemacht\n" +
    "• Mehrfachklicks starten keine doppelten Lade- oder Bereinigungsvorgänge mehr\n" +
    "• WinVora merkt sich Größe und Position des Hauptfensters\n" +
    "• Neue Tastenkürzel: Strg+F für die Suche und Strg+R zum Aktualisieren\n" +
    "• Verbesserte Tastaturbedienung und Beschriftungen für Bildschirmleser\n" +
    "• Versionsanzeige in der App und im Installer bleibt automatisch gleich\n" +
    "• Fehler beim Laden oder Speichern von Einstellungen sind leichter zu finden",
    "• New search: quickly filter available program updates by name or package\n" +
    "• Search now shows result counts and a friendly message when nothing matches\n" +
    "• The update button directly shows how many programs are selected\n" +
    "• The list refreshes automatically after program updates finish\n" +
    "• Program search on the uninstall page now also shows result counts\n" +
    "• Program sizes are now shown much more often instead of only displaying N/A\n" +
    "• Publisher, size and loading text on the Winget page now switch correctly\n" +
    "  between German and English\n" +
    "• The complete WinVora update section is now available in English\n" +
    "• Downloaded WinVora updates are checked before installation\n" +
    "• Damaged or incomplete downloads are removed automatically\n" +
    "• Cleanup of protected Windows files is now safer\n" +
    "• Repeated clicks no longer start duplicate loading or cleanup operations\n" +
    "• WinVora remembers the main window size and position\n" +
    "• New shortcuts: Ctrl+F for search and Ctrl+R to refresh\n" +
    "• Improved keyboard navigation and screen reader labels\n" +
    "• Version numbers in the app and installer now always stay in sync\n" +
    "• Problems loading or saving settings are easier to diagnose"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.8.0",
    "• Update-Bereich in den Einstellungen jetzt ganz oben statt unten -\n" +
    "  zeigt sofort \"Jetzt aktualisieren\", falls schon eins gefunden wurde\n" +
    "• Einstellungen- und Changelog-Fenster kommen jetzt zuverlässig in\n" +
    "  den Vordergrund, statt manchmal hinter dem Hauptfenster zu bleiben\n" +
    "• Scrollbar-Abstand in Einstellungen/Changelog behoben\n" +
    "• GPU-Statuskarte in der oberen Reihe jetzt mit echten Werten\n" +
    "• Neue Mini-Verlaufsdiagramme für CPU/RAM/GPU (letzte 30 Werte),\n" +
    "  mit adaptiver Skalierung und aktuellem Wert direkt daneben\n" +
    "• Neuer Aktivitätsverlauf (Bereinigungen, Updates, Deinstallationen)\n" +
    "• Tooltips für alle Schnellzugriff-Buttons\n" +
    "• CPU/RAM/GPU zeigen jetzt sofort beim Start echte Werte, statt\n" +
    "  erst nach dem ersten Aktualisierungsintervall zu laden\n" +
    "• Aktualisierungsintervall-Einstellung erwähnt jetzt auch GPU\n" +
    "• Ladebildschirm komplett neu gestaltet: Hex-Grid-Muster mit\n" +
    "  durchlaufender Lila-Leuchtwelle, deckt jetzt zuverlässig den\n" +
    "  ganzen Bildschirm ab und ist mittig zentriert\n" +
    "• Ladebildschirm-Text im Hellmodus gefixt (war unsichtbar)\n" +
    "• Alle Karten und Kacheln auf einheitlichen Eckenradius vereinheitlicht\n" +
    "• Bugfix: Verlaufsdiagramme saßen wegen eines SettingsCard-\n" +
    "  Layout-Bugs immer nur schmal am rechten Rand statt in voller Breite",
    "• Update section in Settings now at the top instead of the bottom -\n" +
    "  shows \"Update Now\" immediately if one was already found\n" +
    "• Settings and Changelog windows now reliably come to the front\n" +
    "  instead of sometimes staying behind the main window\n" +
    "• Fixed scrollbar spacing in Settings/Changelog windows\n" +
    "• GPU status card in the top row now shows real values\n" +
    "• New mini history charts for CPU/RAM/GPU (last 30 values),\n" +
    "  with adaptive scaling and the current value shown right next to it\n" +
    "• New activity log (cleanups, updates, uninstalls)\n" +
    "• Tooltips for all Quick Access buttons\n" +
    "• CPU/RAM/GPU now show real values immediately at startup instead\n" +
    "  of only after the first update interval\n" +
    "• Update interval setting now also mentions GPU\n" +
    "• Loading screen completely redesigned: hex grid pattern with a\n" +
    "  flowing purple light wave, now reliably covers the whole screen\n" +
    "  and is centered\n" +
    "• Fixed loading screen text being invisible in light mode\n" +
    "• Unified corner radius across all cards and tiles\n" +
    "• Bugfix: history charts were stuck narrow on the right edge due to\n" +
    "  a SettingsCard layout quirk instead of using the full width"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.7.7",
    "• Storage- und Winget-Karten zeigen jetzt einen akzentfarbenen Rand,\n" +
    "  solange die Kategorie bzw. das Paket ausgewählt ist",
    "• Storage and Winget cards now show an accent-colored border\n" +
    "  while the category or package is selected"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.7.6",
    "• Fluent-Design-Konsistenz: Mica-Effekt jetzt auch in Einstellungen-\n" +
    "  und Changelog-Fenster sichtbar (Root-Hintergrund dafür leicht\n" +
    "  durchscheinend statt komplett opak)\n" +
    "• Weiche Schatten + Akzent-Hover jetzt auch auf Einstellungen-Karten,\n" +
    "  Changelog-Karten und Systeminfo-Karten (GPU/Laufwerke/Netzwerk)\n" +
    "• Akzentfarbe jetzt auf allen Fortschrittsbalken (Winget-Update,\n" +
    "  Storage-Bereinigung, App-Update)",
    "• Fluent Design consistency: Mica effect now also visible in the\n" +
    "  Settings and Changelog windows (root background slightly\n" +
    "  translucent instead of fully opaque)\n" +
    "• Soft shadows + accent hover now also on Settings cards,\n" +
    "  Changelog cards and System Info cards (GPU/drives/network)\n" +
    "• Accent color now on all progress bars (Winget update,\n" +
    "  storage cleanup, app update)"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.7.5",
    "• Bugfix: Systeminfo-Feldbezeichnungen wurden über FindName() gesucht,\n" +
    "  was nicht zuverlässig funktionierte - jetzt direkter Feldzugriff\n" +
    "• Alle 20 Storage-Kategorienamen und -Beschreibungen übersetzt\n" +
    "  (Benutzer Temp, Papierkorb, Prefetch, Windows Update Cache, etc.)",
    "• Bugfix: System Info field labels were looked up via FindName(),\n" +
    "  which wasn't reliable - now uses direct field access instead\n" +
    "• All 20 storage category names and descriptions translated\n" +
    "  (User Temp, Recycle Bin, Prefetch, Windows Update Cache, etc.)"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.7.4",
    "• Alle 26 Systeminfo-Feldbezeichnungen übersetzt (Computername,\n" +
    "  Hersteller/Modell, BIOS-Version, Secure Boot, TPM, etc.)\n" +
    "• GPU-/Laufwerks-/Netzwerk-Karten auf der Systeminfo-Seite übersetzt\n" +
    "• Deinstaller: \"installiert am\" und \"Deinstallieren\"-Button übersetzt\n" +
    "• Bugfix: doppelte Variablendeklaration verhinderte den Build",
    "• All 26 System Info field labels translated (Computer Name,\n" +
    "  Manufacturer/Model, BIOS Version, Secure Boot, TPM, etc.)\n" +
    "• GPU/drive/network cards on the System Info page translated\n" +
    "• Uninstaller: \"installed on\" and \"Uninstall\" button translated\n" +
    "• Bugfix: duplicate variable declaration prevented the build"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.7.3",
    "• Bugfix: Dashboard-Werte (Speicherplatz, Zuletzt bereinigt, Gesamtstatus)\n" +
    "  blieben beim Sprachwechsel in der ursprünglichen Sprache stehen -\n" +
    "  werden jetzt sofort neu berechnet\n" +
    "• \"Changelog anzeigen\"-Hinweistext in der Sidebar übersetzt\n" +
    "• Winget: \"Keine Updates gefunden\"-Meldung übersetzt",
    "• Bugfix: dashboard values (storage space, last cleaned, overall status)\n" +
    "  stayed in the original language after switching - now recalculated\n" +
    "  immediately\n" +
    "• \"View changelog\" hint text in the sidebar translated\n" +
    "• Winget: \"No updates found\" message translated"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.7.2",
    "• Weitere Lücken in der Übersetzung geschlossen: Speicherplatz-Anzeige,\n" +
    "  \"Zuletzt bereinigt\", Sicherheits-Status, Update-Zähler,\n" +
    "  alle Seiten-Untertitel (Winget/Storage/Deinstaller)",
    "• Closed further translation gaps: storage space display,\n" +
    "  \"Last cleaned\", security status, update counter,\n" +
    "  all page subtitles (Winget/Storage/Uninstall)"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.7.1",
    "• Sprachauswahl (Erststart + Einstellungen) jetzt als Dropdown\n" +
    "• Übersetzung deutlich erweitert: Systeminfo-Abschnittsüberschriften,\n" +
    "  Storage-Gruppennamen, Action-Bars (Winget/Storage/Deinstaller),\n" +
    "  Kontakt-Dialog\n" +
    "• Bugfix: Sprachauswahl-Dialog blockierte den App-Start komplett\n" +
    "  (schwarzer Bildschirm) - läuft jetzt erst nach dem normalen Laden",
    "• Language selection (first run + settings) is now a dropdown\n" +
    "• Translation significantly expanded: System Info section headers,\n" +
    "  Storage group names, action bars (Winget/Storage/Uninstall),\n" +
    "  Contact dialog\n" +
    "• Bugfix: language selection dialog completely blocked app startup\n" +
    "  (black screen) - now runs only after normal loading"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.7.0",
    "• Neu: Englische Sprachversion, umschaltbar in den Einstellungen\n" +
    "• Sprachauswahl erscheint einmalig beim allerersten Start\n" +
    "• Übersetzt: Sidebar, Dashboard, Schnellzugriff, Einstellungen-Fenster\n" +
    "• Tiefer liegende Bereiche (Systeminfo-Details, Storage/Winget/\n" +
    "  Deinstaller-Interna, Changelog-Einträge, Log-Meldungen) bleiben\n" +
    "  vorerst Deutsch",
    "• New: English language version, switchable in settings\n" +
    "• Language selection appears once on the very first start\n" +
    "• Translated: sidebar, dashboard, quick access, settings window\n" +
    "• Deeper areas (System Info details, Storage/Winget/Uninstall\n" +
    "  internals, changelog entries, log messages) remain German\n" +
    "  for now"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.6.2",
    "• \"Übersicht\" in der Sidebar heißt jetzt \"Dashboard\"\n" +
    "• Bugfix: Große Seiten-Überschrift zeigte teils den internen englischen\n" +
    "  Namen statt der deutschen Bezeichnung (z.B. \"Uninstall\" statt\n" +
    "  \"Deinstallieren\", \"Storage\" statt \"Dateien\") - jetzt überall konsistent\n" +
    "• Startseiten-Auswahl in den Einstellungen an Sidebar-Namen angeglichen",
    "• \"Übersicht\" in the sidebar is now called \"Dashboard\"\n" +
    "• Bugfix: the large page heading sometimes showed the internal English\n" +
    "  routing name instead of the proper label (e.g. \"Uninstall\" instead\n" +
    "  of \"Deinstallieren\", \"Storage\" instead of \"Dateien\") - now consistent\n" +
    "  everywhere\n" +
    "• Startup page selection in settings aligned with sidebar names"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.6.1",
    "• Weiche Schatten auf allen Dashboard-/Statuskarten (Übersicht)\n" +
    "• CPU-/RAM-Fortschrittsbalken auf der Systeminfo-Seite nutzen jetzt die Akzentfarbe\n" +
    "• Aktive Sidebar-Navigation wird jetzt farblich hervorgehoben",
    "• Soft shadows on all dashboard/status cards (overview)\n" +
    "• CPU/RAM progress bars on the System Info page now use the accent color\n" +
    "• The active sidebar navigation item is now highlighted"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.6.0",
    "• Übersichtsseite komplett überarbeitet: größere Statuskarten mit Icons\n" +
    "• Neues Live-Dashboard: Speicherplatz, installierte Programme,\n" +
    "  letzte Bereinigung, verfügbare Updates, Gesamtstatus\n" +
    "• GPU-Auslastung und CPU-/GPU-Temperatur jetzt über LibreHardwareMonitor\n" +
    "• Neue Akzentfarbe (Violett-Blau) für aktive Elemente und Hover-Effekte\n" +
    "• Schnellzugriff überarbeitet, jetzt mit Icons und Einstellungen-Button\n" +
    "• Dezente Hover-Animationen auf allen Dashboard-Karten",
    "• Overview page completely redesigned: bigger status cards with icons\n" +
    "• New live dashboard: storage space, installed programs, last cleanup,\n" +
    "  available updates, overall status\n" +
    "• GPU usage and CPU/GPU temperature now via LibreHardwareMonitor\n" +
    "• New accent color (violet-blue) for active elements and hover effects\n" +
    "• Quick access redesigned, now with icons and a settings button\n" +
    "• Subtle hover animations on all dashboard cards"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.5.0",
    "• Admin-pflichtige Bereiche auf der Dateien-Seite fragen jetzt gezielt\n" +
    "  per UAC-Prompt nach Rechten, statt stumm mit Fehler abzubrechen\n" +
    "• Sammel-Löschung bündelt alle Admin-Bereiche in einem einzigen UAC-Prompt\n" +
    "• Bugfix: Deadlock beim elevierten Löschvorgang behoben\n" +
    "• Bugfix: Windows Upgrade Logs ($WINDOWS.~BT) ließen sich wegen einer\n" +
    "  einzelnen geschützten Datei (Boot-Konfiguration) gar nicht löschen -\n" +
    "  jetzt wird Datei für Datei einzeln versucht statt alles-oder-nichts",
    "• Admin-required areas on the Files page now specifically request\n" +
    "  elevation via a UAC prompt instead of silently failing\n" +
    "• Bulk deletion now bundles all admin-required areas into a single UAC prompt\n" +
    "• Bugfix: fixed a deadlock in the elevated deletion process\n" +
    "• Bugfix: Windows Upgrade Logs ($WINDOWS.~BT) couldn't be deleted at all\n" +
    "  because of a single protected file (boot configuration) - now each\n" +
    "  file is attempted individually instead of all-or-nothing"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.9",
    "• Kontakt-Button in der Sidebar verkleinert\n" +
    "• Neuer Ko-fi-Button daneben zur Unterstützung von WinVora",
    "• Contact button in the sidebar made smaller\n" +
    "• New Ko-fi button next to it to support WinVora"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.8",
    "• Neuer Update-Hinweis: kleiner roter Badge am Einstellungen-Button,\n" +
    "  falls ein neues Update verfügbar ist\n" +
    "• Prüfung läuft still im Hintergrund beim App-Start, ohne zu stören",
    "• New update indicator: small red badge on the settings button if a\n" +
    "  new update is available\n" +
    "• Check runs quietly in the background at app startup without being intrusive"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.7",
    "• Publish-Größe von 1,9 GB auf 53 MB reduziert\n" +
    "  (PublishSingleFile verursachte einen Bündelungs-Bug mit der WindowsAppSDK)\n" +
    "• Ungenutzte KI/ML-Laufzeitkomponenten (ONNX Runtime u.a.) vom Build ausgeschlossen\n" +
    "• Update-Installation läuft jetzt komplett still (kein Assistenten-Fenster mehr)\n" +
    "• WinVora startet nach einem Update automatisch wieder\n" +
    "• Update-Bestätigungsdialog zeigt jetzt \"Jetzt aktualisieren\" statt \"Löschen\"",
    "• Publish size reduced from 1.9 GB to 53 MB\n" +
    "  (PublishSingleFile caused a bundling bug with the Windows App SDK)\n" +
    "• Unused AI/ML runtime components (ONNX Runtime etc.) excluded from the build\n" +
    "• Update installation now runs completely silently (no more wizard window)\n" +
    "• WinVora automatically restarts after an update\n" +
    "• Update confirmation dialog now shows \"Update Now\" instead of \"Delete\""
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.6",
    "• Update-Download: eindeutiger Temp-Dateiname pro Versuch\n" +
    "  (verhindert Konflikt mit noch laufendem Installer aus vorherigem Versuch)\n" +
    "• Update-Fortschritt zeigt jetzt immer heruntergeladene MB an,\n" +
    "  auch wenn der Server keine Gesamtgröße mitliefert\n" +
    "• Mehr Logging beim Update-Download für einfachere Fehlersuche",
    "• Update download: unique temp file name per attempt\n" +
    "  (prevents conflicts with a still-running installer from a previous attempt)\n" +
    "• Update progress now always shows downloaded MB, even if the server\n" +
    "  doesn't provide a total size\n" +
    "• More logging during update downloads for easier troubleshooting"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.5",
    "• Test-Release zur Überprüfung des Auto-Update-Mechanismus",
    "• Test release to verify the auto-update mechanism"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.4",
    "• Neu: Automatisches Update direkt aus den Einstellungen\n" +
    "  (prüft GitHub-Releases, lädt Installer herunter, aktualisiert automatisch)\n" +
    "• Ladebildschirm: Liquid-Glass-Bänder laufen jetzt wieder etwas ruhiger\n" +
    "• Bugfix: Bänder starteten fälschlicherweise alle mittig übereinander\n" +
    "• Cutouts der Glas-Bänder sind jetzt zufällig statt immer identisch",
    "• New: automatic update directly from settings\n" +
    "  (checks GitHub releases, downloads the installer, updates automatically)\n" +
    "• Loading screen: liquid glass bands now move a bit more calmly again\n" +
    "• Bugfix: bands incorrectly all started stacked in the center\n" +
    "• Cutouts in the glass bands are now random instead of always identical"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.3",
    "• Dateien-Seite zeigt jetzt \"Zuletzt bereinigt: vor X Tagen\" an\n" +
    "• Startbildschirm: Logo jetzt über dem \"WinVora\"-Schriftzug\n" +
    "• Startbildschirm: animierter Glas-Balken läuft im Hintergrund durch",
    "• Files page now shows \"Last cleaned: X days ago\"\n" +
    "• Loading screen: logo now sits above the \"WinVora\" wordmark\n" +
    "• Loading screen: animated glass bar runs through the background"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.2",
    "• Sidebar-Navigation: scrollbar, falls mehr Kategorien nicht mehr auf einmal reinpassen",
    "• Sidebar navigation: now scrollable if more categories don't fit at once"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.1",
    "• Kontakt-Seite mit echten Kontaktdaten aktualisiert\n" +
    "• Winget: Fix für durchgängiges \"N/A\" bei Herausgeber/Größe auf manchen PCs\n" +
    "  (älteres winget kannte ein verwendetes Flag nicht - jetzt entfernt)\n" +
    "• Fehler beim Abrufen von Winget-Details werden jetzt geloggt,\n" +
    "  damit sich sowas beim nächsten Mal leichter nachvollziehen lässt",
    "• Contact page updated with real contact details\n" +
    "• Winget: fixed persistent \"N/A\" for publisher/size on some PCs\n" +
    "  (older winget versions didn't recognize a flag we used - now removed)\n" +
    "• Errors while fetching Winget details are now logged, making it\n" +
    "  easier to diagnose next time"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.0",
    "• Neue Seite: Programme deinstallieren (Registry-Scan, Suche/Filter)\n" +
    "• Deinstallation startet den originalen Uninstaller jedes Programms\n" +
    "• Echte App-Icons statt Platzhalter-Symbolen bei Winget und Deinstaller\n" +
    "• Icons werden im Hintergrund nachgeladen, ohne die Liste zu blockieren\n" +
    "• Titelleisten-Fix: dünne Trennlinie liegt nicht mehr über dem Logo\n" +
    "• Richtiger Windows-Installer (Inno Setup) mit Sprachauswahl,\n" +
    "  wählbarem Installationsort und optionaler Desktop-Verknüpfung\n" +
    "• Installer erkennt vorhandene Installation automatisch und aktualisiert sie\n" +
    "• Installer schließt WinVora bei Bedarf automatisch vor einem Update\n" +
    "• Quellcode und Downloads jetzt in getrennten GitHub-Repos organisiert",
    "• New page: uninstall programs (registry scan, search/filter)\n" +
    "• Uninstalling launches each program's original uninstaller\n" +
    "• Real app icons instead of placeholder symbols for Winget and Uninstall\n" +
    "• Icons load in the background without blocking the list\n" +
    "• Title bar fix: thin divider line no longer overlaps the logo\n" +
    "• Proper Windows installer (Inno Setup) with language selection,\n" +
    "  choosable install location, and optional desktop shortcut\n" +
    "• Installer automatically detects an existing installation and updates it\n" +
    "• Installer automatically closes WinVora if needed before an update\n" +
    "• Source code and downloads now organized in separate GitHub repos"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.3.3",
    "• Dateien-Seite: 20 Kategorien in 5 ausklappbare Gruppen sortiert\n" +
    "• Neue Einstellung: Startseite frei wählbar (Übersicht/System/Apps/Dateien)\n" +
    "• Neue Einstellung: Aktualisierungsintervall für CPU/RAM (1/2/5 Sekunden)\n" +
    "• Neue Einstellung: Mit Windows starten (Autostart)\n" +
    "• Neue Einstellung: Bestätigung vor dem Löschen ein-/ausschaltbar\n" +
    "• Neu: Log-Datei direkt aus den Einstellungen öffnen/leeren\n" +
    "• Neu: Einstellungen mit einem Klick zurücksetzen\n" +
    "• Neuer Kontakt-Button in der Sidebar (unter Version)\n" +
    "• Glas-Intensität jetzt fest auf 18 statt einstellbar\n" +
    "• Einstellungs- und Changelog-Fenster: passende Größe + scrollbar",
    "• Files page: 20 categories sorted into 5 collapsible groups\n" +
    "• New setting: freely choosable startup page (Dashboard/System/Apps/Files)\n" +
    "• New setting: update interval for CPU/RAM (1/2/5 seconds)\n" +
    "• New setting: start with Windows (autostart)\n" +
    "• New setting: toggle confirmation before deleting\n" +
    "• New: open/clear the log file directly from settings\n" +
    "• New: reset settings with one click\n" +
    "• New contact button in the sidebar (below version)\n" +
    "• Glass intensity now fixed at 18 instead of adjustable\n" +
    "• Settings and changelog windows: proper size + scrollable"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.3.2",
    "• Projekt komplett auf WinVora umbenannt (Namespace, exe, Fenstertitel)\n" +
    "• Eigenes App-Icon für Titelleiste und Taskleiste\n" +
    "• Dünne Trennlinie unter der Titelleiste für einen saubereren oberen Rand\n" +
    "• Glas-Karten starten jetzt unterhalb der Fenster-Buttons statt darunter\n" +
    "• Winget: Downloadgröße hat jetzt Vorrang vor Installationsgröße\n" +
    "• Warnhinweis, falls Chrome/Edge beim Löschen des Browser-Cache noch laufen\n" +
    "• Neues Logging (%LOCALAPPDATA%\\WinVora\\log.txt) für Fehler und Aktionen\n" +
    "• Globaler Fehler-Handler, damit stille Abstürze nachvollziehbar werden\n" +
    "• Self-Contained Single-File-Publish (keine Installation beim Testen nötig)\n" +
    "• Admin-Manifest entfernt - App startet ohne UAC-Abfrage\n" +
    "• publish.bat: baut und zippt die Testversion automatisch",
    "• Project completely renamed to WinVora (namespace, exe, window title)\n" +
    "• Custom app icon for title bar and taskbar\n" +
    "• Thin divider line below the title bar for a cleaner top edge\n" +
    "• Glass cards now start below the window buttons instead of overlapping them\n" +
    "• Winget: download size now takes priority over install size\n" +
    "• Warning if Chrome/Edge are still running when clearing browser cache\n" +
    "• New logging (%LOCALAPPDATA%\\WinVora\\log.txt) for errors and actions\n" +
    "• Global error handler so silent crashes become traceable\n" +
    "• Self-contained single-file publish (no installation needed for testing)\n" +
    "• Admin manifest removed - app starts without a UAC prompt\n" +
    "• publish.bat: automatically builds and zips the test version"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.3.1",
    "• Neu: Heller Modus (Umschalter in den Einstellungen)\n" +
    "• Einstellungen-Button jetzt über statt neben der Versions-Karte\n" +
    "• Winget-Liste läuft im Hintergrund - Oberfläche ruckelt beim Laden nicht mehr\n" +
    "• Refresh- und Start-Update-Button bei Winget einheitlich groß",
    "• New: light mode (toggle in settings)\n" +
    "• Settings button now above instead of next to the version card\n" +
    "• Winget list now loads in the background - the UI no longer stutters while loading\n" +
    "• Refresh and Start Update buttons on the Winget page are now a consistent size"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.3.0",
    "• Neue Dateien-Seite (Speicherbereinigung) mit 19 Kategorien\n" +
    "• Auswahl per Toggle, Einzel- und Sammel-Löschung\n" +
    "• \"Alle auswählen\"-Button auf der Dateien-Seite\n" +
    "• Bestätigungsdialog vor jeder Löschung\n" +
    "• Fortschrittsanzeige mit Live-Status beim Bereinigen\n" +
    "• Winget: Herausgeber und Größe werden automatisch nachgeladen\n" +
    "• Winget: Download-Fortschritt in MB beim Installieren\n" +
    "• Winget: klare Fehlermeldung, falls winget nicht installiert ist\n" +
    "• App startet automatisch mit Administratorrechten\n" +
    "• Eigene dunkle Titelleiste statt weißer System-Leiste\n" +
    "• Hintergrund auf reines Schwarz umgestellt\n" +
    "• Karten in kräftigerem Liquid-Glass-Weiß\n" +
    "• Echtes Mica-Backdrop mit Acrylic-Fallback\n" +
    "• Hover-Effekte auf den Info-Karten\n" +
    "• Sanftes Einblenden beim Seitenwechsel\n" +
    "• Ladebildschirm beim App-Start\n" +
    "• Diverse Bugfixes (doppeltes Laden der Systeminfos behoben)",
    "• New Files page (storage cleanup) with 19 categories\n" +
    "• Selection via toggles, single and bulk deletion\n" +
    "• \"Select All\" button on the Files page\n" +
    "• Confirmation dialog before every deletion\n" +
    "• Progress display with live status while cleaning\n" +
    "• Winget: publisher and size are fetched automatically\n" +
    "• Winget: download progress in MB while installing\n" +
    "• Winget: clear error message if winget isn't installed\n" +
    "• App now starts automatically with administrator rights\n" +
    "• Custom dark title bar instead of the white system bar\n" +
    "• Background switched to pure black\n" +
    "• Cards now use a stronger liquid-glass white\n" +
    "• Real Mica backdrop with Acrylic fallback\n" +
    "• Hover effects on info cards\n" +
    "• Smooth fade-in when switching pages\n" +
    "• Loading screen at app startup\n" +
    "• Various bugfixes (fixed system info loading twice)"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.2.0",
    "• Neue Übersicht als Startseite\n" +
    "• Systeminfo, Winget und Dateien als eigene Bereiche\n" +
    "• Große Health-Karten für CPU, RAM, Sicherheit und Updates\n" +
    "• Modernisierte Liquid-Glass-Oberfläche\n" +
    "• Größere Sidebar-Navigation\n" +
    "• Neue große Systeminfo-Dropdowns\n" +
    "• Alle Systeminfo-Kategorien sind einklappbar\n" +
    "• Alles-aufklappen- und Alles-einklappen-Buttons\n" +
    "• Systeminfo-Karten pro Kategorie zusammengefasst\n" +
    "• Größere Schrift, mehr Abstand und bessere Lesbarkeit\n" +
    "• Changelog-Fenster im Liquid-Glass-Stil\n" +
    "• Winget-Prozesshandling verbessert",
    "• New overview as the startup page\n" +
    "• System Info, Winget, and Files as separate sections\n" +
    "• Large health cards for CPU, RAM, security, and updates\n" +
    "• Modernized liquid-glass interface\n" +
    "• Larger sidebar navigation\n" +
    "• New large System Info dropdowns\n" +
    "• All System Info categories are collapsible\n" +
    "• \"Expand All\" and \"Collapse All\" buttons\n" +
    "• System Info cards grouped by category\n" +
    "• Larger text, more spacing, better readability\n" +
    "• Changelog window in liquid-glass style\n" +
    "• Improved Winget process handling"
));

            panel.Children.Add(MakeChangelogCard(
                "Version 0.1.0",
                "• Schnellere Ladezeit\n" +
                "• CPU-Optimierung\n" +
                "• Live-Systeminfos\n" +
                "• Winget-Updateübersicht\n" +
                "• Erstes Changelog-Fenster",
                "• Faster load time\n" +
                "• CPU optimization\n" +
                "• Live system info\n" +
                "• Winget update overview\n" +
                "• First changelog window"
            ));

            var olderReleasesButton = new Button
            {
                Content = Localization.CurrentLanguage == "en" ? "View older releases on GitHub" : "Ältere Versionen auf GitHub ansehen",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            olderReleasesButton.Click += (_, __) => Process.Start(new ProcessStartInfo(
                "https://github.com/WinVora/WinVora-Releases/releases") { UseShellExecute = true });
            panel.Children.Add(olderReleasesButton);

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 22, 0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = panel
            };
            scrollViewer.Resources["ScrollBarSize"] = 16d;
            scrollViewer.Resources["ScrollBarVerticalThumbMinWidth"] = 10d;

            var contentHost = new Grid { Padding = new Thickness(28, 18, 18, 28) };
            contentHost.Children.Add(scrollViewer);
            Grid.SetRow(contentHost, 1);

            var divider = MakeTitleBarDivider();
            Grid.SetRow(divider, 0);

            var titleLabel = MakeTitleBarLabel(Localization.T("Changelog.WindowTitle"));
            Grid.SetRow(titleLabel, 0);

            root.Children.Add(contentHost);
            root.Children.Add(divider);
            root.Children.Add(titleLabel);

            changelogWindow.Content = root;
            StyleDarkWindow(changelogWindow, _settings.ChangelogWindowWidth, _settings.ChangelogWindowHeight);
            WindowActivationService.PlaceWindow(this, changelogWindow,
                _settings.ChangelogWindowX, _settings.ChangelogWindowY,
                _settings.ChangelogWindowWidth, _settings.ChangelogWindowHeight);
            changelogWindow.Activate();
            WindowActivationService.ShowOwnedInFront(this, changelogWindow);
        }

        private Border MakeChangelogCard(string title, string textDe, string? textEn = null)
        {
            _visibleChangelogCardCount++;
            var text = Localization.CurrentLanguage == "en" && textEn != null ? textEn : textDe;
            bool isCurrent = title == $"Version {CurrentVersion}";

            var card = new Border
            {
                Visibility = _visibleChangelogCardCount <= 4 ? Visibility.Visible : Visibility.Collapsed,
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(20),
                Background = isCurrent
                    ? (SolidColorBrush)RootGrid.Resources["AppAccentOverlay10"]
                    : (SolidColorBrush)RootGrid.Resources["AppOverlay18"],
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0)
            };

            var content = new StackPanel
            {
                Spacing = 10
            };

            var versionHeader = new Grid { ColumnSpacing = 10 };
            versionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            versionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            versionHeader.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 19,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"]
            });
            if (isCurrent)
            {
                var badges = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center
                };
                badges.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(9, 4, 9, 4),
                    Background = (SolidColorBrush)RootGrid.Resources["AppOverlay30"],
                    Child = new TextBlock
                    {
                        Text = Localization.CurrentLanguage == "en" ? "Current" : "Aktuell",
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"]
                    }
                });
                if (IsBetaBuild)
                {
                    badges.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(7),
                        Padding = new Thickness(9, 4, 9, 4),
                        Background = (SolidColorBrush)RootGrid.Resources["AppAccentOverlay20"],
                        Child = new TextBlock
                        {
                            Text = "BETA",
                            FontSize = 11,
                            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                            Foreground = (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"]
                        }
                    });
                }
                Grid.SetColumn(badges, 1);
                versionHeader.Children.Add(badges);
            }
            content.Children.Add(versionHeader);

            var bulletList = new StackPanel { Spacing = 8 };
            var bulletItems = new List<string>();

            foreach (var rawLine in text.Replace("\r", "").Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("•", StringComparison.Ordinal))
                {
                    bulletItems.Add(line.TrimStart('•', ' '));
                }
                else if (!string.IsNullOrWhiteSpace(line) && bulletItems.Count > 0)
                {
                    bulletItems[^1] += " " + line;
                }
            }

            bool english = Localization.CurrentLanguage == "en";
            foreach (var category in bulletItems.GroupBy(item => ChangelogUiBuilder.CategoryFor(item, english)))
            {
                bulletList.Children.Add(new TextBlock
                {
                    Text = category.Key,
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"],
                    Margin = new Thickness(0, 8, 0, 2)
                });
                foreach (var item in category)
                {
                    var row = new Grid { ColumnSpacing = 10 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var bullet = new TextBlock { Text = "•", FontSize = 15, Foreground = (SolidColorBrush)RootGrid.Resources["AppAccentBrush"] };
                    var body = new TextBlock
                    {
                        Text = ChangelogUiBuilder.RemoveCategoryPrefix(item),
                        FontSize = 14,
                        LineHeight = 21,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundCC"]
                    };
                    Grid.SetColumn(body, 1);

                    row.Children.Add(bullet);
                    row.Children.Add(body);
                    bulletList.Children.Add(row);
                }
            }

            if (title != $"Version {CurrentVersion}" && bulletItems.Count > 6)
            {
                int additionalCount = bulletItems.Count - 5;
                bulletItems = bulletItems.Take(5).ToList();
                bulletItems.Add(Localization.CurrentLanguage == "en"
                    ? $"{additionalCount} additional technical and quality improvements"
                    : $"{additionalCount} weitere technische und qualitative Verbesserungen");
            }

            content.Children.Add(bulletList);

            card.Child = content;
            AttachCardHoverEffect(card);
            return card;
        }

    }
}
