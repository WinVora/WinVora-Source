@echo off
setlocal

echo ============================================
echo   WinVora - Build fuer den Installer erstellen
echo ============================================
echo.

REM Alten publish-Ordner entfernen, damit keine alten Dateien liegen bleiben
if exist publish (
    echo Loesche alten publish-Ordner...
    rmdir /s /q publish
)

echo Erstelle Self-Contained Single-File Build...
dotnet publish WinVora.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o publish

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo FEHLER: Der Build ist fehlgeschlagen. Siehe Ausgabe oben.
    pause
    exit /b 1
)

echo.
echo ============================================
echo   Fertig! Der publish-Ordner ist aktuell.
echo   Jetzt WinVoraSetup.iss neu kompilieren (F9),
echo   um den neuen Installer zu erzeugen.
echo ============================================
pause
