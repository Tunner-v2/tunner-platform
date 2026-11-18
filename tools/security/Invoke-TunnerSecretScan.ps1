[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string[]]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path }
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path

$rules = @(
    @{ Name = "private-key"; Pattern = '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----' },
    @{ Name = "github-token"; Pattern = '\b(?:ghp|github_pat)_[A-Za-z0-9_]{20,}\b' },
    @{ Name = "slack-token"; Pattern = '\bxox[baprs]-[A-Za-z0-9-]{20,}\b' },
    @{ Name = "generic-secret-assignment"; Pattern = '(?i)\b(?:password|secret|token|api[_-]?key)\s*[:=]\s*["'']?[A-Za-z0-9_+/=-]{12,}' }
)

# A reviewed benign match is bound to one exact repository path, rule and UTF-8
# match digest. No directory, marker-prefix or other-rule exclusions are supported.
$allowancePath = Join-Path $RepositoryRoot 'tools/security/secret-scan-allowances.json'
$allowances = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
try {
    $configuration = [IO.File]::ReadAllText($allowancePath) | ConvertFrom-Json -AsHashtable
    if ($configuration -isnot [System.Collections.IDictionary] -or $configuration.Count -ne 2 -or
        -not $configuration.Contains('schema_version') -or -not $configuration.Contains('allowances') -or
        $configuration.schema_version -isnot [long] -or $configuration.schema_version -ne 1 -or
        $configuration.allowances -isnot [array] -or $configuration.allowances.Count -eq 0) {
        throw 'Invalid configuration shape.'
    }
    foreach ($allowance in $configuration.allowances) {
        if ($allowance -isnot [System.Collections.IDictionary] -or $allowance.Count -ne 4 -or
            -not $allowance.Contains('path') -or -not $allowance.Contains('rule') -or
            -not $allowance.Contains('match_sha256') -or -not $allowance.Contains('reason') -or
            $allowance.path -isnot [string] -or $allowance.path -cnotmatch '^[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*$' -or
            @($allowance.path.Split('/') | Where-Object { $_ -in @('.', '..') }).Count -ne 0 -or
            $allowance.rule -isnot [string] -or $allowance.rule -cne 'generic-secret-assignment' -or
            $allowance.match_sha256 -isnot [string] -or $allowance.match_sha256 -cnotmatch '^[0-9a-f]{64}$' -or
            $allowance.reason -isnot [string] -or [string]::IsNullOrWhiteSpace($allowance.reason)) {
            throw 'Invalid allowance shape.'
        }
        $key = $allowance.path + "`0" + $allowance.rule + "`0" + $allowance.match_sha256
        if (-not $allowances.Add($key)) { throw 'Duplicate allowance.' }
    }
}
catch {
    # JSON/parser diagnostics can contain input values. Emit only the fixed code.
    throw 'Invalid secret-scan allowance configuration.'
}

if ($null -ne $Path -and $Path.Count -gt 0) {
    $candidateFiles = @($Path | ForEach-Object { if ([IO.Path]::IsPathRooted($_)) { $_ } else { Join-Path $RepositoryRoot $_ } })
}
else {
    $candidateFiles = @(& git -C $RepositoryRoot ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw "git ls-files failed with exit code $LASTEXITCODE." }
    $candidateFiles = @($candidateFiles | ForEach-Object { Join-Path $RepositoryRoot $_ })
}

$findings = [System.Collections.Generic.List[object]]::new()
$reviewedMatchCount = 0
foreach ($candidateFile in $candidateFiles) {
    if (-not (Test-Path -LiteralPath $candidateFile -PathType Leaf)) { continue }
    $relativePath = [IO.Path]::GetRelativePath($RepositoryRoot, [IO.Path]::GetFullPath($candidateFile)).Replace('\', '/')
    if ($relativePath -eq "tools/security/Invoke-TunnerSecretScan.ps1") { continue }

    $bytes = [IO.File]::ReadAllBytes($candidateFile)
    if ($bytes -contains 0) { continue }
    $content = [Text.Encoding]::UTF8.GetString($bytes)
    foreach ($rule in $rules) {
        $matches = [regex]::Matches($content, $rule.Pattern)
        foreach ($match in $matches) {
            $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($match.Value))).ToLowerInvariant()
            if ($allowances.Contains($relativePath + "`0" + $rule.Name + "`0" + $digest)) {
                $reviewedMatchCount++
                continue
            }
            $line = ($content.Substring(0, $match.Index) -split "`n").Count
            $findings.Add([PSCustomObject]@{ Path = $relativePath; Line = $line; Rule = $rule.Name })
        }
    }
}

if ($findings.Count -gt 0) {
    $summary = $findings | ForEach-Object { "$($_.Path):$($_.Line) [$($_.Rule)]" }
    throw "Potential committed secret detected. Remove or rotate it before continuing: $($summary -join '; ')"
}

Write-Host "Tunner secret scan passed: $($candidateFiles.Count) file(s) inspected; no unreviewed rule match found; $reviewedMatchCount exact reviewed match(es)."
