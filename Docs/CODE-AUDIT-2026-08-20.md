# WinVora – technischer Code-Audit

Stand: 20. August 2026, nach Umsetzung der Fix-Roadmap und erneutem Code-/Build-Abgleich
Auditbasis: aktueller lokaler Arbeitsstand von `WinVora-Source` einschließlich nicht committeter Änderungen  
Version: `0.8.5-beta.4`

## A. Executive Summary

WinVora ist eine unverpackte WinUI-3-Anwendung auf .NET 8 für x64. Die Anwendung ist self-contained, läuft grundsätzlich mit normalen Benutzerrechten und erhöht nur eng begrenzte Operationen. Die fachlichen Bereiche sind in partielle `MainWindow`-Dateien, Services, Provider und neue Operation-Controller aufgeteilt. Ein klassisches MVVM- oder DI-Modell wird nicht verwendet; UI-Zustand und Orchestrierung liegen weiterhin überwiegend in `MainWindow`.

Positiv bewertet werden insbesondere:

- `asInvoker` als Standard und ein begrenzter Admin-Helper für Storage-Aktionen;
- exakte Allowlist und Reparse-Point-Schutz für `Windows.old` und `$WINDOWS.~BT`;
- blockierte skriptbasierte oder offensichtlich manipulierte Deinstallationsbefehle;
- HTTPS-, Host- und SHA-256-Prüfung des WinVora-Eigenupdates;
- Prozessbaum-Abbruch und Zeitlimits bei regulären WinGet-Installationen;
- getrennte Defender-, Firewall-, TPM- und BitLocker-Abfragen mit Fallbacks;
- begrenzte Logrotation und Anonymisierung des Supportberichts;
- erfolgreicher Release-Publish ohne Compilerwarnungen;
- ein echtes MSTest-Projekt mit 23 bestandenen Release-Tests und CI-Test-Gate;
- auf unveränderliche Commit-SHAs fixierte GitHub Actions;
- keine aktuell bekannten verwundbaren direkten oder transitiven NuGet-Pakete.

Es wurde keine bestätigte kritische Sicherheitslücke gefunden. Die vier ursprünglichen Findings mit hoher Priorität sind im aktuellen Arbeitsstand behoben: Storage-Hilfsprozesse laufen über den gemeinsamen ProcessRunner mit Streamdrain, Timeout und Prozessbaumabbruch; Live-Telemetrie wird im ThreadPool gemessen; Firewallprofile werden konservativ zusammengeführt; jeder von null verschiedene WinGet-Exitcode wird als Fehler behandelt.

Von 25 dokumentierten Findings sind nach dem Re-Audit 21 behoben, eines teilweise behoben und drei weiterhin offen. Die verbleibenden Punkte sind keine bestätigten Release-Blocker hoher Priorität. Sie betreffen die grundsätzlich tabellenbasierte WinGet-Auswertung, verbleibende UI-Kopplung und die noch fehlende Authenticode-Signatur.

## B. Kritische Probleme

Keine bestätigten kritischen Findings.

## C. Hohe Priorität – im aktuellen Stand behoben

### H-01 – Geschützte Storage-Hilfsprozesse können hängen oder parallel zur Löschung weiterlaufen

**Status im Re-Audit:** Behoben. `SystemAccess.ProcessRunner` leert beide Streams, erzwingt Timeout/Abbruch, beendet den Prozessbaum und liefert einen auswertbaren Exitcode. `takeown` und `icacls` werden vor dem Löschpfad geprüft.

**Priorität:** Hoch  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** `Features/Storage/StorageService.cs`  
**Klasse:** `StorageService`  
**Methode:** `DeleteProtectedFolder`, `RunHiddenCommand`  
**Codebereich:** Zeilen 422–463 und 570–598

**Problem:** `RunHiddenCommand` leitet Standardausgabe und Standardfehler um, liest beide Streams aber nie. Bei ausreichend viel Ausgabe kann der Kindprozess auf einem vollen Pipe-Puffer blockieren. Zusätzlich wird der Rückgabewert von `WaitForExit(15000)` ignoriert. Nach einem Timeout wird trotzdem auf `ExitCode` zugegriffen; die entstehende Ausnahme wird gefangen, der Kindprozess jedoch weder beendet noch abgewartet. `DeleteProtectedFolder` ignoriert außerdem die Ergebnisse von `takeown.exe` und `icacls.exe` und beginnt sofort mit der rekursiven Löschung.

**Auswirkung:** Geschützte Bereinigungen können hängen, unvollständig sein oder gleichzeitig mit einem noch laufenden Rechteprozess Dateien bearbeiten. Die Oberfläche kann einen Fehler anzeigen, obwohl der externe Prozess weiterläuft.

**Auslöser:** Große `Windows.old`- oder `$WINDOWS.~BT`-Strukturen, umfangreiche Ausgabe von `takeown`, langsame Datenträger oder ein hängender Systemprozess.

**Reproduktion:** Eine große erlaubte Upgrade-Struktur auswählen, Bereinigung starten und einen langsamen beziehungsweise ausgabereichen `takeown`-Lauf beobachten. Nach 15 Sekunden kann der Helper noch aktiv sein, während WinVora bereits fortfährt.

**Lösung:** Prozesse asynchron starten, beide Streams parallel lesen, Timeout und CancellationToken verknüpfen, bei Timeout den gesamten Prozessbaum beenden und erst nach bestätigtem Exit fortfahren. Rückgabecodes von `takeown` und `icacls` müssen ausgewertet werden.

**Beispiel:** `Task.WhenAll(ReadToEndAsync, WaitForExitAsync)` mit verknüpftem Timeout-Token und `Kill(entireProcessTree: true)`.

**Risiko der Änderung:** Die geschützte Bereinigung und der Admin-Helper sind betroffen. Tests müssen erfolgreiche, abgelehnte und zeitüberschrittene Systembefehle abdecken.

### H-02 – Live-Sensorabfragen laufen im Normalfall auf dem UI-Thread

**Status im Re-Audit:** Behoben. `HardwareTelemetryService.GetSnapshotAsync` serialisiert Messungen und führt den gesamten Messkörper ausdrücklich im ThreadPool aus.

**Priorität:** Hoch  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** `Services/HardwareTelemetryService.cs`, `Features/SystemInfo/MainWindow.SystemInfo.cs`  
**Klasse:** `HardwareTelemetryService`, `MainWindow`  
**Methode:** `GetSnapshotAsync`, `UpdateLiveUsageAsync`  
**Codebereich:** `HardwareTelemetryService.cs` Zeilen 33–60; `MainWindow.SystemInfo.cs` Zeilen 495–577

**Problem:** `UpdateLiveUsageAsync` wird durch einen `DispatcherTimer` auf dem UI-Thread aufgerufen. `GetSnapshotAsync` wartet auf `SemaphoreSlim.WaitAsync`. Ist das Gate frei, ist das Task bereits abgeschlossen und die Methode läuft trotz `ConfigureAwait(false)` synchron auf dem aufrufenden UI-Thread weiter. `PerformanceCounter.NextValue()` und insbesondere `HardwareMonitorService.GetReadings()` werden dann direkt im UI-Thread ausgeführt.

