[CmdletBinding()]
param([string]$ProfilePath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1') -Force
$tunnerRoot = Resolve-TunnerRepositoryRoot -StartDirectory $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ProfilePath)) { $ProfilePath = 'local-config/r2/profile.json' }
$tunnerProfile = if ([IO.Path]::IsPathRooted($ProfilePath)) { [IO.Path]::GetFullPath($ProfilePath) }
    else { [IO.Path]::GetFullPath((Join-Path $tunnerRoot $ProfilePath)) }
if (-not (Test-Path -LiteralPath $tunnerProfile -PathType Leaf)) {
    $tunnerTemplate = Join-Path $tunnerRoot 'local-config/r2/profile.example.json'
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($tunnerProfile))
    # Runtime configuration only; copy atomically refuses an existing destination.
    [IO.File]::Copy($tunnerTemplate, $tunnerProfile, $false)
    Write-Host 'Saved non-secret local configuration from the operator-supplied example.'
}
Push-Location -LiteralPath $tunnerRoot
try {
    & dotnet run --project (Join-Path $tunnerRoot 'src/Tunner.Installer') -- local-r2 $tunnerProfile
    if ($LASTEXITCODE -ne 0) { throw 'Local R2 setup/validation failed. Review the non-secret status and retry; existing credentials are preserved.' }
}
finally { Pop-Location }
