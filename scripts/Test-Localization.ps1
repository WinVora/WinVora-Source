param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot
)

$ErrorActionPreference = 'Stop'
$localizationFiles = Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'Infrastructure') -File -Filter 'Localization*.cs'
$source = ($localizationFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"

$definedKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$duplicateKeys = [System.Collections.Generic.List[string]]::new()
$emptyTranslations = [System.Collections.Generic.List[string]]::new()
$entryPattern = '\["(?<key>[^"]+)"\]\s*='
$simpleTranslationPattern = '\["(?<key>[^"]+)"\]\s*=\s*\(\s*"(?<de>(?:\\.|[^"])*)"\s*,\s*"(?<en>(?:\\.|[^"])*)"\s*\)'

foreach ($match in [regex]::Matches($source, $entryPattern)) {
    $key = $match.Groups['key'].Value
    if (-not $definedKeys.Add($key)) {
        $duplicateKeys.Add($key)
    }
}
foreach ($match in [regex]::Matches($source, $simpleTranslationPattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)) {
    $key = $match.Groups['key'].Value
    if ([string]::IsNullOrWhiteSpace($match.Groups['de'].Value) -or
        [string]::IsNullOrWhiteSpace($match.Groups['en'].Value)) {
        $emptyTranslations.Add($key)
    }
}

$missingKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$sourceDirectories = 'App', 'Features', 'Infrastructure', 'Services', 'UI', 'Tests'
$codeFiles = @(
    foreach ($directory in $sourceDirectories) {
        $path = Join-Path $ProjectRoot $directory
        if (Test-Path -LiteralPath $path) {
            Get-ChildItem -LiteralPath $path -Recurse -File -Filter '*.cs'
        }
    }
    Get-ChildItem -LiteralPath $ProjectRoot -File -Filter '*.cs'
)
foreach ($file in $codeFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($match in [regex]::Matches($content, 'Localization\.T\("(?<key>[^"]+)"\)')) {
        $key = $match.Groups['key'].Value
        if (-not $definedKeys.Contains($key)) {
            [void]$missingKeys.Add($key)
        }
    }
}

# Direkte deutsche Zuweisungen in C# umgehen die Sprachumschaltung. Bilinguale
# Erststarttexte sind erlaubt; neue Ausnahmen sollen bewusst hier dokumentiert werden.
$hardcodedGerman = [System.Collections.Generic.List[string]]::new()
$germanWords = 'Abbrechen|Aktion|Ausgewählte|Bereinigung|Deinstallieren|Einstellungen|Fehler|Keine|Löschen|Programme|Prüfung|Speicher|Verlauf|Wird geladen|Zeitüberschreitung'
foreach ($file in $codeFiles) {
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $file.FullName) {
        $lineNumber++
        if ($line -match '(Text|Content|Title|Header|PlaceholderText|CloseButtonText|PrimaryButtonText)\s*=\s*"[^"]*(' + $germanWords + ')[^"]*"' -and
            $line -notmatch 'Sprache wählen / Choose Language') {
            $rootPrefix = $ProjectRoot.TrimEnd('\') + '\'
            $relative = $file.FullName
            if ($relative.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                $relative = $relative.Substring($rootPrefix.Length)
            }
            $hardcodedGerman.Add("${relative}:$lineNumber")
        }
    }
}

# Unbenannte XAML-Texte können zur Laufzeit nicht von ApplyLanguage aktualisiert
# werden. Benannte Elemente werden zusätzlich durch die internen Sprachtests geprüft.
$hardcodedXaml = [System.Collections.Generic.List[string]]::new()
$xamlFiles = @(
    foreach ($directory in $sourceDirectories) {
        $path = Join-Path $ProjectRoot $directory
        if (Test-Path -LiteralPath $path) {
            Get-ChildItem -LiteralPath $path -Recurse -File -Filter '*.xaml'
        }
    }
)
foreach ($file in $xamlFiles) {
    $xamlLines = Get-Content -LiteralPath $file.FullName
    for ($index = 0; $index -lt $xamlLines.Count; $index++) {
        $line = $xamlLines[$index]
        $lineNumber = $index + 1
        $contextStart = [Math]::Max(0, $index - 4)
        $elementContext = ($xamlLines[$contextStart..$index] -join ' ')
        if ($line -match '(Text|Content|Header|PlaceholderText|ToolTipService\.ToolTip)="[^"]*(' + $germanWords + ')[^"]*"' -and
            $elementContext -notmatch 'x:Name=') {
            $rootPrefix = $ProjectRoot.TrimEnd('\') + '\'
            $relative = $file.FullName.Substring($rootPrefix.Length)
            $hardcodedXaml.Add("${relative}:$lineNumber")
        }
    }
}

# Interpolierte deutsche UI-Meldungen sind zulässig, wenn derselbe kleine
# Codeabschnitt ausdrücklich den aktuellen Sprachmodus berücksichtigt.
$unprotectedInterpolations = [System.Collections.Generic.List[string]]::new()
foreach ($file in $codeFiles) {
    $lines = Get-Content -LiteralPath $file.FullName
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '\$"[^"]*(' + $germanWords + ')[^"]*"') {
            $start = [Math]::Max(0, $index - 4)
            $end = [Math]::Min($lines.Count - 1, $index + 4)
            $context = ($lines[$start..$end] -join ' ')
            $uiContextStart = [Math]::Max(0, $index - 2)
            $uiContext = ($lines[$uiContextStart..$index] -join ' ')
            if ($uiContext -match 'ShowInfo|SetGlobalStatus|CreateConfirmation|PageSubtitle|\.Text\s*=|Content\s*=|Title\s*=' -and
                $context -notmatch 'Localization\.(CurrentLanguage|F|T)') {
                $rootPrefix = $ProjectRoot.TrimEnd('\') + '\'
                $relative = $file.FullName.Substring($rootPrefix.Length)
                $unprotectedInterpolations.Add("${relative}:$($index + 1)")
            }
        }
    }
}

$errors = [System.Collections.Generic.List[string]]::new()
if ($definedKeys.Count -eq 0) { $errors.Add('No localization entries were detected.') }
if ($duplicateKeys.Count -gt 0) { $errors.Add('Duplicate keys: ' + ($duplicateKeys -join ', ')) }
if ($emptyTranslations.Count -gt 0) { $errors.Add('Empty translations: ' + ($emptyTranslations -join ', ')) }
if ($missingKeys.Count -gt 0) { $errors.Add('Missing keys: ' + (($missingKeys | Sort-Object) -join ', ')) }
if ($hardcodedGerman.Count -gt 0) { $errors.Add('Hard-coded German UI text: ' + ($hardcodedGerman -join ', ')) }
if ($hardcodedXaml.Count -gt 0) { $errors.Add('Unnamed hard-coded German XAML text: ' + ($hardcodedXaml -join ', ')) }
if ($unprotectedInterpolations.Count -gt 0) { $errors.Add('Unprotected interpolated German UI text: ' + ($unprotectedInterpolations -join ', ')) }

if ($errors.Count -gt 0) {
    foreach ($errorMessage in $errors) { Write-Error $errorMessage }
    exit 1
}

Write-Host "Localization validation passed ($($definedKeys.Count) keys)."
