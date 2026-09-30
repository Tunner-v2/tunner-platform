[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$schemaRoot = Join-Path $repositoryRoot 'governance\schemas\v1'
$expected = [ordered]@{
  'milestone.schema.json' = 'milestone'; 'sprint.schema.json' = 'sprint'; 'work-item.schema.json' = 'workItem'; 'dependency.schema.json' = 'dependency'; 'todo.schema.json' = 'todo'; 'gate.schema.json' = 'gate'; 'defect.schema.json' = 'defect'; 'reopen.schema.json' = 'reopen'; 'product-decision.schema.json' = 'productDecision'; 'amendment.schema.json' = 'amendment'; 'adr-reference.schema.json' = 'adrReference'; 'release.schema.json' = 'release'; 'evidence-manifest.schema.json' = 'evidenceManifest'; 'specialist-review.schema.json' = 'specialistReview'; 'context-selection.schema.json' = 'contextSelection'; 'handoff.schema.json' = 'handoff'
}

function Read-JsonDocument([string] $path) {
  try { return Get-Content -Raw -LiteralPath $path | ConvertFrom-Json }
  catch { throw "Invalid JSON schema document '$path': $($_.Exception.Message)" }
}

$corePath = Join-Path $schemaRoot 'governance-record.schema.json'
if (-not (Test-Path -LiteralPath $corePath)) { throw "Missing shared schema library: $corePath" }
$core = Read-JsonDocument $corePath
if ($core.'$schema' -ne 'https://json-schema.org/draft/2020-12/schema') { throw 'Shared library must declare JSON Schema Draft 2020-12.' }
if ($null -eq $core.'$defs') { throw 'Shared library is missing $defs.' }

$validated = New-Object System.Collections.Generic.List[string]
foreach ($entry in $expected.GetEnumerator()) {
  $path = Join-Path $schemaRoot $entry.Key
  if (-not (Test-Path -LiteralPath $path)) { throw "Missing required entry schema: $($entry.Key)" }
  $document = Read-JsonDocument $path
  if ($document.'$schema' -ne 'https://json-schema.org/draft/2020-12/schema') { throw "$($entry.Key) must declare JSON Schema Draft 2020-12." }
  $expectedRef = "urn:tunner:governance:schema:v1:record-library#/" + '$defs/' + $entry.Value
  if ($document.'$ref' -ne $expectedRef) { throw "$($entry.Key) must reference $expectedRef." }
  $definition = $core.'$defs'.($entry.Value)
  if ($null -eq $definition) { throw "Shared definition '$($entry.Value)' is missing." }
  if ($definition.required -notcontains 'schema_version') { throw "Shared definition '$($entry.Value)' must require schema_version." }
  $idProperty = @($definition.properties.psobject.Properties | ForEach-Object { $_.Name } | Where-Object { $_ -match '(^id$|_id$)' })
  if ($idProperty.Count -lt 1) { throw "Shared definition '$($entry.Value)' must declare an immutable record identifier field." }
  if ($definition.additionalProperties -ne $false) { throw "Shared definition '$($entry.Value)' must reject unspecified v1 fields." }
  $validated.Add($entry.Key)
}

$unexpected = @(Get-ChildItem -LiteralPath $schemaRoot -Filter '*.schema.json' -File | Where-Object { $_.Name -ne 'governance-record.schema.json' -and $_.Name -notin $expected.Keys })
if ($unexpected.Count -gt 0) { throw "Unexpected v1 entry schema(s): $($unexpected.Name -join ', ')" }

[pscustomobject]@{
  result = 'PASS'
  schema_dialect = 'https://json-schema.org/draft/2020-12/schema'
  shared_library = 'governance-record.schema.json'
  validated_entry_schemas = $validated
  validated_count = $validated.Count
  validation_boundary = 'Structural schema contract only; YAML instance parsing and lifecycle validation are deferred to TUN-P0-004.'
} | ConvertTo-Json -Depth 10
