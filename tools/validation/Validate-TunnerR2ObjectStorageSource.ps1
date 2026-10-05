[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'
$platformRoot = Join-Path $RepositoryRoot 'src/Tunner.Platform/ObjectStorage'
$adapterPath = Join-Path $platformRoot 'R2/AwsR2S3Client.cs'
$contractPath = Join-Path $platformRoot 'IObjectStorage.cs'
$testPath = Join-Path $RepositoryRoot 'tests/Tunner.Platform.ArchitectureTests/R2ObjectStorageTests.cs'

foreach ($path in @($adapterPath, $contractPath, $testPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Required P1 R2 source is missing: $path" }
}

$adapter = Get-Content -LiteralPath $adapterPath -Raw
$contract = Get-Content -LiteralPath $contractPath -Raw
$test = Get-Content -LiteralPath $testPath -Raw

foreach ($marker in @('IAmazonS3', 'PutObjectAsync', 'GetObjectAsync', 'DeleteObjectAsync', 'DisablePayloadSigning = true', 'DisableDefaultChecksumValidation = true')) {
    if ($adapter.IndexOf($marker, [StringComparison]::Ordinal) -lt 0) { throw "R2 adapter is missing required marker: $marker" }
}

if ($contract.IndexOf('interface IObjectStorage', [StringComparison]::Ordinal) -lt 0) { throw 'Provider-neutral object-storage contract is missing.' }
if ($test.IndexOf('ProbeR2S3Client', [StringComparison]::Ordinal) -lt 0) { throw 'Deterministic R2 provider seam test is missing.' }

foreach ($forbidden in @('AmazonS3Config', 'BasicAWSCredentials', 'ServiceURL', 'r2.cloudflarestorage.com', 'accessKey', 'secretKey')) {
    if ($adapter.IndexOf($forbidden, [StringComparison]::Ordinal) -ge 0) { throw "R2 adapter must not contain runtime provider configuration or credentials: $forbidden" }
}

Write-Output 'TUN-P1-009 static R2 object-storage source validation passed.'
