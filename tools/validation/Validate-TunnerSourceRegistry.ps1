[CmdletBinding()]
param([string]$RepositoryRoot = (Get-Location).Path)
$ErrorActionPreference = 'Stop'
foreach ($path in @('src/Tunner.Governance/SourceRegistryApplication.cs','governance/rd/source-registry.json','docs/development/LOCAL_SOURCE_REGISTRY.md')) { if (-not (Test-Path (Join-Path $RepositoryRoot $path))) { throw "Missing P0-012 artifact: $path" } }
$source = Get-Content -Raw (Join-Path $RepositoryRoot 'src/Tunner.Governance/SourceRegistryApplication.cs')
$tests = Get-Content -Raw (Join-Path $RepositoryRoot 'tests/Tunner.Governance.FunctionalTests/Program.cs')
foreach ($token in @('SourceRegistryApplication','EvidenceSha256','maxAgeDays','source_authority','HashFile','TryReadReviewedDate')) { if ($source -notmatch [regex]::Escape($token)) { throw "Missing P0-012 source control: $token" } }
foreach ($token in @('Current registered source evidence','freshness window','hash integrity')) { if ($tests -notmatch [regex]::Escape($token)) { throw "Missing P0-012 regression coverage: $token" } }
Write-Host 'TUN-P0-012 static source-registry validation passed.'