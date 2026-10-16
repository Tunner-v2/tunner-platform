param([Parameter(Mandatory=$true)][string]$RepositoryRoot)
$ErrorActionPreference='Stop'
function Invoke-Tunner([string[]]$Arguments) {
  $output=& dotnet run --project (Join-Path $RepositoryRoot 'src/Tunner.Governance') --no-build -- @Arguments
  if($LASTEXITCODE -ne 0){throw "tunner command failed: $($Arguments -join ' ')"}
  return ($output | Out-String | ConvertFrom-Json)
}
$authority=Invoke-Tunner @('--repository',$RepositoryRoot,'authority','verify')
$status=Invoke-Tunner @('--repository',$RepositoryRoot,'governance','status')
$next=Invoke-Tunner @('--repository',$RepositoryRoot,'governance','next')
$pack=Join-Path $RepositoryRoot 'artifacts/recovery-context'
$context=Invoke-Tunner @('--repository',$RepositoryRoot,'context','build','--output',$pack)
$verified=Invoke-Tunner @('--repository',$RepositoryRoot,'context','verify','--output',$pack)
$manifest=Get-Content -LiteralPath (Join-Path $pack 'manifest.json') -Raw | ConvertFrom-Json
$scope=$manifest.WorkItem.Id
$escalation=Invoke-Tunner @('--repository',$RepositoryRoot,'context','escalate',$scope,'--from','CORE','--reason','additional declared implementation context required')
$p0Historical=@($status.Payload.HistoricalScopes | Where-Object {$_.Id -eq 'P0' -and $_.HistoricalStatus -eq 'CLOSED'}).Count -eq 1
$hasState=Test-Path -LiteralPath (Join-Path $pack 'CURRENT_STATE.md')
$hasHandoff=Test-Path -LiteralPath (Join-Path $pack 'HANDOFF.md')
if($authority.Payload.Outcome -ne 'VERIFIED' -or $context.Payload.Outcome -ne 'GENERATED' -or $verified.Payload.Outcome -ne 'CURRENT' -or -not $p0Historical -or -not $hasState -or -not $hasHandoff -or $escalation.Payload.NextMode -ne 'TASK'){throw 'Fresh recovery assertions failed.'}
$actionable=@($next.Payload.Items | Where-Object {$_.Actionable})
$result=[ordered]@{schema_version=1;scope_id=$scope;status='PASS';authority=$authority.Payload.Outcome;historical_scopes=$status.Payload.HistoricalScopes;active_revisions=@($status.Payload.Revisions | Where-Object {$_.Status -notin @('CLOSED','CANCELED','SUPERSEDED')});selected_revision=$manifest.Revision;latest_revision_lineage=$manifest.LatestRevisions;roles=@($manifest.Included | Where-Object {$_.Reason -eq 'activated role skill'} | ForEach-Object {$_.Path});eligible_work=@($actionable | ForEach-Object {$_.WorkItemId});exact_next_action=if($actionable.Count -gt 0){$actionable[0].WorkItemId+': '+$actionable[0].Reason}else{'Inspect the scoped readiness findings from tunner governance next.'};context_verify=$verified.Payload.Outcome;escalation_next_mode=$escalation.Payload.NextMode}
$result|ConvertTo-Json -Depth 8
