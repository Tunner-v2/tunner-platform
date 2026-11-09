#requires -Version 5.1
[CmdletBinding()]
param([string[]]$Images = @(), [string]$RepositoryRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1') -Force
$RepositoryRoot = Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
$catalog = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'infra/security-images/catalog.json') -Raw | ConvertFrom-Json
$required = @($catalog.images | Where-Object { $Images -ccontains $_.image })
if ($required.Count -eq 0) { return }
$pwsh = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $pwsh) { throw 'PowerShell7 (pwsh) is required to prepare the reviewed local security-image recipes.' }
foreach ($entry in $required) {
    & $pwsh.Source -NoLogo -NoProfile -File (Join-Path $RepositoryRoot 'tools/security/Build-TunnerSecurityImages.ps1') -RepositoryRoot $RepositoryRoot -Component $entry.component -Ensure
    if ($LASTEXITCODE -ne 0) { throw ('Cannot prepare the reviewed '+$entry.component+' development image.') }
}
