# WinVora – technischer Code-Audit

Stand: 20. August 2026  
Auditbasis: aktueller lokaler Arbeitsstand von `WinVora-Source` einschließlich nicht committeter Änderungen  
Version: `0.8.5-beta.3`

## A. Executive Summary

WinVora ist eine unverpackte WinUI-3-Anwendung auf .NET 8 für x64. Die Anwendung ist self-contained, läuft grundsätzlich mit normalen Benutzerrechten und erhöht nur eng begrenzte Operationen. Die fachlichen Bereiche sind in partielle `MainWindow`-Dateien, Services, Provider und neue Operation-Controller aufgeteilt. Ein klassisches MVVM- oder DI-Modell wird nicht verwendet; UI-Zustand und Orchestrierung liegen weiterhin überwiegend in `MainWindow`.

Positiv bewertet wurden insbesondere:

- `asInvoker` als Standard und ein begrenzter Admin-Helper für Storage-Aktionen;
- exakte Allowlist und Reparse-Point-Schutz für `Windows.old` und `$WINDOWS.~BT`;
- blockierte skriptbasierte oder offensichtlich manipulierte Deinstallationsbefehle;
- HTTPS-, Host- und SHA-256-Prüfung des WinVora-Eigenupdates;
- Prozessbaum-Abbruch und Zeitlimits bei regulären WinGet-Installationen;
- getrennte Defender-, Firewall-, TPM- und BitLocker-Abfragen mit Fallbacks;
- begrenzte Logrotation und Anonymisierung des Supportberichts;
- erfolgreicher Release-Publish ohne Compilerwarnungen;
- keine aktuell bekannten verwundbaren direkten oder transitiven NuGet-Pakete.

Es wurde keine bestätigte kritische Sicherheitslücke gefunden. Vor einem stabilen Release sollten jedoch vier Findings mit hoher Priorität behoben werden. Besonders relevant sind ein fehlerhafter Timeout bei geschützten Storage-Hilfsprozessen, synchrone Sensorarbeit auf dem UI-Thread, ein zu optimistischer Firewallstatus und eine WinGet-Fehlerbehandlung, die bestimmte fehlgeschlagene Prüfungen als „keine Updates“ darstellen kann.

## B. Kritische Probleme

Keine bestätigten kritischen Findings.

## C. Hohe Priorität

### H-01 – Geschützte Storage-Hilfsprozesse können hängen oder parallel zur Löschung weiterlaufen

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

Die Sicherheitsbasis ist insgesamt vernünftig: Hauptprozess ohne dauerhafte Erhöhung, begrenzter Storage-Helper, exakte Kategorie- und Pfadprüfung, Reparse-Point-Schutz, sichere Eigenupdate-Prüfung und keine gefundenen Secrets. Die wichtigsten Sicherheitskorrekturen sind der konservative Firewallstatus und das robuste Beenden der elevierten Storage-Systemprozesse. Zusätzlich sollten Systemprogramme über absolute System32-Pfade gestartet werden, um mögliches PATH-Hijacking weiter zu reduzieren.

## G. Performance

Das größte konkrete Performanceproblem ist die Live-Sensormessung auf dem UI-Thread. Danach folgen die doppelte PerformanceCounter-Initialisierung und manuelle PC-Check-WMI-Abfragen, die zwar Zeitlimits besitzen, aber große Ergebnislisten vollständig materialisieren. Positiv sind begrenzte Storage-Parallelität, Reparse-Point-Schutz, verzögertes Laden von Programmsymbolen und Überlappungsschutz des Live-Timers.

## H. Architektur

Die neuen Operation-Controller und `SystemAccess`-Schnittstellen sind ein sinnvoller Schritt. Die Architektur ist jedoch weiterhin UI-zentriert: `MainWindow` besitzt Navigation, Zustände, Dialoge, Datenrendering und einen Teil der fachlichen Orchestrierung. Für die nächste Stufe sollten keine pauschalen Frameworks eingeführt werden. Sinnvoll sind kleine, testbare Koordinatoren für Navigation, Telemetrie, Updateerkennung und Storage-Prozesse.

## I. Release-Risiken

- Falsche grüne Firewallanzeige.
- Storage-Systemprozesse können nach Timeout weiterlaufen.
- WinGet-Fehler können als „keine Updates“ erscheinen.
- UI-Stottern durch Sensorzugriffe.
- Kein echter automatisierter Test-Gate im Releaseworkflow.
- Nicht signierter Installer erzeugt SmartScreen-Vertrauenshürden.
- ARM64 wird bewusst nicht unterstützt; der Download muss eindeutig als x64 gekennzeichnet bleiben.
- Ein echter Test auf einem frischen Windows-PC ohne Entwicklungswerkzeuge wurde im statischen Audit nicht durchgeführt und ist **nicht abschließend verifizierbar**.

## Top-10-Probleme

| Reihenfolge | Problem | Priorität | Auswirkung | Aufwand |
|---:|---|---|---|---|
| 1 | Storage-Hilfsprozesse/Timeout | Hoch | Hängen und konkurrierende Löschung | Mittel |
| 2 | Live-Sensoren im UI-Thread | Hoch | Regelmäßiges UI-Stottern | Klein–Mittel |
| 3 | Firewallstatus zu optimistisch | Hoch | Falsche Sicherheitsanzeige | Klein–Mittel |
| 4 | WinGet-Fehler als leere Liste | Hoch | Kernfunktion liefert falsches Ergebnis | Mittel |
| 5 | Storage-Abbruchtoken fehlt | Mittel | Bereinigung nicht sauber abbrechbar | Klein |
| 6 | Navigation startet versteckte Arbeit | Mittel | Inkonsistenter Zustand und I/O | Mittel |
| 7 | Interne Diagnose kann hängen | Mittel | Dauerhafter Ladezustand/Prozessrest | Klein |
| 8 | Aktivierungsstatus ohne Produktfilter | Mittel | Falsche Windows-Aktivierungsanzeige | Klein |
| 9 | Doppelte Counter-Initialisierung | Mittel | Handle-/Messwert-Race | Klein |
| 10 | Keine automatischen Release-Tests | Mittel | Regressionen passieren Pipeline | Mittel |

