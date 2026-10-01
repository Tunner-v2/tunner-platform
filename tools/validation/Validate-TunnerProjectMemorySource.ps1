param([Parameter(Mandatory=$true)][string]$RepositoryRoot)
$ErrorActionPreference='Stop'
$required=@('docs/project/PROJECT_CONTEXT.md','docs/project/CURRENT_STATE.md','docs/project/DECISION_INDEX.md','docs/project/RISK_REGISTER.md','docs/project/HANDOFF.md')
foreach($relative in $required){$path=Join-Path $RepositoryRoot $relative;if(-not(Test-Path -LiteralPath $path)){throw "Missing project-memory artifact: $relative"};if([string]::IsNullOrWhiteSpace([IO.File]::ReadAllText($path))){throw "Empty project-memory artifact: $relative"}}
$state=[IO.File]::ReadAllText((Join-Path $RepositoryRoot 'docs/project/CURRENT_STATE.md'))
$handoff=[IO.File]::ReadAllText((Join-Path $RepositoryRoot 'docs/project/HANDOFF.md'))
foreach($heading in @('Baseline and milestone','Locally validated foundations','Exact next authorized action')){if($state -notmatch [regex]::Escape($heading)){throw "CURRENT_STATE.md is missing section: $heading"}}
foreach($heading in @('Work item','Verified local evidence','Exact next action')){if($handoff -notmatch [regex]::Escape($heading)){throw "HANDOFF.md is missing section: $heading"}}
foreach($directory in @('governance/milestones','governance/work-items','governance/evidence','governance/context')){if(-not(Test-Path -LiteralPath (Join-Path $RepositoryRoot $directory))){throw "Missing governed state directory: $directory"}}
Write-Output 'TUN-P0-030 project-memory validation passed.'