**Auswirkung:** Regelmäßiges Stottern beim Scrollen, Verschieben oder Vergrößern des Fensters; auf langsamer oder problematischer Hardware sind längere UI-Blockaden möglich.

**Auslöser:** Standardmäßiger Live-Timer, insbesondere jeder dritte Tick mit vollständiger LibreHardwareMonitor-Aktualisierung.

**Reproduktion:** Systeminfo oder Dashboard öffnen, Fenster kontinuierlich bewegen beziehungsweise scrollen und Sensor-Ticks mit einem Profiler korrelieren.

**Lösung:** Die komplette Messung hinter dem Gate explizit auf einen Hintergrundthread verschieben. Nur die fertige Momentaufnahme darf anschließend im UI-Thread angewendet werden.

**Risiko der Änderung:** Synchronisierung und Shutdown von LibreHardwareMonitor müssen beibehalten werden; UI-Controls dürfen nicht aus dem Hintergrundthread angesprochen werden.

### H-03 – Firewallstatus wird bei nur einem aktiven Profil als vollständig aktiv gemeldet

**Status im Re-Audit:** Behoben. Alle gefundenen Firewallprofile werden gemeinsam und konservativ bewertet; deaktivierte oder unbekannte Profile können kein vollständig aktives Ergebnis mehr erzeugen.

**Priorität:** Hoch  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** `Features/SystemInfo/SystemInfoProvider.cs`, `Features/SystemInfo/SystemInfoProvider.Security.cs`  
**Klasse:** `SystemInfoProvider`  
**Methode:** `GetFastSecurityStatusAsync`, `FillFirewall`  
**Codebereich:** `SystemInfoProvider.cs` Zeilen 44–53; `SystemInfoProvider.Security.cs` Zeilen 194–228

**Problem:** Sowohl WMI- als auch Registry-Pfad verwenden `Any`. Dadurch genügt ein aktiviertes Domain-, Private- oder Public-Profil, um global „Aktiv“ zu melden, selbst wenn ein weiteres Profil deaktiviert ist.

**Auswirkung:** Das Dashboard kann einen grünen Sicherheitsstatus anzeigen, obwohl ein Windows-Firewallprofil deaktiviert ist. Dies ist eine falsche Sicherheitszusage.

**Auslöser:** Mindestens ein aktiviertes und mindestens ein deaktiviertes Firewallprofil.

**Reproduktion:** In der Windows-Firewall ein Profil deaktivieren, ein anderes aktiv lassen und WinVora neu prüfen lassen.

**Lösung:** Alle ausgelesenen Profile bewerten und zwischen „Aktiv“, „Teilweise deaktiviert“, „Deaktiviert“ und „Nicht prüfbar“ unterscheiden. Optional sollte zusätzlich das aktuell verwendete Netzwerkprofil hervorgehoben werden.

**Risiko der Änderung:** Dashboardstatus, Sicherheitskarte und `SecurityStatusEvaluator` müssen dieselben Zustände verstehen.

### H-04 – Fehlgeschlagene WinGet-Prüfung kann als leere Updateliste erscheinen

**Status im Re-Audit:** Behoben. `WingetDiscoveryService` behandelt jeden von null verschiedenen Exitcode unabhängig vom Inhalt von `stderr` als Fehler. Ein automatisierter Release-Test deckt diesen Fall ab.

**Priorität:** Hoch  
**Sicherheit der Einschätzung:** Sehr wahrscheinlich  
**Datei:** `Features/Updates/WingetDiscoveryService.cs`  
**Klasse:** `WingetDiscoveryService`  
**Methode:** `GetUpgrades`  
**Codebereich:** Zeilen 31–91

**Problem:** Bei einem WinGet-Exitcode ungleich null wird nur dann eine Exception ausgelöst, wenn keine Pakete gefunden wurden und `stderr` nicht leer ist. Fehler, die ausschließlich nach `stdout` geschrieben werden oder ohne Text enden, liefern dagegen erfolgreich eine leere Liste. Der gelesene Standardoutput wird nicht als Diagnosepuffer erhalten.

**Auswirkung:** Quellen-, Netzwerk- oder WinGet-Fehler können als „Alles ist aktuell“ angezeigt werden. Eine Kernfunktion liefert damit ein falsches Ergebnis.

**Auslöser:** WinGet beendet sich mit Fehlercode, liefert aber keine verwertbare Tabelle und keinen Text auf `stderr`.

**Reproduktion:** Eine fehlerhafte beziehungsweise nicht erreichbare Quelle simulieren und prüfen, ob WinGet den Fehler auf `stdout` ausgibt.

**Lösung:** Jeden Exitcode ungleich null als fehlgeschlagene Prüfung behandeln, sofern das Ergebnis nicht ausdrücklich als vollständig verwertbar bestätigt wurde. Standardoutput und Standardfehler begrenzt puffern und für eine verständliche Fehlermeldung auswerten.

**Risiko der Änderung:** Parser, Fehlerübersetzung, leere Ansicht und Update-Nachprüfung sind betroffen.

## D. Mittlere Priorität

### M-01 – CancellationToken fehlt bei der tatsächlich verwendeten normalen Storage-Löschung

**Status im Re-Audit:** Behoben. Der aktive Löschpfad reicht `_storageOperations.Token` bis `StorageService.DeleteCategoryAsync` und die darunterliegenden Operationen weiter.

**Priorität:** Mittel  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** `Features/Storage/MainWindow.Storage.Operations.cs`  
**Klasse:** `MainWindow`  
**Methode:** `StorageDeleteSelectedCoreAsync`  
**Codebereich:** Zeile 149

**Problem:** Die aktive Schleife ruft `StorageService.DeleteCategoryAsync(category)` ohne `_storageOperations.Token` auf. Die ungenutzte Methode `DeleteCategoriesAsync` reicht den Token korrekt weiter, wird aber nirgends aufgerufen.

**Auswirkung:** Abbrechen beziehungsweise Schließen stoppt normale Dateioperationen nicht kontrolliert. Der Prozess kann nur durch vollständiges Beenden der App unterbrochen werden.

**Auslöser:** Schließen oder Abbrechen während einer größeren nicht administrativen Bereinigung.

**Reproduktion:** Große Browser- oder Benutzer-Temp-Struktur löschen und WinVora währenddessen schließen.

**Lösung:** Den Controller-Token an jeden Löschaufruf weiterreichen und den ungenutzten parallelen Codepfad entfernen.

**Risiko der Änderung:** Teilweise gelöschte Kategorien müssen weiterhin korrekt als abgebrochen protokolliert werden.

### M-02 – Abgelehnte Navigation startet trotzdem unsichtbare Seitenarbeit

**Status im Re-Audit:** Behoben. Navigationshandler und Command-Palette starten Seitenarbeit nur noch nach erfolgreichem `TrySetPage`.

