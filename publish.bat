@echo off
setlocal

echo ============================================
echo   WinVora - Test-Build erstellen
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
echo Erstelle ZIP-Datei...

REM Alte ZIP entfernen, falls vorhanden
if exist WinVora-Test.zip del WinVora-Test.zip

powershell -Command "Compress-Archive -Path 'publish\*' -DestinationPath 'WinVora-Test.zip' -Force"

echo.
echo ============================================
echo   Fertig! WinVora-Test.zip liegt bereit.
echo ============================================
pause
