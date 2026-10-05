param([Parameter(Mandatory=$true)][string]$RepositoryRoot)
$ErrorActionPreference='Stop'
$required=@('docs/project/PROJECT_CONTEXT.md','docs/project/CURRENT_STATE.md','docs/project/DECISION_INDEX.md','docs/project/RISK_REGISTER.md','docs/project/HANDOFF.md')
foreach($relative in $required){$path=Join-Path $RepositoryRoot $relative;if(!(Test-Path -LiteralPath $path) -or [string]::IsNullOrWhiteSpace([IO.File]::ReadAllText($path))){throw "Missing or empty project-memory artifact: $relative"}}
$state=[IO.File]::ReadAllText((Join-Path $RepositoryRoot 'docs/project/CURRENT_STATE.md'))
foreach($relative in @('docs/project/HANDOFF.md','docs/project/PROJECT_CONTEXT.md')){if([IO.File]::ReadAllText((Join-Path $RepositoryRoot $relative)) -ne $state){throw "Derived project state disagrees: $relative"}}
foreach($text in @('Derived deterministically from repository-native governance','Latest bounded Revision lineage','Validated SHA:','Tested SHA:','Closure SHA:','Exact next governed action:','No chat history is required')){if(!$state.Contains($text)){throw "Missing recovery fact: $text"}}
$status=& dotnet run --no-build --project (Join-Path $RepositoryRoot 'src/Tunner.Governance') -- --repo-root $RepositoryRoot governance status | Out-String | ConvertFrom-Json
if($LASTEXITCODE -ne 0){throw 'Authoritative governance status is invalid'}
foreach($scope in $status.Payload.HistoricalScopes|Where-Object {$_.Id -in @('P0','P1')}){if(!$state.Contains($scope.Id+': historical '+$scope.HistoricalStatus)){throw "Historical milestone state contradicts governance: $($scope.Id)"}}
foreach($revision in $status.Payload.Revisions){if(!$state.Contains($revision.Id+': '+$revision.Status)){throw "Revision state contradicts governance: $($revision.Id)"}}
Write-Output 'PASS: CURRENT_STATE/HANDOFF/PROJECT_CONTEXT agree with authoritative governance.'