**Priorität:** Mittel  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** `App/MainWindow.xaml.cs`, mehrere Seiten-Handler und `Features/Commands/MainWindow.CommandPalette.cs`  
**Klasse:** `MainWindow`  
**Methode:** `SetPage` und aufrufende Handler  
**Codebereich:** `MainWindow.xaml.cs` Zeilen 1603–1609; Command-Palette Zeilen 24–31

**Problem:** `SetPage` liefert `void`. Während einer Installation verweigert die Methode Navigation durch ein frühes `return`. Aufrufer können dies nicht erkennen und starten danach trotzdem `LoadStorage`, `LoadInstalledPrograms`, `AnalyzePerformanceAsync` oder `LoadWinget`.

**Auswirkung:** Unsichtbare Scans, unnötige I/O-Last und veränderte globale UI-Zustände, obwohl weiterhin die Update-Seite sichtbar ist.

**Auslöser:** Seitenwechsel oder Command-Palette während eines laufenden Programmupdates.

**Reproduktion:** Update starten und währenddessen „Dateien analysieren“ über die Command-Palette ausführen.

**Lösung:** `SetPage` als `bool TrySetPage` ausführen und Folgearbeit nur bei erfolgreicher Navigation starten; alternativ Navigation zentral deaktivieren.

**Risiko der Änderung:** Alle Navigationsaufrufer müssen geprüft werden.

### M-03 – Interne Diagnose kann trotz angeblichem Timeout unbegrenzt hängen

**Status im Re-Audit:** Behoben. Diagnoseprozesse verwenden den gemeinsamen ProcessRunner; WMI-Diagnosen besitzen zusätzlich ein begrenztes `WaitAsync`.

**Priorität:** Mittel  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** `Features/Diagnostics/MainWindow.InternalDiagnostics.cs`  
**Klasse:** `MainWindow`  
**Methode:** `CheckWinget`, `CheckWmi`  
**Codebereich:** Zeilen 81–105

**Problem:** `StandardOutput.ReadToEnd()` läuft vor `WaitForExit(3000)` und kann unbegrenzt blockieren. Ein nach drei Sekunden noch laufender Prozess wird nicht beendet. Die WMI-Prüfung besitzt ebenfalls weder Query-Timeout noch CancellationToken.

**Auswirkung:** Der Statusdialog kann dauerhaft im Ladezustand bleiben und einen `winget`-Prozess zurücklassen.

**Auslöser:** Hängendes WinGet oder blockierende WMI-Infrastruktur.

**Reproduktion:** WinGet-Aufruf künstlich blockieren oder WMI-Dienst anhalten und Diagnose öffnen.

**Lösung:** Dieselben Prozess- und WMI-Abstraktionen mit Timeout verwenden, die bereits in den Hauptfunktionen vorhanden sind.

**Risiko der Änderung:** Gering; betroffen ist nur die interne Diagnose.

### M-04 – Windows-Aktivierung kann durch ein anderes lizenziertes Microsoft-Produkt positiv werden

**Status im Re-Audit:** Behoben. Die WMI-Abfrage filtert auf die Windows-Application-ID `55c92734-d682-4d71-983e-d6ec3f16059f`.

**Priorität:** Mittel  
**Sicherheit der Einschätzung:** Sehr wahrscheinlich  
**Datei:** `Features/SystemInfo/SystemInfoProvider.Windows.cs`  
**Klasse:** `SystemInfoProvider`  
**Methode:** `IsActivated`  
**Codebereich:** Zeilen 177–190

**Problem:** Die WMI-Abfrage sucht jedes `SoftwareLicensingProduct` mit `PartialProductKey`, aber filtert nicht auf die Windows-Application-ID. Eine aktivierte Office- oder andere Microsoft-Lizenz kann deshalb `true` liefern.

**Auswirkung:** WinVora kann „Windows aktiviert“ anzeigen, obwohl nur ein anderes Produkt aktiviert ist.

**Auslöser:** Nicht aktiviertes Windows mit mindestens einem anderen lizenzierten Produkt in derselben WMI-Klasse.

**Reproduktion:** WMI-Ergebnisliste mit mehreren Produktfamilien vergleichen.

**Lösung:** Auf die Windows-Application-ID `55c92734-d682-4d71-983e-d6ec3f16059f` und passende Lizenzobjekte filtern.

**Risiko der Änderung:** Nur die Aktivierungsanzeige.

### M-05 – Mehrere WinVora-Instanzen sind nicht koordiniert

**Status im Re-Audit:** Behoben. `AppInstance.FindOrRegisterForKey` koordiniert eine Hauptinstanz und leitet weitere Aktivierungen an sie weiter.

**Priorität:** Mittel  
**Sicherheit der Einschätzung:** Möglich  
**Datei:** `App/App.xaml.cs`, `Features/Settings/AppSettings.cs`  
**Klasse:** `App`, `AppSettings`  
**Methode:** `OnLaunched`, `Save`  
**Codebereich:** `App.xaml.cs` Zeilen 62–75; `AppSettings.cs` Zeilen 232–250

**Problem:** Es existiert keine AppInstance-, Mutex- oder andere Single-Instance-Koordination. Beide Instanzen verwenden dieselbe `settings.json.tmp` und können gleichzeitig Updates, Scans oder Bereinigungen starten.

**Auswirkung:** Verlorene Einstellungsänderungen, temporäre Dateikollisionen und konkurrierende Systemoperationen.

**Auslöser:** Doppelklick während eines langsamen Starts oder manueller zweiter Start.

**Reproduktion:** Zwei Instanzen öffnen, gleichzeitig Einstellungen ändern und speichern.

**Lösung:** Zweite Aktivierung an die bestehende Instanz weiterleiten oder mindestens Settings-Speicherung prozessübergreifend sperren.

**Risiko der Änderung:** Start-, Aktivierungs- und Deep-Link-Verhalten.

### M-06 – CPU-PerformanceCounter wird beim Start doppelt und unsynchronisiert initialisiert

**Status im Re-Audit:** Behoben. Warm-up und Messung laufen zentral über `HardwareTelemetryService`/`SystemInfoProvider`; parallele Telemetrie wird durch ein `SemaphoreSlim` verhindert.

**Priorität:** Mittel  
**Sicherheit der Einschätzung:** Sehr wahrscheinlich  
**Datei:** `App/MainWindow.xaml.cs`, `Services/HardwareTelemetryService.cs`, `Features/SystemInfo/SystemInfoProvider.cs`  
**Klasse:** `MainWindow`, `HardwareTelemetryService`, `SystemInfoProvider`  
**Methode:** `LoadInitialDataAsync`, `WarmUp`, `WarmUpCpuCounter`, `GetLiveUsage`  
**Codebereich:** `MainWindow.xaml.cs` Zeilen 1378–1382; `HardwareTelemetryService.cs` Zeilen 27–31; `SystemInfoProvider.cs` Zeilen 217–248

