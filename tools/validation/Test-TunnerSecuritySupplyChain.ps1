[CmdletBinding()]
param([string]$RepositoryRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$temporarySource = Join-Path $RepositoryRoot 'tools/security/.tunner-p0-013-negative.cs'
$temporaryCompose = Join-Path $RepositoryRoot 'artifacts/supply-chain/.tunner-p0-013-invalid-compose.yaml'
$expectedFailures = [System.Collections.Generic.List[string]]::new()

try {
    [IO.Directory]::CreateDirectory((Split-Path -Parent $temporaryCompose)) | Out-Null
    [IO.File]::WriteAllText($temporarySource, ('public sealed class Probe { private readonly object _value = new ' + 'Binary' + 'Formatter(); }' + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    try {
        & (Join-Path $RepositoryRoot 'tools/security/Invoke-TunnerSastPolicyScan.ps1') -RepositoryRoot $RepositoryRoot
        throw 'SAST policy scan accepted an unsafe binary serialization fixture.'
    }
    catch {
        if ($_.Exception.Message -notmatch 'unsafe-binary-serialization') { throw }
        $expectedFailures.Add('unsafe source policy')
    }
    Remove-Item -LiteralPath $temporarySource -Force

    [IO.File]::WriteAllText($temporaryCompose, "services:`n  bad:`n    image: example:latest`n", [Text.UTF8Encoding]::new($false))
    try {
        & (Join-Path $RepositoryRoot 'tools/security/Invoke-TunnerContainerPolicyScan.ps1') -RepositoryRoot $RepositoryRoot -ComposePath 'artifacts/supply-chain/.tunner-p0-013-invalid-compose.yaml'
        throw 'Container policy scan accepted a mutable latest image fixture.'
    }
    catch {
        if ($_.Exception.Message -notmatch 'Mutable latest image tag') { throw }
        $expectedFailures.Add('mutable container tag')
    }
    Remove-Item -LiteralPath $temporaryCompose -Force

    try {
        & (Join-Path $RepositoryRoot 'tools/security/New-TunnerSupplyChainEvidence.ps1') -RepositoryRoot $RepositoryRoot -AsOf '30-09-2026'
        throw 'Supply-chain generator accepted a non-ISO evidence date.'
    }
    catch {
        if ($_.Exception.Message -notmatch 'ISO date') { throw }
        $expectedFailures.Add('non-ISO evidence date')
    }

    try {
        & (Join-Path $RepositoryRoot 'tools/security/New-TunnerSupplyChainEvidence.ps1') -RepositoryRoot $RepositoryRoot -AsOf '2026-09-30' -OutputDirectory '../escape'
        throw 'Supply-chain generator accepted an escaping output directory.'
    }
    catch {
        if ($_.Exception.Message -notmatch 'OutputDirectory must remain below') { throw }
        $expectedFailures.Add('escaping output directory')
    }
}
finally {
    Remove-Item -LiteralPath $temporarySource -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryCompose -Force -ErrorAction SilentlyContinue
}

if ($expectedFailures.Count -ne 4) { throw "Expected four rejection checks; observed $($expectedFailures.Count)." }
Write-Host "TUN-P0-013 negative security baseline validation passed: $($expectedFailures -join ', ')."