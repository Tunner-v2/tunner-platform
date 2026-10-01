[CmdletBinding()]
param([string]$RepositoryRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$registryPath = Join-Path $repositoryRoot 'governance/skills/skill-registry.json'
if (-not (Test-Path -LiteralPath $registryPath)) { throw 'SKILL_REGISTRY_MISSING: governance/skills/skill-registry.json' }
try { $registry = Get-Content -LiteralPath $registryPath -Raw | ConvertFrom-Json } catch { throw "SKILL_REGISTRY_INVALID: $($_.Exception.Message)" }
if ($registry.schema_version -ne 1 -or $registry.registry_id -ne 'tunner-mandatory-role-skills') { throw 'SKILL_REGISTRY_INVALID: expected schema_version 1 and canonical registry_id.' }
$expectedRoles = @('full-stack-engineer','solution-architect','project-manager','ui-ux-engineer','frontend-engineer','tester-qa-engineer','financial-specialist','compliance-specialist','auditor','founder-product-owner','admin-operator','support-engineer','end-user-ux-reviewer','content-writer','business-analyst','sdk-engineer','governance-engineer','rd-engineer','cyber-security-engineer','devops-engineer')
$findings = New-Object System.Collections.Generic.List[string]
$roles = @($registry.required_roles)
if ($roles.Count -ne $expectedRoles.Count) { $findings.Add('SKILL_ROLE_COUNT: registry must contain exactly twenty mandatory roles.') }
foreach($role in $expectedRoles) {
    $entry = @($roles | Where-Object { $_.role -eq $role })
    if ($entry.Count -ne 1) { $findings.Add("SKILL_ROLE_ENTRY: $role must occur exactly once."); continue }
    $entry = $entry[0]
    $expectedPath = ".agent/skills/$role/SKILL.md"
    if ($entry.manifest_path -ne $expectedPath) { $findings.Add("SKILL_PATH: $role must declare $expectedPath"); continue }
    $path = Join-Path $repositoryRoot $entry.manifest_path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $findings.Add("SKILL_MANIFEST_MISSING: $role"); continue }
    $content = Get-Content -LiteralPath $path -Raw
    foreach($field in @("skill_id: $role", 'name:', 'version:', ("status: " + $entry.required_status), 'authority:')) {
        if ($content -notmatch ('(?m)^' + [regex]::Escape($field))) { $findings.Add("SKILL_METADATA: $role missing $field") }
    }
    if ($content -notmatch '(?m)^version:\s*\d+\.\d+\.\d+\s*$') { $findings.Add("SKILL_VERSION: $role must use semantic version.") }
    foreach($heading in @('Mission','Activation triggers','Required authority/context','Mandatory checks','Required outputs/evidence','Blocking conditions','Prohibited actions','Standard handoff','Context rule')) {
        if ($content -notmatch ('(?m)^## ' + [regex]::Escape($heading) + '\s*$')) { $findings.Add("SKILL_CONTRACT: $role missing $heading") }
    }
}
$entryContract = Join-Path $repositoryRoot '.agent/AGENTS.md'
if (-not (Test-Path -LiteralPath $entryContract)) { $findings.Add('SKILL_ENTRY_CONTRACT_MISSING: .agent/AGENTS.md') }
else {
    $entry = Get-Content -LiteralPath $entryContract -Raw
    foreach($required in @('../AGENTS.md','skills/<role>/SKILL.md','activate only the roles determined by governance')) {
        if ($entry -notmatch [regex]::Escape($required)) { $findings.Add("SKILL_ENTRY_CONTRACT: missing $required") }
    }
}
if ($findings.Count -gt 0) { throw ("Skill registry validation failed:" + [Environment]::NewLine + ($findings -join [Environment]::NewLine)) }
[pscustomobject]@{ result='PASS'; registry_id=$registry.registry_id; role_count=$roles.Count; context_requirement='Activated skills must be represented with role, semantic version, and SHA-256 by ContextApplication.' } | ConvertTo-Json