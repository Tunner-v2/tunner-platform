[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AsOf,
    [string]$RepositoryRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
& (Join-Path $RepositoryRoot 'tools/security/Invoke-TunnerSastPolicyScan.ps1') -RepositoryRoot $RepositoryRoot
& (Join-Path $RepositoryRoot 'tools/security/Invoke-TunnerSecretScan.ps1') -RepositoryRoot $RepositoryRoot
& (Join-Path $RepositoryRoot 'tools/security/Invoke-TunnerContainerPolicyScan.ps1') -RepositoryRoot $RepositoryRoot
& (Join-Path $RepositoryRoot 'tools/security/New-TunnerSupplyChainEvidence.ps1') -RepositoryRoot $RepositoryRoot -AsOf $AsOf
Write-Host 'Tunner P0 security and supply-chain baseline passed.'