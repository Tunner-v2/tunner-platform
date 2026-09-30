[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("doctor", "setup", "start", "stop", "health", "logs")]
    [string]$Command
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$composeFile = Join-Path $repositoryRoot "infra/docker/compose.yaml"
$defaultEnvFile = Join-Path $repositoryRoot "infra/docker/.env.example"
$localEnvFile = Join-Path $repositoryRoot "infra/docker/.env.local"
$envFile = if (Test-Path -LiteralPath $localEnvFile) { $localEnvFile } else { $defaultEnvFile }

function Assert-DockerAvailable {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw "Docker Desktop with Docker Compose is required. Install/start Docker Desktop, then run this command again."
    }
}

function Invoke-Compose {
    param([string[]]$Arguments)

    $composeArguments = @(
        "compose",
        "--project-directory", $repositoryRoot,
        "--env-file", $envFile,
        "--file", $composeFile
    ) + $Arguments

    & docker @composeArguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose failed with exit code $LASTEXITCODE."
    }
}

function Invoke-Doctor {
    Assert-DockerAvailable
    & docker version
    if ($LASTEXITCODE -ne 0) { throw "docker version failed with exit code $LASTEXITCODE." }
    & docker compose version
    if ($LASTEXITCODE -ne 0) { throw "docker compose version failed with exit code $LASTEXITCODE." }
    Invoke-Compose -Arguments @("config", "--quiet")
    Write-Host "Docker and the Tunner local Compose configuration are ready."
}

switch ($Command) {
    "doctor" { Invoke-Doctor }
    "setup" {
        Invoke-Doctor
        Write-Host "P0-006 needs no credential file. P0-007 owns OpenBao initialization, policy, and application secret injection."
    }
    "start" {
        Invoke-Doctor
        Write-Host "Starting the Tunner local dependency foundation. This pulls images and starts containers."
        Invoke-Compose -Arguments @("up", "--detach", "--wait", "--wait-timeout", "120")
        Write-Host "Local dependency foundation started. Run: ./tools/dev/tunner-dev.ps1 health"
    }
    "stop" {
        Assert-DockerAvailable
        Invoke-Compose -Arguments @("stop")
        Write-Host "Local dependency foundation stopped. Named volumes were preserved."
    }
    "health" {
        Assert-DockerAvailable
        Invoke-Compose -Arguments @("ps", "--format", "json")
        Write-Host "OpenBao is process-healthy when its status is uninitialized, sealed, or unsealed. P0-007 owns bootstrap and secret readiness."
    }
    "logs" {
        Assert-DockerAvailable
        Invoke-Compose -Arguments @("logs", "--tail", "200")
    }
}
