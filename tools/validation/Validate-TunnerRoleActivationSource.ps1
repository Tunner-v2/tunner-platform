param([Parameter(Mandatory = $true)][string]$RepositoryRoot)
$ErrorActionPreference = 'Stop'

foreach ($relative in @('src/Tunner.Governance/RoleActivationApplication.cs','src/Tunner.Governance/GovernanceApplication.cs','src/Tunner.Governance/Program.cs','governance/policies/role-activation-policy.yaml','governance/schemas/v1/governance-record.schema.json','tests/Tunner.Governance.FunctionalTests/Program.cs','docs/development/LOCAL_ROLE_ACTIVATION.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $relative) -PathType Leaf)) { throw "P0-027 required artifact missing: $relative" }
}
$engine = Get-Content -Raw (Join-Path $RepositoryRoot 'src/Tunner.Governance/RoleActivationApplication.cs')
foreach ($token in @('RoleActivationApplication','every_implementation','governance_context_orchestration_evidence_rule_change','mandatory role is not activated','mandatory role review is missing','context_expansion_requested','RoleCalculationPayload')) {
    if ($engine -notmatch [regex]::Escape($token)) { throw "P0-027 role engine token missing: $token" }
}
$governance = Get-Content -Raw (Join-Path $RepositoryRoot 'src/Tunner.Governance/GovernanceApplication.cs')
foreach ($token in @('specialist-review','RoleActivationApplication.CheckReviews')) {
    if ($governance -notmatch [regex]::Escape($token)) { throw "P0-027 governance integration token missing: $token" }
}
$program = Get-Content -Raw (Join-Path $RepositoryRoot 'src/Tunner.Governance/Program.cs')
if ($program -notmatch [regex]::Escape('CreateRolesCommand')) { throw 'P0-027 CLI command is missing.' }
$policy = Get-Content -Raw (Join-Path $RepositoryRoot 'governance/policies/role-activation-policy.yaml')
if ($policy -notmatch '(?m)^enforcement:' -or $policy -notmatch '(?m)^  required_from:') { throw 'P0-027 prospective enforcement boundary is missing.' }
$tests = Get-Content -Raw (Join-Path $RepositoryRoot 'tests/Tunner.Governance.FunctionalTests/Program.cs')
foreach ($token in @('mandatory role is not activated: auditor','complete passing structured reviews','A blocking specialist review')) {
    if ($tests -notmatch [regex]::Escape($token)) { throw "P0-027 regression coverage missing: $token" }
}
Write-Host 'TUN-P0-027 static role-activation validation passed.'