**Problem:** Zwei parallele `Task.Run`-Aufrufe erreichen dieselbe ungeschützte `_cpuCounter == null`-Prüfung. Zusätzlich kann der früh gestartete Live-Timer gleichzeitig `GetLiveUsage` aufrufen.

**Auswirkung:** Doppelte Counter-Instanzen, verlorene Handles oder nicht deterministische erste Messwerte.

**Auslöser:** Normaler Programmstart auf einem System, auf dem beide Hintergrundtasks gleichzeitig laufen.

**Reproduktion:** Start mehrfach mit Handle-/Thread-Profiler beobachten.

**Lösung:** Nur einen Warm-up-Pfad behalten und Erstellung sowie Nutzung des Counters gemeinsam synchronisieren.

**Risiko der Änderung:** Erste CPU-Anzeige und Live-Telemetrie.

### M-07 – Sprachwechsel verwendet sprachabhängige Werte aus dem alten Snapshot weiter

**Status im Re-Audit:** Behoben. Ein Sprachwechsel verwirft den sprachabhängigen Systeminfo-Cache und lädt den Snapshot versionsgesichert neu. Schnelle weitere Sprachwechsel können den älteren Refresh überholen, ohne veraltete Werte wieder anzuwenden.

**Priorität:** Mittel  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** `App/MainWindow.xaml.cs`, `Features/SystemInfo/MainWindow.SystemInfo.cs`  
**Klasse:** `MainWindow`  
**Methode:** `RefreshLoadedPagesForLanguageChange`, `ApplySnapshot`  
**Codebereich:** `MainWindow.xaml.cs` Zeilen 589–619; `MainWindow.SystemInfo.cs` Zeilen 82–151

**Problem:** Der Snapshot enthält bereits lokalisierte Werte wie „Nicht verfügbar“, „Aktiv“ oder Batteriestatus. Beim Sprachwechsel wird derselbe Snapshot erneut angewendet, ohne ihn zu invalidieren oder die Werte neutral zu modellieren.

**Auswirkung:** Englische Oberfläche kann deutsche Systemwerte enthalten und umgekehrt.

**Auslöser:** Sprache nach dem Laden der Systeminformationen umstellen.

**Reproduktion:** Systeminfo auf Deutsch öffnen, anschließend auf Englisch wechseln.

**Lösung:** Providerwerte als neutrale Zustände/Enums speichern oder sprachabhängige Bereiche beim Sprachwechsel neu laden.

**Risiko der Änderung:** Systeminfo, Dashboard-Sicherheit und Cacheformat.

### M-08 – Eigenupdate besitzt keinen durchgängigen CancellationToken

**Status im Re-Audit:** Behoben. Prüfung, Download und Validierung akzeptieren und propagieren Abbruchtoken; Fensterabschluss bricht laufende Start-/Updatearbeit ab.

**Priorität:** Mittel  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** `Features/Updates/UpdateService.cs`  
**Klasse:** `UpdateService`  
**Methode:** `CheckForUpdateAsync`, `GetLatestStableReleaseAsync`, `DownloadUpdateAsync`, `VerifySha256Async`  
**Codebereich:** Zeilen 28–59 und 178–271

**Problem:** Netzwerk-, Stream- und Hashoperationen haben zwar HttpClient-Zeitlimits, können aber nicht gezielt beim Schließen des Einstellungsfensters oder der App abgebrochen werden.

**Auswirkung:** Downloads oder Hashprüfungen laufen unnötig weiter; UI-Lebenszyklus und temporäre Dateien sind schwerer kontrollierbar.

**Auslöser:** Fenster/App während eines Eigenupdates schließen.

**Reproduktion:** Langsamen Download starten und Einstellungen schließen.

**Lösung:** CancellationToken durch die gesamte Updatekette reichen und beim Schließen abbrechen.

**Risiko der Änderung:** Update-Dialoge und Installerstart.

### M-09 – Tests sind Debug-Selbsttests und blockieren keinen Release

**Status im Re-Audit:** Behoben. `Tests/WinVora.Tests.csproj` enthält 16 Release-Tests; der GitHub-Workflow führt `dotnet test` vor Publish und Installerbau aus.

**Priorität:** Mittel  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** `Tests/CoreLogicSelfTests.cs`, `.github/workflows/release.yml`  
**Klasse:** `CoreLogicSelfTests`  
**Methode:** `Run`  
**Codebereich:** Selbsttests Zeilen 20–287; Workflow Zeilen 16–100

**Problem:** Die Tests laufen ausschließlich durch `[Conditional("DEBUG")]` in einer gestarteten Debug-App. Es existiert kein Testprojekt und der Release-Workflow führt kein `dotnet test` aus.

**Auswirkung:** Parser-, Sicherheits- oder Storage-Regressionen können trotz grüner Release-Pipeline veröffentlicht werden.

**Auslöser:** Releasebuild oder GitHub-Tag ohne vorherigen manuellen Debugstart.

**Reproduktion:** Einen Selbsttest absichtlich fehlschlagen lassen und den Releasebuild ausführen; er bleibt erfolgreich.

**Lösung:** Kritische reine Logik in ein echtes xUnit-/MSTest-Projekt überführen und im Workflow ausführen.

**Risiko der Änderung:** Gering; Produktionscode muss gegebenenfalls über `InternalsVisibleTo` testbar gemacht werden.

### M-10 – Startfortschritt und tatsächlicher Startablauf widersprechen sich

**Status im Re-Audit:** Behoben. Der sichtbare Startfortschritt beschreibt zwei tatsächlich ausgeführte UI-Phasen; langsamere Update- und Detailabfragen laufen anschließend im Hintergrund.

**Priorität:** Mittel  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** `App/MainWindow.xaml.cs`  
**Klasse:** `MainWindow`  
**Methode:** `MainWindow_Activated`, `LoadInitialDataAsync`, `LoadDeferredStartupDataAsync`  
**Codebereich:** Zeilen 1291–1342 und 1368–1510

**Problem:** `_initialBackgroundRefresh` wird gestartet, aber nirgends abgewartet. Das Overlay verschwindet nach der Mindestzeit von drei Sekunden, selbst wenn weiterhin „Schritt 2 von 4“ angezeigt wird. Kommentare behaupten dagegen, vollständige Startdaten würden vor dem Einblenden abgewartet.

**Auswirkung:** Unplausibler Fortschritt, sichtbares Nachladen und unobservierte Fehler im Hintergrundtask.

**Auslöser:** Systemabfragen dauern länger als drei Sekunden.

**Reproduktion:** WMI verlangsamen und Startfortschritt beobachten.

**Lösung:** Entweder bewusst nur zwei echte Startphasen anzeigen und Hintergrundladen klar kennzeichnen oder den Task bis zum Abschluss der vier Phasen abwarten. Hintergrundtask muss in jedem Fall beobachtet und geloggt werden.

**Risiko der Änderung:** Wahrgenommene Startzeit und Ladebildschirm.

