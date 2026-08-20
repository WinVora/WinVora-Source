param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:WINVORA_SIGNING_PFX_BASE64) -or
    [string]::IsNullOrWhiteSpace($env:WINVORA_SIGNING_PFX_PASSWORD)) {
    Write-Host 'Signing secrets are not configured; artifact remains unsigned.'
    return
}

$artifact = (Resolve-Path -LiteralPath $Path).Path
$pfxPath = Join-Path $env:RUNNER_TEMP ("winvora-signing-{0}.pfx" -f [Guid]::NewGuid())
$certificate = $null
try {
    [IO.File]::WriteAllBytes($pfxPath, [Convert]::FromBase64String($env:WINVORA_SIGNING_PFX_BASE64))
    $password = ConvertTo-SecureString $env:WINVORA_SIGNING_PFX_PASSWORD -AsPlainText -Force
    $certificate = Import-PfxCertificate -FilePath $pfxPath `
        -CertStoreLocation 'Cert:\CurrentUser\My' -Password $password -Exportable:$false
    if (-not $certificate) { throw 'The signing certificate could not be imported.' }

    $signTool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" `
        -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signTool) { throw 'signtool.exe was not found.' }

    & $signTool.FullName sign /sha1 $certificate.Thumbprint /fd SHA256 `
        /tr 'http://timestamp.digicert.com' /td SHA256 $artifact
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE." }

    $signature = Get-AuthenticodeSignature -LiteralPath $artifact
    if ($signature.Status -ne 'Valid') {
        throw "Signature verification failed: $($signature.Status)"
    }
    Write-Host "Signed and verified: $artifact"
}
finally {
    if ($certificate) {
        Remove-Item -LiteralPath ("Cert:\CurrentUser\My\{0}" -f $certificate.Thumbprint) -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $pfxPath -Force -ErrorAction SilentlyContinue
}
