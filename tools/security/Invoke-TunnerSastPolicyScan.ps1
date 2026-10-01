[CmdletBinding()]
param([string]$RepositoryRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$scannerPath = 'tools/security/Invoke-TunnerSastPolicyScan.ps1'
$extensions = @('.cs', '.csproj', '.props', '.targets', '.ps1', '.sh')
$rules = @(
    @{ Name = 'unsafe-binary-serialization'; Pattern = '\b(?:BinaryFormatter|SoapFormatter|NetDataContractSerializer|LosFormatter|ObjectStateFormatter)\b' },
    @{ Name = 'unsafe-json-polymorphism'; Pattern = '\bTypeNameHandling\s*=' },
    @{ Name = 'insecure-hash-implementation'; Pattern = '\b(?:MD5CryptoServiceProvider|SHA1Managed|SHA1CryptoServiceProvider)\b' },
    @{ Name = 'unsafe-xml-resolver'; Pattern = '\bXmlResolver\s*=' }
)

$tracked = @(& git -C $RepositoryRoot ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw "git ls-files failed with exit code $LASTEXITCODE." }

$findings = [System.Collections.Generic.List[object]]::new()
foreach ($relativePath in $tracked) {
    $normalized = $relativePath.Replace('\', '/')
    if ($normalized -eq $scannerPath) { continue }
    if ($extensions -notcontains [IO.Path]::GetExtension($normalized).ToLowerInvariant()) { continue }

    $fullPath = Join-Path $RepositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { continue }
    $bytes = [IO.File]::ReadAllBytes($fullPath)
    if ($bytes -contains 0) { continue }
    $content = [Text.Encoding]::UTF8.GetString($bytes)
    foreach ($rule in $rules) {
        foreach ($match in [regex]::Matches($content, $rule.Pattern)) {
            $line = ($content.Substring(0, $match.Index) -split "`n").Count
            $findings.Add([PSCustomObject]@{ Path = $normalized; Line = $line; Rule = $rule.Name })
        }
    }
}

if ($findings.Count -gt 0) {
    $summary = $findings | ForEach-Object { "$($_.Path):$($_.Line) [$($_.Rule)]" }
    throw "Tunner SAST policy finding. Remove the unsafe construct or record an approved exception before continuing: $($summary -join '; ')"
}

Write-Host "Tunner SAST policy scan passed: $($tracked.Count) tracked or unignored file(s) inspected; no rule match found."