## Fix-Roadmap

### Phase 1 – Sofort beheben

1. `RunHiddenCommand` asynchron, mit Streamdrain, Timeout, Kill und Exitcodeprüfung implementieren.
2. Rückgabewerte von `takeown` und `icacls` vor der geschützten Löschung prüfen.
3. Live-Telemetrie vollständig vom UI-Thread lösen.
4. Firewallprofile vollständig und konservativ bewerten.
5. WinGet-Exitcode unabhängig vom `stderr` als Fehler behandeln.

### Phase 2 – Vor Release

1. Storage-CancellationToken im aktiven Löschpfad weiterreichen.
2. Navigation auf `TrySetPage` beziehungsweise zentral deaktivierte Navigation umstellen.
3. Interne Diagnose mit denselben Timeouts wie Produktionsabfragen absichern.
4. Windows-Aktivierung auf Windows-Application-ID filtern.
5. PerformanceCounter-Warm-up vereinheitlichen und synchronisieren.
6. Eigenupdate durchgängig abbrechbar machen.
7. Mehrfachinstanzen koordinieren.

### Phase 3 – Performance

1. Messkörper der Hardwaretelemetrie im Hintergrund ausführen.
2. PC-Check-Abfragen auf benötigte Felder und begrenzte Ergebnisse reduzieren.
3. Startfortschritt an tatsächliche Phasen anpassen.
4. Live-Telemetrie mit realen schwachen Systemen profilieren.

### Phase 4 – Architektur und Tests

1. WinGet-Discovery hinter eine testbare Prozessschnittstelle legen.
2. Storage-Systembefehle hinter einen testbaren ProcessRunner legen.
3. Neutrale Enums/Modelle statt lokalisierter Providerwerte verwenden.
4. xUnit-/MSTest-Projekt für Parser, Versionslogik, Security-Evaluator, Storage-Allowlist und Diagnose-Anonymisierung anlegen.
5. `dotnet test` vor Publish in GitHub Actions ausführen.

### Phase 5 – Polish

1. Restliche feste deutsche Texte zentralisieren.
2. Lokalisierungsvalidator um leere Werte und mehr UI-Kontexte erweitern.
3. Legacy-Cleanup-Marker korrigieren.
4. Sensorfehler einmalig protokollieren.
5. Toten Storage-Code und überflüssige Leerzeilen entfernen.
6. Actions optional auf Commit-SHAs fixieren.
7. Signierungsstrategie dokumentieren.

## Gesamtbewertung

| Kategorie | Bewertung | Begründung |
|---|---:|---|
| Stabilität | 7/10 | Controller und Fehlerbehandlung sind gut, einige Prozess- und Abbruchpfade bleiben unsauber. |
| Sicherheit | 7/10 | Gute Least-Privilege- und Updatebasis; Firewallanzeige und Systemprozessstart müssen nachgebessert werden. |
| Performance | 6/10 | Start wurde optimiert, Live-Sensoren können den UI-Thread jedoch regelmäßig blockieren. |
| Architektur | 6/10 | Gute neue Services/Controller, aber weiterhin starke `MainWindow`-Kopplung. |
| Codequalität | 7/10 | Nullable und Analyzer aktiv, nachvollziehbare Kommentare; tote Pfade und große Klassen bleiben. |
| Wartbarkeit | 6/10 | Ordnerstruktur ist sinnvoll, fachlicher Zustand bleibt stark UI-gebunden. |
| Fehlerbehandlung | 7/10 | Zentrales Logging und viele Fallbacks; einzelne Timeouts und leere Catches sind problematisch. |
| UI-Zuverlässigkeit | 7/10 | Responsive Maßnahmen vorhanden; Telemetrie, Navigation und Startfortschritt können inkonsistent wirken. |
| Kompatibilität | 7/10 | Windows 10/11 x64 und PerMonitorV2 sind sauber konfiguriert; ARM64 fehlt bewusst. |
| Release-Reife | 6/10 | Build/Publish funktionieren, aber vier hohe Findings und fehlende CI-Tests verhindern eine uneingeschränkte Freigabe. |

## Release-Entscheidung

**NICHT RELEASE-BEREIT** für einen neuen stabilen Release.

Eine Beta kann für kontrollierte Tests weiterverwendet werden. Vor einem stabilen Release müssen mindestens H-01 bis H-04 behoben und anschließend die betroffenen Storage-, WinGet-, Security- und Telemetriepfade praktisch getestet werden. Danach ist eine erneute, fokussierte Releaseprüfung erforderlich.

## Nachweis der technischen Prüfung

- Release-Publish erfolgreich.
- `WinVora.exe` im Publish vorhanden.
- 501 Publish-Dateien, insgesamt rund 200 MB.
- Build ohne Compilerwarnungen.
- Lokalisierungsprüfung meldet 320 Schlüssel.
- UI-Qualitätsskript erfolgreich.
- `dotnet list package --vulnerable --include-transitive`: keine bekannten anfälligen Pakete.
- `dotnet list package --outdated --include-transitive`: Updates vorhanden, aber kein akuter Vulnerability-Zwang.
- Keine gefundenen API-Keys, Tokens, Passwörter oder persönlichen Entwicklerpfade im geprüften Quellbestand.