### M-11 – WinGet-Tabellenparser bleibt von Konsolenformatierung abhängig

**Status im Re-Audit:** Offen, aber weiter abgesichert. Der Parser leitet Spalten primär aus der sprachneutralen Trennlinie ab, entfernt ANSI-Steuerzeichen und besitzt Tests für deutsche, englische und französische Varianten. Die Paketextraktion bleibt wegen der verfügbaren WinGet-Schnittstelle grundsätzlich tabellenabhängig.

**Priorität:** Mittel  
**Sicherheit der Einschätzung:** Möglich  
**Datei:** `Features/Updates/WingetDiscoveryService.cs`, `Features/Updates/WingetTableParser.cs`  
**Klasse:** `WingetDiscoveryService`, `WingetTableParser`  
**Methode:** `GetColumnStarts`, `Parse`  
**Codebereich:** Discovery Zeilen 59–103; Parser Zeilen 7–30

**Problem:** Paketfelder werden aus visuell ausgerichteten Spalten ausgeschnitten. Unterschiedliche WinGet-Versionen, breite Unicode-Zeichen, umgebrochene Zeilen oder geänderte Tabellenformate können Spalten verschieben.

**Auswirkung:** Updates werden übersehen oder mit falscher ID/Version dargestellt.

**Auslöser:** Nicht standardmäßige Konsolenausgabe oder Paketnamen mit breiten Zeichen.

**Reproduktion:** Parser mit lokalisierten, umgebrochenen und Unicode-Testtabellen ausführen.

**Lösung:** Wenn WinGet keine strukturierte Ausgabe anbietet, Parser mit mehreren realen Fixtures absichern und unplausible IDs/Versionen ablehnen.

**Risiko der Änderung:** Updateerkennung und Ergebnisnachprüfung.

### M-12 – Bekannte Anwendung wird synchron und gewaltsam im UI-Ablauf beendet

**Status im Re-Audit:** Behoben. WinVora fordert Claude zuerst über `CloseMainWindow` zum geordneten Schließen auf und wartet asynchron. Ein Prozessbaumabbruch erfolgt nur nach einer zweiten, verständlichen Nutzerbestätigung; Abbruch und Fehler werden als eigene Updateergebnisse behandelt.

**Priorität:** Mittel  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** `Features/Updates/UpdateApplicationShutdownService.cs`  
**Klasse:** `UpdateApplicationShutdownService`  
**Methode:** `TryCloseForUpdate`  
**Codebereich:** Zeilen 23–44

**Problem:** Nach bestätigter Warnung wird Claude mit `Kill(entireProcessTree: true)` beendet und synchron bis zu fünf Sekunden gewartet. Der Aufruf liegt im UI-gesteuerten Installationsablauf.

**Auswirkung:** Bis zu fünf Sekunden eingefrorene Oberfläche; nicht gespeicherte Eingaben können verloren gehen. Letzteres wird im Dialog korrekt angekündigt.

**Auslöser:** Claude läuft und reagiert nach dem Kill nicht sofort.

**Reproduktion:** Claude mit blockiertem Unterprozess starten und Update ausführen.

**Lösung:** Zuerst normales Schließen versuchen, asynchron warten und erst nach weiterer Bestätigung hart beenden.

**Risiko der Änderung:** Updatefälle, die wegen gesperrter Dateien bislang nur durch Kill funktionieren.

## E. Niedrige Priorität

### L-01 – Legacy-Cleanup schreibt den Erfolgsmarker auch nach fehlgeschlagenem Löschen

**Status im Re-Audit:** Behoben. Der Marker wird nur geschrieben, wenn jede alte Aufgabe nicht mehr existiert oder erfolgreich gelöscht wurde; Fehler führen zu einem späteren Wiederholungsversuch.

**Priorität:** Niedrig  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** `Infrastructure/LegacyFeatureCleanup.cs`  
**Klasse:** `LegacyFeatureCleanup`  
**Methode:** `RemoveMaintenanceTasksOnceAsync`  
**Codebereich:** Zeilen 14–23

**Problem:** Fehler beim Löschen geplanter Aufgaben werden intern gefangen; anschließend wird trotzdem der Erfolgsmarker geschrieben.

**Auswirkung:** Ein transienter Fehler wird nie erneut versucht. Veraltete Wartungsaufgaben können dauerhaft bestehen bleiben.

**Auslöser:** Die Aufgabenplanung ist vorübergehend nicht erreichbar oder der Benutzer besitzt nicht die erforderlichen Rechte.

**Reproduktion:** Zugriff auf die Aufgabenplanung verweigern, Cleanup starten und anschließend Marker sowie Aufgabenbestand prüfen.

**Lösung:** Den Marker nur nach erfolgreicher Prüfung beziehungsweise erfolgreicher Entfernung schreiben; andernfalls einen getrennten Fehlerstatus speichern und später erneut versuchen.

**Risiko der Änderung:** Bei zu aggressivem Wiederholen könnten bei jedem Start unnötige Aufgabenplanerzugriffe entstehen. Deshalb Wiederholung begrenzen.

### L-02 – Sensorfehler werden vollständig verschluckt

**Status im Re-Audit:** Behoben. Sensor-/Hardwarefehler werden einmalig protokolliert; fehlerhafte Hardwareobjekte werden für weitere Ticks übersprungen.

**Priorität:** Niedrig  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** `Services/HardwareMonitorService.cs`  
**Klasse:** `HardwareMonitorService`  
**Methode:** `WarmUp`, `GetReadings`  
**Codebereich:** Zeilen 74–78 und 86–160

**Problem:** Nicht unterstützte Hardware ist normal, aber auch unerwartete Bibliotheks-, Treiber- oder Objektfehler werden durch leere Catch-Blöcke vollständig verschluckt.

**Auswirkung:** Fehlende oder falsche Sensorwerte sind im Supportbericht nicht erklärbar; echte Regressionen sehen wie normale Hardwareeinschränkungen aus.

**Auslöser:** LibreHardwareMonitor wirft beim Öffnen, Aktualisieren oder Lesen einzelner Geräte eine Exception.

**Reproduktion:** Hardwarezugriff simuliert fehlschlagen lassen oder eine nicht unterstützte Sensorkonfiguration verwenden und das Protokoll prüfen.

**Lösung:** Erwartete Hardwareausnahmen einmalig auf Debug-/Warning-Ebene protokollieren und den betroffenen Sensor weiterhin als nicht verfügbar behandeln.

**Risiko der Änderung:** Unbegrenztes Logging könnte das Protokoll fluten. Meldungen müssen deshalb pro Gerät und Fehlerart gedrosselt werden.

### L-03 – Lokalisierungsvalidator prüft leere Übersetzungen nicht tatsächlich

**Status im Re-Audit:** Behoben. `Test-Localization.ps1` erkennt leere deutsche und englische Werte und lässt den Build bei Fehlern scheitern.

**Priorität:** Niedrig  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** `scripts/Test-Localization.ps1`  
**Klasse:** Nicht anwendbar; PowerShell-Prüfskript  
**Methode:** Hauptskript  
**Codebereich:** Zeilen 10–20 und 112–119

