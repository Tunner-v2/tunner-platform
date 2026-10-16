[CmdletBinding()]
param([string]$RepositoryRoot, [switch]$NoRestore)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1') -Force
$RepositoryRoot = Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
$project = 'tests/Tunner.Installer.Tests/Tunner.Installer.Tests.csproj'
$receipts = Join-Path $RepositoryRoot 'artifacts/r2-installer'
Push-Location -LiteralPath $RepositoryRoot
try {
    if (-not $NoRestore) {
        & dotnet restore $project
        if ($LASTEXITCODE -ne 0) { throw 'Installer test restore failed.' }
    }
    & dotnet build $project --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
    # The suite always starts disposable OpenBao; it never contacts a deployed R2 bucket.
    & dotnet test $project --no-build --logger trx --results-directory $receipts
    if ($LASTEXITCODE -ne 0) { throw 'Installer tests failed. Inspect local TRX receipts.' }
    & (Join-Path $RepositoryRoot 'tools/security/Invoke-TunnerSecretScan.ps1') -RepositoryRoot $RepositoryRoot
    & (Join-Path $RepositoryRoot 'tools/security/Invoke-TunnerSastPolicyScan.ps1') -RepositoryRoot $RepositoryRoot
    Write-Output 'PASS: installer build, tests, secret scan and SAST; live R2 acceptance is separate.'
}
finally { Pop-Location }
