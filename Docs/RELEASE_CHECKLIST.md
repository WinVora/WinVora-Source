# WinVora 0.8.5-beta.3 – Beta-Release-Checkliste

- Projektversion: `0.8.5-beta.3`
- Installername: `WinVora-Setup-0.8.5-beta.3.exe`
- GitHub-Veröffentlichung ausdrücklich als Prerelease markieren
- Release-Build: `dotnet publish WinVora.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o publish`
- Installer: `Packaging/WinVoraSetup.iss` anschließend mit Inno Setup kompilieren
- README, Endnutzer-Changelog und Installerdateiname auf dieselbe Version prüfen
- Programm-Update, Abbruch, Deinstallation, Dateien und Verlaufsfilter manuell testen
- SHA-256-Prüfsumme des fertigen Installers für das GitHub-Release erzeugen