**Problem:** `$emptyTranslations` wird angelegt und später ausgewertet, aber nie befüllt. Die deutsche Wortliste erfasst zudem nur einen begrenzten Ausschnitt.

**Auswirkung:** Die CI kann trotz leerer Werte oder sichtbarer fest eingebauter deutscher Texte erfolgreich sein.

**Auslöser:** Eine Übersetzung wird leer gelassen oder ein nicht in der Wortliste enthaltener deutscher Text direkt in C#/XAML ergänzt.

**Reproduktion:** Einen Übersetzungswert leeren oder einen neuen deutschen UI-Text einbauen und das Skript ausführen.

**Lösung:** Alle Katalogwerte beider Sprachen explizit auf Leerwerte prüfen und XAML/C# über bekannte UI-Eigenschaften beziehungsweise einen Allowlist-basierten Scanner untersuchen.

**Risiko der Änderung:** Zu breite Textsuche erzeugt False Positives in Kommentaren, Logs und technischen Bezeichnern. Der Scanner braucht klare Kontexte und Ausnahmen.

### L-04 – Verbliebene fest eingebaute deutsche UI-Texte

**Status im Re-Audit:** Behoben. Die identifizierten dynamischen Update-, Storage- und Einstellungswerte liegen im Übersetzungskatalog; der Release-Build prüft aktuell 352 vollständige Schlüssel sowie typische C#- und XAML-Kontexte.

**Priorität:** Niedrig  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** unter anderem `Features/SystemInfo/MainWindow.SystemInfo.cs`, `Features/Uninstall/MainWindow.Uninstall.cs`, `Features/Storage/StorageService.cs`  
**Klasse:** `MainWindow`, `StorageService`  
**Methode:** Systeminfo-Ladepfad, Deinstallationsfehlerpfad, Storage-Ergebnisaufbau  
**Codebereich:** Systeminfo Zeilen 74–80; Deinstallation Zeilen 105–113; Storage Zeilen 233–235

**Problem:** Einige Fehler-, Lade- und Aktionswerte werden direkt auf Deutsch erzeugt und umgehen den Übersetzungskatalog.

**Auswirkung:** Im englischen Modus erscheinen gemischte Sprachen; automatisierte Katalogprüfungen erfassen diese Texte nicht zuverlässig.

**Auslöser:** Der entsprechende Lade-, Fehler- oder Storage-Aktionspfad wird bei englischer Sprache angezeigt.

**Reproduktion:** Englisch aktivieren und Systeminfofehler, Deinstallationsfehler sowie den betreffenden Storage-Status auslösen.

**Lösung:** Fachlogik soll neutrale Statuscodes liefern; sichtbare Texte erst in der UI über `Localization` auflösen.

**Risiko der Änderung:** Bestehende Vergleiche könnten aktuell lokalisierte Texte als Zustandswert verwenden. Vor der Umstellung müssen alle Aufrufer gesucht werden.

### L-05 – Ungenutzter Storage-Codepfad

**Status im Re-Audit:** Behoben. Der ungenutzte `DeleteCategoriesAsync`-Pfad wurde entfernt; die UI besitzt nur noch den zentralen produktiven Löschablauf.

**Priorität:** Niedrig  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** `Features/Storage/MainWindow.Storage.Operations.cs`  
**Klasse:** `MainWindow`  
**Methode:** `DeleteCategoriesAsync`  
**Codebereich:** Zeilen 19–57

**Problem:** Die Methode ist nicht referenziert und weicht bereits vom tatsächlich verwendeten Löschpfad ab.

**Auswirkung:** Wartung kann versehentlich am falschen Codepfad erfolgen; Sicherheits- und Abbruchkorrekturen laufen auseinander.

**Auslöser:** Ein Entwickler findet über die Methodensuche den ungenutzten Pfad und ändert ihn in der Annahme, er sei produktiv.

**Reproduktion:** Projektweit nach Aufrufern von `DeleteCategoriesAsync` suchen; es existiert kein produktiver Aufruf.

**Lösung:** Nach erneuter Referenzprüfung entfernen oder den aktiven Ablauf bewusst darauf konsolidieren. Wegen der aktuell unterschiedlichen Semantik ist Entfernen risikoärmer.

**Risiko der Änderung:** Reflection- oder XAML-Aufrufe wären theoretisch möglich, wurden im Projekt aber nicht gefunden. Vor Entfernung Release-Build und Storage-UI testen.

### L-06 – Sehr große UI-Orchestrierung bleibt schwer testbar

**Status im Re-Audit:** Offen, aber reduziert. Operation-Controller, Systemzugriffsinterfaces, Provider und Feature-Partialdateien verkleinern die fachliche Kopplung; `MainWindow` bleibt trotzdem der zentrale UI-Orchestrator.

**Priorität:** Niedrig  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** insbesondere `App/MainWindow.xaml.cs` und große partielle Featuredateien  
**Klasse:** `MainWindow`  
**Methode:** projektweit zahlreiche Navigations-, Lade- und Renderpfade  
**Codebereich:** `MainWindow.xaml.cs` mit rund 2.400 Zeilen; Settings rund 1.235; Update-Installation rund 1.028

**Problem:** Die Aufteilung in Partial-Dateien verbessert die Ablage, ändert aber nicht die starke Kopplung an eine einzige `MainWindow`-Instanz mit UI-, Zustands- und Orchestrierungsverantwortung.

**Auswirkung:** Fachabläufe sind schwer isoliert testbar; Änderungen an Navigation oder Zuständen besitzen eine große Regressionsfläche.

**Auslöser:** Neue Funktionen benötigen Zugriff auf mehrere Controls, globale Flags und bestehende Ladepfade.

**Reproduktion:** Einen Update- oder Navigationsablauf ohne echtes `MainWindow` testen; viele direkte Control-Abhängigkeiten verhindern dies.

**Lösung:** Kein Komplettumbau. Nur reine Operationen und Zustandsautomaten schrittweise in kleine Koordinatoren verschieben und über schmale Interfaces anbinden.

**Risiko der Änderung:** Ein großflächiger MVVM-Umbau würde derzeit mehr Regressionen als Nutzen erzeugen. Refactorings müssen klein und funktional motiviert bleiben.

### L-07 – Abhängigkeiten sind teilweise nicht auf dem neuesten Stand

**Status im Re-Audit:** Teilweise behoben. Windows App SDK wurde für die Beta kontrolliert auf 2.4.0 und `System.Management` auf 10.0.11 aktualisiert. Release-Build, 23 Tests und self-contained Publish sind sauber; die übrigen `System.*`-Pakete bleiben bewusst auf der zum .NET-8-Ziel passenden Hauptversion, statt blind auf 10.x gehoben zu werden.

**Priorität:** Niedrig  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** `WinVora.csproj`  
**Klasse:** Nicht anwendbar; Projektkonfiguration  
**Methode:** NuGet-Paketreferenzen  
**Codebereich:** Zeilen 73–80

