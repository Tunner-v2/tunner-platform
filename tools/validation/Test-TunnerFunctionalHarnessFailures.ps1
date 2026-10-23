#requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
if (-not (Test-Path -LiteralPath (Join-Path $RepositoryRoot '.tunner-root'))) { throw 'Expected a marked Tunner checkout.' }
$checks = 0
foreach ($project in @('Tunner.Governance.FunctionalTests', 'Tunner.AuditRemediation.FunctionalTests')) {
    foreach ($fault in @('startup', 'body', 'cleanup')) {
        $flag = if ($fault -eq 'body') { '--self-test-failure-reporting' } else { '--self-test-' + $fault + '-failure' }
        $info = [Diagnostics.ProcessStartInfo]::new('dotnet')
        $info.WorkingDirectory = $RepositoryRoot
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        foreach ($argument in @('run', '--no-build', '--no-restore', '--project', ('tests/' + $project), '--', $flag)) { $info.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($info)
        if ($null -eq $process) { throw 'Cannot start the controlled harness check.' }
        try {
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(60000)) { $process.Kill($true); $process.WaitForExit(); throw ('Harness check timed out: ' + $project + '/' + $fault) }
            $outText = $stdout.GetAwaiter().GetResult()
            $errText = $stderr.GetAwaiter().GetResult()
            $marker = if ($fault -eq 'body' -and $project -eq 'Tunner.Governance.FunctionalTests') { 'Controlled harness failure reporting check' } else { 'Controlled harness ' + $fault + ' reporting check' }
            if ($process.ExitCode -ne 1 -or -not $errText.Contains($marker) -or $errText.Contains('Unhandled exception.') -or $outText.Contains('PASS:')) {
                throw ('Expected controlled exit1 with ' + $fault + ' diagnostics: ' + $project + '; actual exit=' + $process.ExitCode + '; stderr=' + $errText + '; stdout=' + $outText)
            }
            $checks++
            Write-Output ('PASS: ' + $project + '/' + $fault + ' controlled exit1')
        } finally { $process.Dispose() }
    }
}
Write-Output ('PASS: ' + $checks + ' functional harness startup/body/cleanup failure checks')
