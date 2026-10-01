[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Get-Location).Path,
    [Parameter(Mandatory = $true)][string]$AsOf,
    [ValidateRange(0, 3650)][int]$MaxSourceAgeDays = 14,
    [switch]$SkipSchemaValidation,
    [switch]$SkipSourceFreshness
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$findings = New-Object System.Collections.Generic.List[string]

function Add-Finding([string]$Code, [string]$Path, [string]$Detail) {
    $script:findings.Add("$Code $Path :: $Detail")
}

function Test-WithinRepository([string]$Candidate) {
    $root = [IO.Path]::GetFullPath($repositoryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return [IO.Path]::GetFullPath($Candidate).StartsWith($root, [StringComparison]::OrdinalIgnoreCase)
}

function Test-MarkdownLinks {
    $markdownFiles = New-Object System.Collections.Generic.List[IO.FileInfo]
    foreach ($relativeDirectory in @('docs', 'governance', '.github')) {
        $directory = Join-Path $repositoryRoot $relativeDirectory
        if (Test-Path -LiteralPath $directory -PathType Container) {
            Get-ChildItem -LiteralPath $directory -Filter '*.md' -File -Recurse | ForEach-Object { $markdownFiles.Add($_) }
        }
    }
    foreach ($relativeFile in @('README.md', 'CONTRIBUTING.md')) {
        $path = Join-Path $repositoryRoot $relativeFile
        if (Test-Path -LiteralPath $path -PathType Leaf) { $markdownFiles.Add((Get-Item -LiteralPath $path)) }
    }

    $linkPattern = [regex]'(?<!!)\[[^\]]*\]\((?<target><[^>]+>|[^\s)]+)(?:\s+["''][^)]*["''])?\)'
    foreach ($file in $markdownFiles | Sort-Object FullName -Unique) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($match in $linkPattern.Matches($content)) {
            $target = $match.Groups['target'].Value.Trim().Trim('<', '>')
            if ([string]::IsNullOrWhiteSpace($target) -or $target.StartsWith('#')) { continue }
            if ($target -match '^[a-zA-Z][a-zA-Z0-9+.-]*:' -or $target.StartsWith('//')) { continue }

            $pathPart = ($target -split '#', 2)[0]
            if ([string]::IsNullOrWhiteSpace($pathPart)) { continue }
            try { $pathPart = [Uri]::UnescapeDataString($pathPart) }
            catch { Add-Finding 'DOC_LINK_INVALID' $file.FullName "cannot decode '$target'"; continue }
            if ([IO.Path]::IsPathRooted($pathPart)) { Add-Finding 'DOC_LINK_ESCAPES_REPOSITORY' $file.FullName "rooted target '$target'"; continue }

            $candidate = [IO.Path]::GetFullPath((Join-Path $file.DirectoryName $pathPart))
            if (-not (Test-WithinRepository $candidate)) { Add-Finding 'DOC_LINK_ESCAPES_REPOSITORY' $file.FullName "target '$target'"; continue }
            if (-not (Test-Path -LiteralPath $candidate)) { Add-Finding 'DOC_LINK_MISSING' $file.FullName "target '$target'" }
        }
    }
}

function Test-GovernanceIdentifiers {
    $recordTypes = @(
        @('milestones', 'milestone_id'), @('sprints', 'sprint_id'), @('work-items', 'work_item_id'), @('dependencies', 'dependency_id'),
        @('todos', 'id'), @('defects', 'defect_id'), @('reopens', 'reopen_id'), @('decisions', 'decision_id'),
        @('gates', 'gate_id'), @('releases', 'release_id'), @('context', 'selection_id')
    )
    foreach ($recordType in $recordTypes) {
        $directory = Join-Path (Join-Path $repositoryRoot 'governance') $recordType[0]
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) { continue }
        $seen = @{}
        foreach ($file in Get-ChildItem -LiteralPath $directory -Filter '*.yaml' -File -Recurse | Sort-Object FullName) {
            $line = Select-String -LiteralPath $file.FullName -Pattern ('^' + [regex]::Escape($recordType[1]) + ':\s*(?<id>.*?)\s*$') | Select-Object -First 1
            if ($null -eq $line) { Add-Finding 'DOC_ID_MISSING' $file.FullName "missing $($recordType[1])"; continue }
            $identifier = $line.Matches[0].Groups['id'].Value.Trim().Trim('"', "'")
            if ($identifier -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') { Add-Finding 'DOC_ID_INVALID' $file.FullName "$($recordType[1]) '$identifier'"; continue }
            if ($seen.ContainsKey($identifier)) { Add-Finding 'DOC_ID_DUPLICATE' $file.FullName "$($recordType[1]) '$identifier' also occurs in $($seen[$identifier])"; continue }
            $seen[$identifier] = $file.FullName
        }
    }
}

