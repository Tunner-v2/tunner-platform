[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [switch]$SkipComposeConfig
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path }

$requiredFiles = @(
    "infra/docker/compose.yaml",
    "infra/docker/.env.example",
    "infra/openbao/config/openbao.hcl",
    "tools/dev/tunner-dev.ps1",
    "docs/development/LOCAL_DOCKER.md"
)

foreach ($relativePath in $requiredFiles) {
    $fullPath = Join-Path $RepositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "Missing required P0-006 artifact: $relativePath"
    }
}

$composePath = Join-Path $RepositoryRoot "infra/docker/compose.yaml"
$compose = Get-Content -LiteralPath $composePath -Raw
$envExample = Get-Content -LiteralPath (Join-Path $RepositoryRoot "infra/docker/.env.example") -Raw
$openBao = Get-Content -LiteralPath (Join-Path $RepositoryRoot "infra/openbao/config/openbao.hcl") -Raw
$tool = Get-Content -LiteralPath (Join-Path $RepositoryRoot "tools/dev/tunner-dev.ps1") -Raw

foreach ($service in @("postgres:", "rabbitmq:", "redis:", "openbao:", "mailpit:", "otel-lgtm:", "api-placeholder:", "worker-placeholder:")) {
    if (-not $compose.Contains($service)) { throw "Compose service missing: $service" }
}

foreach ($image in @("postgres:18.6", "rabbitmq:4.3.6-management", "redis:8.10.2", "openbao/openbao:2.7.0", "axllent/mailpit:v1.31.3", "grafana/otel-lgtm:0.34.0")) {
    if (-not $envExample.Contains($image)) { throw "Pinned image is missing from .env.example: $image" }
}

if ($compose -match '(?im)^\s*image:\s*.*:(latest|edge)\s*$' -or $envExample -match '(?im)=.*:(latest|edge)\s*$') {
    throw "Mutable latest or edge image tag found."
}
if ($compose -match '(?im)minio|object-store|cloudflare|r2') { throw "P0-006 Compose must not introduce object-store emulation or R2 configuration." }
if ($compose -match '(?im)(root[_-]?token|dev[_-]?root[_-]?token)') { throw "P0-006 Compose must not contain an OpenBao root token." }
if ($envExample -match '(?im)(password|secret|token|api[_-]?key)\s*=\s*\S+') { throw ".env.example must not contain credentials or secrets." }

$publishedPorts = [regex]::Matches($compose, '(?m)^\s*-\s*"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
foreach ($publishedPort in $publishedPorts) {
    if ($publishedPort -notmatch '^127\.0\.0\.1:') { throw "Published port is not loopback-bound: $publishedPort" }
}

if ($openBao -notmatch 'storage\s+"(file|raft)"' -or $openBao -notmatch 'tls_disable\s*=\s*1') {
    throw "OpenBao local transport/storage configuration is incomplete."
}
$openBaoConfiguration = ($openBao -split "`r?`n" | Where-Object { $_ -notmatch '^\s*#' }) -join "`n"
if ($openBaoConfiguration -match '(?im)(token|secret|password)') { throw "OpenBao configuration must not contain credentials or secrets." }

foreach ($command in @('"doctor"', '"setup"', '"start"', '"stop"', '"health"', '"logs"')) {
    if (-not $tool.Contains($command)) { throw "tunner-dev command missing: $command" }
}
if ($tool -notmatch '"compose",' -or $tool -notmatch '--wait') { throw "tunner-dev must invoke Docker Compose and wait for service health when starting." }

$otelLgtm = [regex]::Match($compose, '(?ms)^  otel-lgtm:\r?\n(?<body>.*?)(?=^  [a-z-]+:|\z)').Groups['body'].Value
if ([string]::IsNullOrWhiteSpace($otelLgtm)) { throw "Unable to isolate the LGTM Compose service for health validation." }
if ($otelLgtm -match '(?i)wget') { throw "LGTM health check must not depend on wget; the selected image does not include it." }
if ($otelLgtm -notmatch 'test:\s*\["CMD-SHELL",\s*"test -f /tmp/ready"\]') { throw "LGTM health check must use the image startup readiness sentinel." }

if (-not $SkipComposeConfig) {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw "Docker is required to validate Compose configuration." }
    & docker compose --env-file (Join-Path $RepositoryRoot "infra/docker/.env.example") --file $composePath config --quiet
    if ($LASTEXITCODE -ne 0) { throw "docker compose config failed with exit code $LASTEXITCODE." }
}

Write-Host "TUN-P0-006 source validation passed."