**Problem:** NuGet meldet unter anderem Windows App SDK 2.4.0 statt 2.2.0 als neuere Version. Weitere `System.*`-Pakete besitzen neuere Major-Versionen.

**Auswirkung:** Langfristig fehlen Fehlerkorrekturen und Supportverbesserungen; aktuell besteht laut NuGet-Audit jedoch keine bekannte Schwachstelle.

**Auslöser:** Neue Windows-/SDK-Versionen oder künftige Abhängigkeiten setzen neuere APIs voraus.

**Reproduktion:** `dotnet list package --outdated --include-transitive` ausführen.

**Lösung:** Windows App SDK separat in einem Beta-Zweig aktualisieren und vollständig testen. `System.*` 10.x nicht blind in das .NET-8-Projekt übernehmen.

**Risiko der Änderung:** Windows App SDK-Updates können XAML-, Runtime- und Deploymentverhalten verändern; Major-Upgrades können Binärkompatibilität brechen.

### L-08 – GitHub Actions sind nur auf bewegliche Major-Tags fixiert

**Status im Re-Audit:** Behoben. Checkout, .NET-Setup, Artefakttransfer und Release-Action sind auf konkrete Commit-SHAs fixiert.

**Priorität:** Niedrig  
**Sicherheit der Einschätzung:** Möglich  
**Datei:** `.github/workflows/release.yml`  
**Klasse:** Nicht anwendbar; GitHub-Actions-Workflow  
**Methode:** `uses`-Schritte  
**Codebereich:** Zeilen 20–23, 72, 86 und 93

**Problem:** Actions werden über `@v4` beziehungsweise `@v2` statt über unveränderliche Commit-SHAs geladen.

**Auswirkung:** Ein kompromittierter oder unerwartet veränderter Major-Tag könnte Releaseartefakte beeinflussen. Das Risiko ist möglich, aber nicht als konkrete Kompromittierung belegt.

**Auslöser:** Der referenzierte Action-Tag wird upstream verändert oder kompromittiert.

**Reproduktion:** Workflowdatei prüfen; die Referenzen sind nicht auf Commit-SHAs festgeschrieben.

**Lösung:** Kritische Release-Actions auf geprüfte Commit-SHAs fixieren und Aktualisierungen beispielsweise über Dependabot verwalten.

**Risiko der Änderung:** Fixierte SHAs erhalten keine automatischen Sicherheitsupdates und müssen aktiv gepflegt werden.

### L-09 – Installer ist nicht signiert

**Status im Re-Audit:** Offen mit vorbereiteter Pipeline. `scripts/Sign-Artifact.ps1` signiert Anwendung und Installer optional aus geschützten CI-Secrets, verifiziert die Signatur und entfernt Zertifikat sowie temporäre PFX-Datei. Ohne erworbenes vertrauenswürdiges Zertifikat bleiben Artefakte bewusst unsigniert; SHA-256 wird weiterhin veröffentlicht.

**Priorität:** Niedrig aus Codesicht, hoch für Nutzervertrauen  
**Sicherheit der Einschätzung:** Bestätigt  
**Datei:** `Packaging/WinVoraSetup.iss` und `.github/workflows/release.yml`  
**Klasse:** Nicht anwendbar; Installer- und Releasekonfiguration  
**Methode:** Installer-Build und Releaseartefakterzeugung  
**Codebereich:** Signierschritt beziehungsweise `SignTool`-Konfiguration fehlt

**Problem:** Der Installer besitzt keine Authenticode-Signatur.

**Auswirkung:** Windows zeigt einen unbekannten Herausgeber beziehungsweise SmartScreen-Warnungen; normale Nutzer können die Herkunft nicht bequem prüfen. SHA-256 hilft nur bei manueller Gegenprüfung.

**Auslöser:** Ein Endnutzer lädt und startet den Installer auf einem System ohne bestehende Reputation.

**Reproduktion:** Frischen Releaseinstaller auf einem sauberen Windows-System starten und die Signaturdetails prüfen.

**Lösung:** Vor breiter Verteilung eine dokumentierte Signierungsstrategie einführen. Bis dahin Prüfsumme und exakte Downloadquelle sichtbar erklären.

**Risiko der Änderung:** Zertifikat, Schlüsselverwaltung und CI-Secrets werden sicherheitskritische neue Komponenten. Eine unsaubere Signierpipeline wäre gefährlicher als der aktuelle transparente Zustand.

## F. Security

Die Sicherheitsbasis ist nach der Roadmap gut: Hauptprozess ohne dauerhafte Erhöhung, begrenzter Admin-Helper, exakte Kategorie- und Pfadprüfung, Reparse-Point-Schutz, absolute Systempfade, robuster Prozessrunner, konservativer Firewallstatus, HTTPS-/Host-/Hash-Prüfung des Eigenupdates, anonymisierte Diagnose und keine gefundenen Secrets. Die frühere Gefahr weiterlaufender Storage-Hilfsprozesse ist behoben. Das verbleibende sicherheitsnahe Release-Thema ist nicht der Anwendungscode, sondern die fehlende Authenticode-Signatur des Installers.

## G. Performance

Die Live-Sensormessung wurde vollständig vom UI-Thread gelöst, zentral serialisiert und kurz zwischengespeichert. Storage-Parallelität ist begrenzt, Reparse Points werden übersprungen, Programmsymbole werden verzögert geladen und Timer können nicht überlappen. Der PC-Check lädt nur noch die tatsächlich benötigten Systembereiche. Langsame native Sensorabfragen werden erkannt, protokolliert und anschließend mit einem längeren Cacheintervall ausgeführt. Reales Profiling auf schwächerer Hardware bleibt trotzdem ein externer Praxistest und ist **nicht abschließend verifizierbar**.

## H. Architektur

Operation-Controller, `SystemAccess`-Schnittstellen, zentrale Telemetrie, getrennte Provider und ein echtes Testprojekt haben die Architektur deutlich verbessert. Der neue sprachabhängige Systeminfo-Refresh und die Update-Anwendungsbeendigung liegen als kleine, zweckgebundene MainWindow-Teilmodule vor. Die Anwendung bleibt UI-zentriert; ein riskanter Komplettumbau ist weiterhin nicht gerechtfertigt.

## I. Release-Risiken

- WinGet-Paketextraktion bleibt trotz sprachneutraler Spaltenerkennung und zusätzlicher Fixtures abhängig von einer tabellarischen Konsolenausgabe.
- Nicht signierter Installer erzeugt SmartScreen-Vertrauenshürden.
- ARM64 wird bewusst nicht unterstützt; der Download muss eindeutig als x64 gekennzeichnet bleiben.
- Ein echter Test auf einem frischen Windows-PC ohne Entwicklungswerkzeuge wurde im statischen Audit nicht durchgeführt und ist **nicht abschließend verifizierbar**.

## Wichtigste verbleibende Punkte

