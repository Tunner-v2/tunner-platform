[CmdletBinding()]
param([string]$RepositoryRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$module = Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1'
Import-Module $module -Force
$root = Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
$expected = (Resolve-Path -LiteralPath $root).Path
$nested = Join-Path $expected 'src/Tunner.Governance'
if (-not (Test-Path -LiteralPath $nested -PathType Container)) { throw "Missing nested execution directory: $nested" }

$fromNested = Resolve-TunnerRepositoryRoot -StartDirectory $nested
if ($fromNested -ne $expected) { throw 'Nested-directory resolution did not return the repository root.' }

$fromExplicit = Resolve-TunnerRepositoryRoot -RepositoryRoot $expected -StartDirectory $nested
if ($fromExplicit -ne $expected) { throw 'Explicit repository-root resolution did not return the repository root.' }

$previousEnvironmentRoot = [Environment]::GetEnvironmentVariable('TUNNER_REPO_ROOT', 'Process')
try {
    [Environment]::SetEnvironmentVariable('TUNNER_REPO_ROOT', $expected, 'Process')
    $fromEnvironment = Resolve-TunnerRepositoryRoot -StartDirectory $nested
    if ($fromEnvironment -ne $expected) { throw 'Environment repository-root resolution did not return the repository root.' }
}
finally {
    [Environment]::SetEnvironmentVariable('TUNNER_REPO_ROOT', $previousEnvironmentRoot, 'Process')
}

Write-Host 'TUN-P0-035 repository-root resolver checks passed.'