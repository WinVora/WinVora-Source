param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,
    [string]$InstallerPath,
    [switch]$RequireSignature
)

$ErrorActionPreference = 'Stop'
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$requiredFiles = @(
    'WinVora.exe',
    'WinVora.dll',
    'WinVora.deps.json',
    'WinVora.runtimeconfig.json',
    'coreclr.dll',
    'hostfxr.dll',
    'WinVora.pri',
    'app.ico'
)

$missing = $requiredFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $publish $_)) }
if ($missing.Count -gt 0) {
    throw "Self-contained publish is incomplete. Missing: $($missing -join ', ')"
}

$unexpectedSymbols = Get-ChildItem -LiteralPath $publish -Recurse -File -Filter '*.pdb'
if ($unexpectedSymbols.Count -gt 0) {
    throw "End-user publish contains PDB files: $($unexpectedSymbols.FullName -join ', ')"
}

$exe = Join-Path $publish 'WinVora.exe'
$version = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'WinVora.exe does not contain a product version.'
}

$runtimeConfig = Get-Content -LiteralPath (Join-Path $publish 'WinVora.runtimeconfig.json') -Raw
if ($runtimeConfig -notmatch 'Microsoft\.NETCore\.App') {
    throw 'Runtime configuration does not identify the .NET runtime.'
}

if ($InstallerPath) {
    $installer = (Resolve-Path -LiteralPath $InstallerPath).Path
    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($RequireSignature -and $signature.Status -ne 'Valid') {
        throw "Installer signature is not valid: $($signature.Status)"
    }
    Write-Host "Installer signature: $($signature.Status)"
}

$totalBytes = (Get-ChildItem -LiteralPath $publish -Recurse -File |
    Measure-Object -Property Length -Sum).Sum
Write-Host ("Publish readiness passed: WinVora {0}, {1} files, {2:N1} MB." -f `
    $version,
    (Get-ChildItem -LiteralPath $publish -Recurse -File).Count,
    ($totalBytes / 1MB))
