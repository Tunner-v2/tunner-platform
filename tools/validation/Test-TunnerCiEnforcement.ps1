[CmdletBinding()]
param([string]$RepositoryRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1') -Force
$RepositoryRoot = (Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot)
$helper = Join-Path $RepositoryRoot 'tools/security/Assert-TunnerCiResult.ps1'
& $helper -Result 'success' | Out-Null
$checks = 1
foreach ($result in @('failure','cancelled','skipped','pending','in_progress','neutral','Success','success ' ,'',$null)) {
    $rejected = $false
    try { & $helper -Result $result | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Non-success CI outcome was accepted: '$result'." }
    $checks++
}
$workflow = [IO.File]::ReadAllText((Join-Path $RepositoryRoot '.github/workflows/security-supply-chain.yml'))
$gate = [regex]::Match($workflow,'(?ms)^  container-security-gate:\r?\n(?<body>.*?)(?=^  [a-z-]+:|\z)').Groups['body'].Value
foreach ($fragment in @('name: Container security gate','needs: [container-image-scan]','IMAGE_SCAN_RESULT: $'+'{{ needs.container-image-scan.result }}','Assert-TunnerCiResult.ps1 -Result $env:IMAGE_SCAN_RESULT','if: $'+'{{ always() }}')) {
    if (-not $gate.Contains($fragment)) { throw "Missing fail-closed gate binding: $fragment" }
    $checks++
}
foreach ($fragment in @('fail-fast: false','severity: HIGH,CRITICAL',"exit-code: '1'",'ignore-unfixed: true','Test-TunnerCiEnforcement.ps1')) {
    if (-not $workflow.Contains($fragment)) { throw "Existing scan policy/regression binding changed: $fragment" }
    $checks++
}
if ($workflow -match 'continue-on-error:\s*true') { throw 'Security checks cannot continue on error.' }
$policy = Get-Content (Join-Path $RepositoryRoot 'governance/config/required-main-checks.json') -Raw | ConvertFrom-Json
if ($policy.branch -cne 'main' -or -not $policy.strict_required_status_checks_policy -or $policy.required_status_checks.Count -ne 3) { throw 'Expected strict main checks.' }
foreach ($check in $policy.required_status_checks) {
    if ($check.integration_id -ne 15368 -or -not $workflow.Contains('name: ' + $check.context)) { throw ('Required check is not bound to the observed Actions app/workflow: ' + $check.context) }
    $checks++
}
$create = [IO.File]::ReadAllText((Join-Path $RepositoryRoot '.github/workflows/create-pull-request.yml'))
if (-not $create.Contains('--json isDraft') -or -not $create.Contains('Draft PR retained for review')) { throw 'Draft reviews must skip auto-merge enablement.' }
$checks++
Write-Output "PASS: $checks CI result and enforced-check contract checks"
