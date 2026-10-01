[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("status", "apply-policies")]
    [string]$Command
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$module = Join-Path $PSScriptRoot "../lib/Tunner.RepositoryRoot.psm1"
Import-Module $module -Force
$repositoryRoot = Resolve-TunnerRepositoryRoot -StartDirectory $PSScriptRoot
$composeFile = Join-Path $repositoryRoot "infra/docker/compose.yaml"
$composeProjectDirectory = Split-Path -Parent $composeFile
$defaultEnvFile = Join-Path $repositoryRoot "infra/docker/.env.example"
$localEnvFile = Join-Path $repositoryRoot "infra/docker/.env.local"
$envFile = if (Test-Path -LiteralPath $localEnvFile) { $localEnvFile } else { $defaultEnvFile }
$policyDirectory = Join-Path $repositoryRoot "infra/openbao/policies"

function Invoke-OpenBaoCompose {
    param([string[]]$Arguments)

    & docker compose --project-directory $composeProjectDirectory --env-file $envFile --file $composeFile @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "OpenBao Docker Compose command failed with exit code $LASTEXITCODE."
    }
}

function Get-PlainTextSecret {
    param([System.Security.SecureString]$SecureValue)

    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker Desktop with Docker Compose is required. Run ./tools/dev/tunner-dev.ps1 doctor first."
}

switch ($Command) {
    "status" {
        Invoke-OpenBaoCompose -Arguments @("exec", "-T", "openbao", "bao", "status")
        Write-Host "OpenBao status was queried without reading or writing secrets."
    }
    "apply-policies" {
        $secureRootToken = Read-Host -Prompt "Enter the ephemeral local OpenBao root token" -AsSecureString
        $rootToken = Get-PlainTextSecret -SecureValue $secureRootToken
        try {
            $mountList = & docker compose --project-directory $composeProjectDirectory --env-file $envFile --file $composeFile exec -T --env "BAO_TOKEN=$rootToken" openbao bao secrets list -format=json
            if ($LASTEXITCODE -ne 0) { throw "OpenBao mount listing failed with exit code $LASTEXITCODE." }
            $mounts = $mountList | ConvertFrom-Json
            if ($null -eq $mounts.'tunner/') {
                & docker compose --project-directory $composeProjectDirectory --env-file $envFile --file $composeFile exec -T --env "BAO_TOKEN=$rootToken" openbao bao secrets enable -path=tunner kv-v2
                if ($LASTEXITCODE -ne 0) { throw "OpenBao KV-v2 mount creation failed with exit code $LASTEXITCODE." }
            }

            foreach ($policyName in @("tunner-local-bootstrap", "tunner-local-runtime")) {
                $policyPath = Join-Path $policyDirectory "$policyName.hcl"
                if (-not (Test-Path -LiteralPath $policyPath)) { throw "Missing OpenBao policy: $policyPath" }

                Get-Content -LiteralPath $policyPath -Raw |
                    & docker compose --project-directory $composeProjectDirectory --env-file $envFile --file $composeFile exec -T --env "BAO_TOKEN=$rootToken" openbao bao policy write $policyName -
                if ($LASTEXITCODE -ne 0) {
                    throw "OpenBao policy write failed for $policyName with exit code $LASTEXITCODE."
                }
            }
        }
        finally {
            Remove-Variable rootToken -ErrorAction SilentlyContinue
            Remove-Variable secureRootToken -ErrorAction SilentlyContinue
        }
        Write-Host "OpenBao policies were applied. No token or secret value was written to the repository or echoed."
    }
}