function Test-AuthorityHeaders {
    foreach ($relativeDirectory in @('docs/authority/baselines/1.6.0', 'docs/authority/amendments')) {
        $directory = Join-Path $repositoryRoot $relativeDirectory
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) { Add-Finding 'DOC_AUTHORITY_ROOT_MISSING' $relativeDirectory 'authority directory is missing'; continue }
        foreach ($file in Get-ChildItem -LiteralPath $directory -Filter '*.md' -File -Recurse | Sort-Object FullName) {
            $relativePath = $file.FullName.Substring($directory.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
            if ($relativePath -match '(^|[\\/])decisions[\\/].*README\.md$') { continue }
            $header = Get-Content -LiteralPath $file.FullName -TotalCount 8
            $isDecisionRecord = $relativePath -match '(^|[\\/])decisions[\\/]'
            $hasAuthority = if ($isDecisionRecord) {
                @($header | Where-Object { $_ -match '^#\s+(ADR|PDR)-[0-9]+' }).Count -gt 0
            } else {
                @($header | Where-Object { $_ -match '^>\s+\*\*Tunner Development Authority (Package|Amendment)\*\*' }).Count -gt 0
            }
            $hasStatus = if ($isDecisionRecord) {
                @($header | Where-Object { $_ -match '^\*\*Status:\*\*' }).Count -gt 0
            } else {
                @($header | Where-Object { $_ -match '^>\s+\*\*Status:\*\*' }).Count -gt 0
            }
            if (-not $hasAuthority) { Add-Finding 'DOC_AUTHORITY_HEADER_MISSING' $file.FullName 'missing approved authority or decision-record header' }
            if (-not $hasStatus) { Add-Finding 'DOC_AUTHORITY_STATUS_MISSING' $file.FullName 'missing Status header' }
        }
    }
}

function Invoke-CheckedCommand([string]$Name, [scriptblock]$Command) {
    try { & $Command }
    catch { Add-Finding 'DOC_SUBVALIDATION_FAILED' $Name $_.Exception.Message; return }
    if ($LASTEXITCODE -ne 0) { Add-Finding 'DOC_SUBVALIDATION_FAILED' $Name "exit code $LASTEXITCODE" }
}

Test-MarkdownLinks
Test-GovernanceIdentifiers
Test-AuthorityHeaders

if (-not $SkipSchemaValidation) {
    $schemaValidator = Join-Path $repositoryRoot 'tools/validation/Validate-GovernanceSchemas.ps1'
    $governanceProject = Join-Path $repositoryRoot 'src/Tunner.Governance/Tunner.Governance.csproj'
    if (-not (Test-Path -LiteralPath $schemaValidator) -or -not (Test-Path -LiteralPath $governanceProject)) { Add-Finding 'DOC_SCHEMA_VALIDATOR_MISSING' 'tools/validation' 'required schema validator or governance project is missing' }
    else {
        Invoke-CheckedCommand 'schema catalog' { & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $schemaValidator }
        Invoke-CheckedCommand 'governance records' { & dotnet run --project $governanceProject --no-build -- --repository $repositoryRoot governance validate }
    }
}

if (-not $SkipSourceFreshness) {
    $governanceProject = Join-Path $repositoryRoot 'src/Tunner.Governance/Tunner.Governance.csproj'
    if (-not (Test-Path -LiteralPath $governanceProject)) { Add-Finding 'DOC_SOURCE_VALIDATOR_MISSING' 'src/Tunner.Governance' 'governance project is missing' }
    else { Invoke-CheckedCommand 'source freshness' { & dotnet run --project $governanceProject --no-build -- --repository $repositoryRoot sources check --as-of $AsOf --max-age-days $MaxSourceAgeDays } }
}

if ($findings.Count -gt 0) {
    $lineBreak = [Environment]::NewLine
    throw ("Documentation validation failed:" + $lineBreak + ($findings -join $lineBreak))
}

[pscustomobject]@{
    result = 'PASS'
    repository_root = $repositoryRoot
    as_of = $AsOf
    max_source_age_days = $MaxSourceAgeDays
    checked = @('repository-local Markdown links', 'canonical governance record identifiers and duplicates by record type', 'authority headers', 'governance schema catalog and records', 'recorded source freshness')
    boundary = 'External URLs are syntax-classified but not fetched; source freshness uses only the local P0-012 registry and caller-supplied evaluation date.'
} | ConvertTo-Json -Depth 5