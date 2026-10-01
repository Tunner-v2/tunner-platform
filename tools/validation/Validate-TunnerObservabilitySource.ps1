[CmdletBinding()]
param([string]$RepositoryRoot)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepositoryRoot = (Resolve-Path (Join-Path $scriptDirectory '../..')).Path
}
$required = @(
    'src/Tunner.Observability/Tunner.Observability.csproj',
    'src/Tunner.Observability/TunnerObservabilityOptions.cs',
    'src/Tunner.Observability/TelemetryAttributePolicy.cs',
    'src/Tunner.Observability/TunnerTelemetry.cs',
    'src/Tunner.Observability/TunnerTelemetryProcessors.cs',
    'src/Tunner.Observability/TunnerObservabilityServiceCollectionExtensions.cs',
    'tests/Tunner.Observability.FunctionalTests/Program.cs',
    'docs/development/LOCAL_OBSERVABILITY.md'
)
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $path))) { throw "Missing P0-009 artifact: $path" }
}

$packages = Get-Content -Raw (Join-Path $RepositoryRoot 'Directory.Packages.props')
foreach ($package in @('OpenTelemetry', 'OpenTelemetry.Exporter.OpenTelemetryProtocol', 'OpenTelemetry.Extensions.Hosting')) {
    $packagePattern = 'PackageVersion Include="' + [regex]::Escape($package) + '" Version="1\.19\.1"'
    if ($packages -notmatch $packagePattern) { throw "P0-009 must pin $package 1.19.1." }
}

$options = Get-Content -Raw (Join-Path $RepositoryRoot 'src/Tunner.Observability/TunnerObservabilityOptions.cs')
$policy = Get-Content -Raw (Join-Path $RepositoryRoot 'src/Tunner.Observability/TelemetryAttributePolicy.cs')
$registration = Get-Content -Raw (Join-Path $RepositoryRoot 'src/Tunner.Observability/TunnerObservabilityServiceCollectionExtensions.cs')
$processors = Get-Content -Raw (Join-Path $RepositoryRoot 'src/Tunner.Observability/TunnerTelemetryProcessors.cs')
if ($options -notmatch '127\.0\.0\.1:24318' -or $options -notmatch 'IsLoopback' -or $options -notmatch 'OTEL_EXPORTER_OTLP_ENDPOINT') { throw 'P0-009 must provide a loopback-only configurable OTLP endpoint.' }
if ($policy -notmatch 'AllowedAttributeKeys' -or $policy -notmatch 'tunner\.operation_id' -or $policy -notmatch 'tunner\.causation_id') { throw 'P0-009 must use an explicit safe correlation allow-list.' }
if ($registration -notmatch 'AddOpenTelemetry' -or $registration -notmatch 'AddOtlpExporter' -or $registration -notmatch 'HttpProtobuf') { throw 'P0-009 must register backend-neutral OpenTelemetry/OTLP export.' }
if ($processors -notmatch 'BaseProcessor<Activity>' -or $processors -notmatch 'BaseProcessor<LogRecord>' -or $processors -notmatch 'record\.Exception = null') { throw 'P0-009 must redact unapproved activity and log data before export.' }
if ($registration -match '(?i)password|secret|api[_-]?key|authorization') { throw 'P0-009 registration must not embed credential material.' }
Write-Host 'TUN-P0-009 static observability source validation passed.'