| Reihenfolge | Problem | Priorität | Auswirkung | Aufwand |
|---:|---|---|---|---|
| 1 | Frischer-PC-Test noch nicht praktisch durchgeführt | Release-Risiko | Runtime-/Installerprobleme könnten unentdeckt bleiben | Mittel |
| 2 | Installer nicht signiert | Niedrig im Code, hoch fürs Vertrauen | SmartScreen zeigt unbekannten Herausgeber | Extern/Kosten |
| 3 | WinGet-Tabellenparser | Mittel, möglich | Neue Ausgabeformate können die Erkennung beeinträchtigen | Mittel |
| 4 | Windows App SDK 2.4 braucht mehrtägigen Beta-Praxistest | Release-Risiko | XAML-/Runtime-Regressionen könnten erst praktisch auffallen | Mittel |
| 5 | UI-Orchestrierung bleibt groß | Niedrig | Höhere Regressions- und Wartungsfläche | Laufend |
| 6 | RAM-Verbrauch nur auf einem leistungsfähigen System gemessen | Optimierung | Potenzial auf schwächerer Hardware unbekannt | Mittel |
| 7 | Signier-Secrets und Zertifikatsbetrieb noch nicht erprobt | Release-Risiko | Erste signierte Pipeline könnte falsch konfiguriert sein | Mittel |
| 8 | Hersteller-Updateabläufe bleiben extern | Möglich | Installer können sich trotz korrekter WinGet-Steuerung ungewöhnlich verhalten | Laufend |
| 9 | ARM64 nicht unterstützt | Bewusste Einschränkung | App läuft nur als x64-Anwendung | Hoch |
| 10 | Visuelle DPI-Matrix benötigt manuelle Kontrolle | Niedrig | Seltene Layoutfehler bei 175–200 % möglich | Klein–Mittel |

## Fix-Roadmap

### Abgeschlossen

1. Alle ursprünglichen Punkte der Phasen 1 und 2.
2. Hintergrundtelemetrie, begrenzte PC-Check-Abfragen und realer Startfortschritt aus Phase 3.
3. Testbare Prozessschnittstellen, Operation-Controller, MSTest-Projekt und CI-Test-Gate aus Phase 4.
4. Erweiterter Lokalisierungsvalidator, Legacy-Marker, Sensorlogging, Storage-Bereinigung, Action-SHAs und dokumentierte Signierungsstrategie aus Phase 5.

### Noch vor einem stabilen Release offen

1. Die in `Docs/RELEASE-QA.md` definierte Matrix auf einem frischen Windows-10/11-x64-PC ohne Entwicklungswerkzeuge praktisch durchführen.
2. Windows App SDK 2.4 und die neuen Updatepfade mehrere Tage in der Beta verwenden.
3. Eine vertrauenswürdige Authenticode-Signatur aktivieren, sobald Zertifikat, Budget und sichere Schlüsselverwaltung vorhanden sind.

### Nachgelagert

1. Weitere reale WinGet-Ausgabevarianten als Fixtures aufnehmen, sobald sie beobachtet werden.
2. RAM und Sensorticks auf schwächerer Hardware mit dem dokumentierten Profilingplan messen.
3. UI-Orchestrierung nur bei konkretem funktionalem Bedarf weiter aus `MainWindow` herauslösen.

## Gesamtbewertung

| Kategorie | Bewertung | Begründung |
|---|---:|---|
| Stabilität | 9/10 | Prozess-, Abbruch-, Navigations- und Mehrfachinstanzpfade sind abgesichert; auch Claude wird zuerst geordnet geschlossen. |
| Sicherheit | 9/10 | Least Privilege, Allowlist, Prozessrunner, konservative Security-Auswertung und Updateprüfung sind robust; Installer ist noch unsigniert. |
| Performance | 8/10 | Telemetrie läuft im Hintergrund, ist serialisiert und gecacht; reales Low-End-Profiling und RAM-Optimierung bleiben offen. |
| Architektur | 7/10 | Controller und Schnittstellen verbessern die Testbarkeit deutlich; `MainWindow` bleibt der zentrale Orchestrator. |
| Codequalität | 8/10 | Nullable, Analyzer, Validatoren, zentrale Logger und entfernte tote Pfade ergeben einen sauberen Build. |
| Wartbarkeit | 8/10 | Gute Featurestruktur, 23 Release-Tests und zweckgebundene Teilmodule; UI-gebundene Partialklassen bleiben. |
| Fehlerbehandlung | 9/10 | Timeouts, Exitcodes, Abbruchtoken, Nutzerabbruch und Logging sind vereinheitlicht; externe Ausgabeformate bleiben ein Risiko. |
| UI-Zuverlässigkeit | 8/10 | Responsive Layouts, Hintergrundarbeit und Navigation wurden praktisch geprüft und korrigiert. |
| Kompatibilität | 8/10 | Windows 10/11 x64, PerMonitorV2 und self-contained Publish sind sauber konfiguriert; ARM64 fehlt bewusst. |
| Release-Reife | 9/10 | Release-Build, 23 Tests und Publish-Readiness sind sauber; Frisch-PC-Praxistest und Signatur begrenzen die Bewertung. |

**Gesamtdurchschnitt: 8,4/10** (vor dem Re-Audit: 6,6/10).

## Release-Entscheidung

**RELEASE-BEREIT MIT EINSCHRÄNKUNGEN**.

Der aktuelle Stand ist für eine öffentliche Beta release-bereit. Die früheren hohen Release-Blocker sowie M-07 und M-12 sind behoben und durch Build-, Test- oder Ablaufprüfungen abgesichert. Vor einem stabilen Release bleiben der praktische Test auf einem frischen x64-Windows-System und ein mehrtägiger Beta-Test des Windows App SDK 2.4 erforderlich. Solange kein vertrauenswürdiges Zertifikat konfiguriert ist, muss der Installer transparent als „Unbekannter Herausgeber“ erklärt werden.

## Nachweis der technischen Prüfung

- Release-Testlauf erfolgreich: 23 von 23 Tests bestanden, keine übersprungen.
- Release-Build erfolgreich und ohne Compilerwarnungen.
- Self-contained Publish-Prüfung erfolgreich: Version `0.8.5-beta.4`, 504 Dateien, 198,4 MB, keine PDB im Endnutzerordner.
- Lokalisierungsprüfung meldet 352 vollständige Schlüssel.
- UI-Qualitätsskript erfolgreich.
- Releaseworkflow führt Tests vor Publish und Installerbau aus.
- Kritische GitHub Actions sind auf konkrete Commit-SHAs fixiert.
- `dotnet list package --vulnerable --include-transitive`: keine bekannten anfälligen Pakete.
- Windows App SDK 2.4.0 und `System.Management` 10.0.11 sind in der Beta enthalten; unpassende `System.*`-Major-Upgrades wurden bewusst nicht erzwungen.
- Keine gefundenen API-Keys, Tokens, Passwörter oder persönlichen Entwicklerpfade im geprüften Quellbestand.
