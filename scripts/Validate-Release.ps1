param(
    [Parameter(Mandatory = $false)]
    [string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '..\WinVora.csproj'
$readmePath = Join-Path $PSScriptRoot '..\README.md'
$checklistPath = Join-Path $PSScriptRoot '..\Docs\RELEASE_CHECKLIST.md'
$installerPath = Join-Path $PSScriptRoot '..\Packaging\WinVoraSetup.iss'

[xml]$project = Get-Content -LiteralPath $projectPath
$projectVersion = $project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $ExpectedVersion) { $ExpectedVersion = $projectVersion }
if ($projectVersion -ne $ExpectedVersion) {
    throw "Projektversion $projectVersion stimmt nicht mit $ExpectedVersion überein."
}

$readme = Get-Content -LiteralPath $readmePath -Raw
$checklist = Get-Content -LiteralPath $checklistPath -Raw
$installer = Get-Content -LiteralPath $installerPath -Raw

if ($readme -notmatch [regex]::Escape("Version $ExpectedVersion") -or
    $readme -notmatch [regex]::Escape("WinVora-Setup-$ExpectedVersion.exe")) {
    throw "README enthält nicht überall Version und Installername $ExpectedVersion."
}
if ($checklist -notmatch [regex]::Escape("WinVora $ExpectedVersion") -or
    $checklist -notmatch [regex]::Escape("WinVora-Setup-$ExpectedVersion.exe")) {
    throw "Release-Checkliste enthält nicht überall Version und Installername $ExpectedVersion."
}
if ($installer -notmatch 'GetStringFileInfo\("\.\.\\publish\\WinVora\.exe", "ProductVersion"\)' -or
    $installer -notmatch 'OutputBaseFilename=WinVora-Setup-\{#MyAppVersion\}') {
    throw 'Installer übernimmt Version oder Dateinamen nicht aus der zentralen Projektversion.'
}

Write-Host "Release-Metadaten für WinVora $ExpectedVersion sind konsistent."
