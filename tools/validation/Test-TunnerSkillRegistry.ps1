[CmdletBinding()]
param([string]$RepositoryRoot = (Get-Location).Path)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$validator = Join-Path $repositoryRoot 'tools/validation/Validate-TunnerSkillRegistry.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('tunner-skill-registry-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $fixture -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot '.agent') -Destination (Join-Path $fixture '.agent') -Recurse
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'governance/skills') -Destination (Join-Path $fixture 'governance/skills') -Recurse -Force
    & $validator -RepositoryRoot $fixture | Out-Null
    $path = Join-Path $fixture '.agent/skills/auditor/SKILL.md'
    [IO.File]::WriteAllText($path, ([IO.File]::ReadAllText($path).Replace('skill_id: auditor', 'skill_id: altered-auditor')))
    try { & $validator -RepositoryRoot $fixture | Out-Null; throw 'Negative fixture unexpectedly passed.' } catch { if ($_.Exception.Message -notmatch 'SKILL_METADATA') { throw } }
    Write-Host 'TUN-P0-026 isolated skill-registry checks passed.'
} finally { if(Test-Path -LiteralPath $fixture){ Remove-Item -LiteralPath $fixture -Recurse -Force } }