; ============================================================
; WinVora - Inno Setup Installer-Skript
; ============================================================
; Voraussetzung: Inno Setup muss installiert sein (kostenlos):
; https://jrsoftware.org/isdl.php
;
; Vor dem Kompilieren:
; 1. Führe publish.bat aus, damit der "publish"-Ordner aktuell ist.
; 2. Öffne diese Datei mit dem "Inno Setup Compiler".
; 3. Drücke F9 (oder Build -> Compile).
; 4. Der fertige Installer liegt danach in "installer_output".
; ============================================================

#define MyAppName "WinVora"
; Liest die zentrale Produktversion aus der zuvor veröffentlichten EXE.
; Dadurch muss die Version nur noch in WinVora.csproj geändert werden.
#define MyAppVersion GetStringFileInfo("..\publish\WinVora.exe", "ProductVersion")
#define MyAppPublisher "WinVora"
#define MyAppExeName "WinVora.exe"

; Feste, eindeutige ID für dieses Programm (nicht ändern!) - Windows nutzt
; das, um bei künftigen Updates zu erkennen, dass es dieselbe App ist.
#define MyAppId "{{A6F1C2B4-9E3D-4F7A-8B21-3D4E5F6A7B8C}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; Lässt den Nutzer den Installationsort im Setup-Assistenten selbst wählen
; (Seite "Zielordner wählen" wird automatisch angezeigt, solange
; DisableDirPage nicht auf "yes" steht).
DisableDirPage=no

OutputDir=..\installer_output
OutputBaseFilename=WinVora-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Falls WinVora beim Update gerade läuft, bietet der Installer automatisch
; an, es zu schließen, statt einfach mit einem Fehler abzubrechen.
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

; Weitere Sprachen (optional einkommentieren, falls beim Inno-Setup-Install
; die entsprechenden Sprachpakete mit heruntergeladen wurden):
; Name: "french"; MessagesFile: "compiler:Languages\French.isl"
; Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
; Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Nimmt automatisch ALLES aus dem publish-Ordner mit (die .exe und die
; wenigen Restdateien wie resources.pri, falls vorhanden).
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; "skipifsilent" bewusst entfernt: beim Auto-Update über die App selbst läuft
; der Installer im Silent-Modus, und genau dann soll WinVora automatisch
; wieder starten (nicht wie sonst üblich beim stillen Modus übersprungen werden).
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall
