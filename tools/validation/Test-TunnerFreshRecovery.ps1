param([Parameter(Mandatory=$true)][string]$RepositoryRoot)
$ErrorActionPreference='Stop'
function Invoke-Tunner([string[]]$Arguments) {
  $output=& dotnet run --project (Join-Path $RepositoryRoot 'src/Tunner.Governance') --no-build -- @Arguments
  if($LASTEXITCODE -ne 0){throw "tunner command failed: $($Arguments -join ' ')"}
  return ($output | Out-String | ConvertFrom-Json)
}
$authority=Invoke-Tunner @('--repository',$RepositoryRoot,'authority','verify')
$next=Invoke-Tunner @('--repository',$RepositoryRoot,'governance','next')
$roles=Invoke-Tunner @('--repository',$RepositoryRoot,'governance','roles','calculate','TUN-P0-032')
$context=Invoke-Tunner @('--repository',$RepositoryRoot,'context','build','--work-item','TUN-P0-032','--mode','TASK','--output',(Join-Path $RepositoryRoot 'artifacts/recovery-context'))
$escalation=Invoke-Tunner @('--repository',$RepositoryRoot,'context','escalate','TUN-P0-032','--from','TASK','--reason','cross-domain audit required')
if($authority.Payload.Outcome -ne 'VERIFIED' -or -not $next.Payload.Items -or $roles.Payload.RequiredRoles.Count -lt 1 -or $context.Payload.Outcome -ne 'GENERATED' -or $escalation.Payload.Outcome -ne 'INSUFFICIENT_CONTEXT'){throw 'Fresh recovery assertions failed.'}
$result=[ordered]@{schema_version=1;scope_id='TUN-P0-032';status='PASS';authority=$authority.Payload.Outcome;current_work='TUN-P0-032';roles=$roles.Payload.RequiredRoles;next_items=$next.Payload.Items.Count;context_mode='TASK';escalation_next_mode=$escalation.Payload.NextMode}
$result|ConvertTo-Json -Depth 5