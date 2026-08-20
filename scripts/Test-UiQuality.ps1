param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot
)

$ErrorActionPreference = 'Stop'
$xamlPath = Join-Path $ProjectRoot 'App\MainWindow.xaml'
$manifestPath = Join-Path $ProjectRoot 'app.manifest'
$performancePath = Join-Path $ProjectRoot 'Features\Performance\MainWindow.Performance.cs'

$xaml = Get-Content -LiteralPath $xamlPath -Raw
$manifest = Get-Content -LiteralPath $manifestPath -Raw
$performance = Get-Content -LiteralPath $performancePath -Raw
$failures = [System.Collections.Generic.List[string]]::new()

foreach ($resource in @(
    'AppAccentBrush', 'AppAccentBrushLight', 'AppSuccessBrush',
    'AppWarningBrush', 'AppErrorBrush', 'AppCardSurfaceBrush',
    'AppMutedForegroundBrush', 'AppFaintForegroundBrush')) {
    if ($xaml -notmatch ('x:Key="' + [regex]::Escape($resource) + '"')) {
        $failures.Add("Missing required theme resource: $resource")
    }
}

if ($xaml -notmatch 'UseSystemFocusVisuals="True"') {
    $failures.Add('System focus visuals are not enabled on RootGrid.')
}
if ($manifest -notmatch '>PerMonitorV2<') {
    $failures.Add('Per-monitor V2 DPI awareness is missing from app.manifest.')
}
if ($performance -match 'AppDangerBrush') {
    $failures.Add('PC Check references the undefined AppDangerBrush resource.')
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'UI quality validation passed (theme resources, focus visuals, DPI manifest).'
