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
$composeProjectDirectory = Split-Path -Parent $composeFile
$defaultEnvFile = Join-Path $repositoryRoot "infra/docker/.env.example"
$localEnvFile = Join-Path $repositoryRoot "infra/docker/.env.local"
$envFile = if (Test-Path -LiteralPath $localEnvFile) { $localEnvFile } else { $defaultEnvFile }

function Assert-DockerAvailable {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw "Docker Desktop with Docker Compose is required. Install/start Docker Desktop, then run this command again."
    }
}

function Invoke-Docker {
    param(
        [string[]]$Arguments,
        [string]$Operation
    )

    # Docker Compose writes normal progress/status lines to stderr. Invoke it through the
    # .NET process API, capture both streams, replay them, and use only its exit code.
    $docker = Get-Command docker -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $docker.Source
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.Arguments = (($Arguments | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + $_.Replace('"', '\"') + '"' } else { $_ }
    }) -join ' ')

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) { throw "Unable to start $Operation." }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $standardOutput.GetAwaiter().GetResult()
        $stderr = $standardError.GetAwaiter().GetResult()
        if (-not [string]::IsNullOrWhiteSpace($stdout)) { Write-Host -NoNewline $stdout }
        if (-not [string]::IsNullOrWhiteSpace($stderr)) { Write-Host -NoNewline $stderr }
        $exitCode = $process.ExitCode
    }
    finally {
        $process.Dispose()
    }

    if ($exitCode -ne 0) {
        throw "$Operation failed with exit code $exitCode."
    }
}

function Invoke-Compose {
    param([string[]]$Arguments)

    $composeArguments = @(
        "compose",
        "--project-directory", $composeProjectDirectory,
        "--env-file", $envFile,
        "--file", $composeFile
    ) + $Arguments

    Invoke-Docker -Arguments $composeArguments -Operation "docker compose"
}

function Invoke-Doctor {
    Assert-DockerAvailable
    Invoke-Docker -Arguments @("version") -Operation "docker version"
    Invoke-Docker -Arguments @("compose", "version") -Operation "docker compose version"
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
