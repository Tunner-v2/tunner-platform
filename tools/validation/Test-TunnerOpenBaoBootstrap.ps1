[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1') -Force
$RepositoryRoot = Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
$helper = Join-Path $RepositoryRoot 'tools/dev/openbao-bootstrap.ps1'
if (Test-Path Variable:\global:TunnerOpenBaoBootstrapFixture) { throw 'A bootstrap fixture is already active.' }
$global:TunnerOpenBaoBootstrapFixture = [pscustomobject]@{
    Credential = [Guid]::NewGuid().ToString('N')
    Scenario = ''
    Observed = New-Object 'System.Collections.Generic.List[string]'
    PromptCount = 0
    PolicyBodies = @{}
}
try {
# These command doubles execute the full production helper without accessing Docker or real secrets.
function Read-Host {
    param([string]$Prompt, [switch]$AsSecureString)
    if (-not $AsSecureString) { throw 'Bootstrap must request private input.' }
    $global:TunnerOpenBaoBootstrapFixture.PromptCount++
    return ConvertTo-SecureString -String $global:TunnerOpenBaoBootstrapFixture.Credential -AsPlainText -Force
}

function docker {
    $arguments = @($args)
    $baoIndex = [Array]::IndexOf($arguments, 'bao')
    if ($baoIndex -lt 0) { throw 'Unexpected fixture command shape.' }
    $operation = @($arguments[($baoIndex + 1)..($arguments.Count - 1)])
    $operationText = $operation -join ' '
    $global:TunnerOpenBaoBootstrapFixture.Observed.Add($operationText)
    $global:LASTEXITCODE = 0
    if ($operationText -eq 'status') {
        Write-Output 'Sealed false'
        return
    }
    $authorization = @($arguments | Where-Object { $_ -like 'BAO_TOKEN=*' })
    if ($authorization.Count -ne 1 -or $authorization[0] -cne ('BAO_TOKEN=' + $global:TunnerOpenBaoBootstrapFixture.Credential)) {
        throw 'Fixture authorization missing or changed.'
    }
    if ($operationText -eq 'secrets list -format=json') {
        if ($global:TunnerOpenBaoBootstrapFixture.Scenario -eq 'list-failure') { $global:LASTEXITCODE = 2; return }
        if ($global:TunnerOpenBaoBootstrapFixture.Scenario -eq 'invalid-json') { Write-Output '{invalid'; return }
        if ($global:TunnerOpenBaoBootstrapFixture.Scenario -eq 'empty') { Write-Output '{}'; return }
        if ($global:TunnerOpenBaoBootstrapFixture.Scenario -eq 'unrelated' -or $global:TunnerOpenBaoBootstrapFixture.Scenario -eq 'create-failure') {
            Write-Output '{"sys/":{"type":"system"}}'
            return
        }
        Write-Output '{"tunner/":{"type":"kv","options":{"version":"2"}}}'
        return
    }
    if ($operationText -eq 'secrets enable -path=tunner kv-v2') {
        if ($global:TunnerOpenBaoBootstrapFixture.Scenario -eq 'create-failure') { $global:LASTEXITCODE = 2 }
        return
    }
    if ($operationText -like 'policy write *') {
        $name = [string]$operation[2]
        $global:TunnerOpenBaoBootstrapFixture.PolicyBodies[$name] = (@($input) -join [Environment]::NewLine).Trim()
        if (($global:TunnerOpenBaoBootstrapFixture.Scenario -eq 'first-policy-failure' -and $name -eq 'tunner-local-bootstrap') -or
            ($global:TunnerOpenBaoBootstrapFixture.Scenario -eq 'second-policy-failure' -and $name -eq 'tunner-local-runtime')) {
            $global:LASTEXITCODE = 2
        }
        return
    }
    throw 'Unexpected fixture operation.'
}

function Assert-Case {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$cases = @(
    @{ Name = 'empty'; Command = 'apply-policies'; Fails = $false; Calls = 4; Creates = 1; Policies = 2 },
    @{ Name = 'unrelated'; Command = 'apply-policies'; Fails = $false; Calls = 4; Creates = 1; Policies = 2 },
    @{ Name = 'existing'; Command = 'apply-policies'; Fails = $false; Calls = 3; Creates = 0; Policies = 2 },
    @{ Name = 'list-failure'; Command = 'apply-policies'; Fails = $true; Calls = 1; Creates = 0; Policies = 0 },
    @{ Name = 'invalid-json'; Command = 'apply-policies'; Fails = $true; Calls = 1; Creates = 0; Policies = 0 },
    @{ Name = 'create-failure'; Command = 'apply-policies'; Fails = $true; Calls = 2; Creates = 1; Policies = 0 },
    @{ Name = 'first-policy-failure'; Command = 'apply-policies'; Fails = $true; Calls = 2; Creates = 0; Policies = 1 },
    @{ Name = 'second-policy-failure'; Command = 'apply-policies'; Fails = $true; Calls = 3; Creates = 0; Policies = 2 },
    @{ Name = 'status'; Command = 'status'; Fails = $false; Calls = 1; Creates = 0; Policies = 0 }
)

foreach ($case in $cases) {
    $global:TunnerOpenBaoBootstrapFixture.Scenario = $case.Name
    $global:TunnerOpenBaoBootstrapFixture.Observed.Clear()
    $global:TunnerOpenBaoBootstrapFixture.PromptCount = 0
    $global:TunnerOpenBaoBootstrapFixture.PolicyBodies = @{}
    $global:LASTEXITCODE = 0
    $capture = New-Object 'System.Collections.Generic.List[string]'
    $failure = $null
    try {
        & $helper $case.Command *>&1 | ForEach-Object { $capture.Add([string]$_) }
    }
    catch { $failure = $_.Exception.Message }
    $failed = $null -ne $failure
    $safeFailure = if ($null -eq $failure) { 'none' } else { $failure.Replace($global:TunnerOpenBaoBootstrapFixture.Credential, '[redacted]') }
    Assert-Case ($failed -eq $case.Fails) ('Unexpected completion for ' + $case.Name + ': ' + $safeFailure)
    Assert-Case ($global:TunnerOpenBaoBootstrapFixture.Observed.Count -eq $case.Calls) ('Unexpected command count for ' + $case.Name)
    $creates = @($global:TunnerOpenBaoBootstrapFixture.Observed | Where-Object { $_ -eq 'secrets enable -path=tunner kv-v2' }).Count
    Assert-Case ($creates -eq $case.Creates) ('Unexpected mount mutation for ' + $case.Name)
    Assert-Case ($global:TunnerOpenBaoBootstrapFixture.PolicyBodies.Count -eq $case.Policies) ('Unexpected policy mutation for ' + $case.Name)
    Assert-Case ($global:TunnerOpenBaoBootstrapFixture.PromptCount -eq $(if ($case.Command -eq 'status') { 0 } else { 1 })) 'Private prompt count changed.'
    foreach ($name in $global:TunnerOpenBaoBootstrapFixture.PolicyBodies.Keys) {
        $expected = [IO.File]::ReadAllText((Join-Path $RepositoryRoot ('infra/openbao/policies/' + $name + '.hcl'))).Trim()
        Assert-Case ($global:TunnerOpenBaoBootstrapFixture.PolicyBodies[$name] -ceq $expected) 'Policy content changed in transit.'
    }
    $captured = ($capture -join [Environment]::NewLine) + $failure
    Assert-Case (-not $captured.Contains($global:TunnerOpenBaoBootstrapFixture.Credential)) 'Fixture private input leaked into output.'
    Write-Output ('PASS: ' + $case.Name)
}
Write-Output ('PASS: 9 complete bootstrap scenarios, private-input redaction and unchanged policy bodies; PowerShell ' + $PSVersionTable.PSVersion)

}
finally {
    $global:TunnerOpenBaoBootstrapFixture.Credential = $null
    Remove-Variable -Name TunnerOpenBaoBootstrapFixture -Scope Global
}
