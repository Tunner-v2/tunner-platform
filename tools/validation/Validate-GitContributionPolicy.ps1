[CmdletBinding()]
param([string]$RepositoryRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
}

$policy = Get-Content -LiteralPath (Join-Path $RepositoryRoot "governance/policies/git-contribution-policy.yaml") -Raw
$contributing = Get-Content -LiteralPath (Join-Path $RepositoryRoot "CONTRIBUTING.md") -Raw
$template = Get-Content -LiteralPath (Join-Path $RepositoryRoot ".github/pull_request_template.md") -Raw

foreach ($required in @("required_for_main: true", "required_codeowner_review: true", "approval_pending: continues_governance_eligible_local_and_branch_execution", "integration_gate: protected_main_merge_requires_human_approval", "dependency_readiness: [LOCAL_VALIDATED, MERGED_TO_MAIN]")) {
    if (-not $policy.Contains($required)) { throw "Missing policy requirement: $required" }
}
if ($contributing -notmatch "awaiting approval, continue all governance-eligible local or branch work") { throw "Contribution guidance must preserve non-blocking approval execution." }
if ($template -notmatch "Dependency readiness required") { throw "PR template must collect dependency readiness." }

Write-Host "TUN-P0-002 static contribution policy validation passed."