[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string[]]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path }

$rules = @(
    @{ Name = "private-key"; Pattern = '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----' },
    @{ Name = "github-token"; Pattern = '\b(?:ghp|github_pat)_[A-Za-z0-9_]{20,}\b' },
    @{ Name = "slack-token"; Pattern = '\bxox[baprs]-[A-Za-z0-9-]{20,}\b' },
    @{ Name = "generic-secret-assignment"; Pattern = '(?i)\b(?:password|secret|token|api[_-]?key)\s*[:=]\s*["'']?[A-Za-z0-9_+/=-]{12,}' }
)

if ($null -ne $Path -and $Path.Count -gt 0) {
    $candidateFiles = @($Path | ForEach-Object { if ([IO.Path]::IsPathRooted($_)) { $_ } else { Join-Path $RepositoryRoot $_ } })
}
else {
    $candidateFiles = @(& git -C $RepositoryRoot ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw "git ls-files failed with exit code $LASTEXITCODE." }
    $candidateFiles = @($candidateFiles | ForEach-Object { Join-Path $RepositoryRoot $_ })
}

$findings = [System.Collections.Generic.List[object]]::new()
foreach ($candidateFile in $candidateFiles) {
    if (-not (Test-Path -LiteralPath $candidateFile -PathType Leaf)) { continue }
    $relativePath = [IO.Path]::GetRelativePath($RepositoryRoot, $candidateFile).Replace('\', '/')
    if ($relativePath -eq "tools/security/Invoke-TunnerSecretScan.ps1") { continue }

    $bytes = [IO.File]::ReadAllBytes($candidateFile)
    if ($bytes -contains 0) { continue }
    $content = [Text.Encoding]::UTF8.GetString($bytes)
    foreach ($rule in $rules) {
        $matches = [regex]::Matches($content, $rule.Pattern)
        foreach ($match in $matches) {
            $line = ($content.Substring(0, $match.Index) -split "`n").Count
            $findings.Add([PSCustomObject]@{ Path = $relativePath; Line = $line; Rule = $rule.Name })
        }
    }
}

if ($findings.Count -gt 0) {
    $summary = $findings | ForEach-Object { "$($_.Path):$($_.Line) [$($_.Rule)]" }
    throw "Potential committed secret detected. Remove or rotate it before continuing: $($summary -join '; ')"
}

Write-Host "Tunner secret scan passed: $($candidateFiles.Count) file(s) inspected; no rule match found."