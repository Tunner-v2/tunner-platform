param([Parameter(Mandatory=$true)][string]$RepositoryRoot)
$ErrorActionPreference = 'Stop'
$parseErrors = $null
$tokens = $null
$source = Join-Path $RepositoryRoot 'tools/validation/Validate-TunnerProjectMemorySource.ps1'
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Project-memory validator must parse.' }
$function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-TunnerProjectRevisionSummary' }, $true)
if ($null -eq $function) { throw 'Missing executable revision-summary assertion.' }
$check = $function.Body.GetScriptBlock()
$closed = [pscustomobject]@{ Id='P0-RV001'; Status='CLOSED' }
$active = [pscustomobject]@{ Id='P0-RV003'; Status='ACTIVE' }
$blocked = [pscustomobject]@{ Id='P0-RV004'; Status='BLOCKED' }
function Expect-Rejection([string]$Text, [object[]]$Records) {
    $rejected = $false
    try { & $check -ProjectState $Text -Revisions $Records }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Contradictory or missing active revision was accepted.' }
}
& $check -ProjectState 'P0-RV003: ACTIVE' -Revisions @($closed, $active)
& $check -ProjectState "P0-RV001: CLOSED`nP0-RV003: ACTIVE" -Revisions @($closed, $active)
& $check -ProjectState '' -Revisions @($closed, [pscustomobject]@{Id='OLD-CANCELED';Status='CANCELED'}, [pscustomobject]@{Id='OLD-SUPERSEDED';Status='SUPERSEDED'})
Expect-Rejection "P0-RV001: ACTIVE`nP0-RV003: ACTIVE" @($closed, $active)
Expect-Rejection 'P0-RV001: CLOSED' @($closed, $active)
Expect-Rejection 'P0-RV001: CLOSED' @($closed, $blocked)
Expect-Rejection 'P0-RV003: CLOSED' @($active)
Write-Output 'PASS: 7 bounded revision-summary checks; omitted terminal history accepted, missing active/blocked and contradictory displayed statuses rejected.'
