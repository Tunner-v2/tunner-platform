[CmdletBinding()]
param([AllowNull()][AllowEmptyString()][string]$Result)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# GitHub reports the result of the complete, fail-fast:false scan matrix here.
# Pending,missing,skipped,cancelled and failed checks must never approve integration.
if ($Result -cne 'success') { throw "Required container scan matrix did not succeed: '$Result'." }
Write-Output 'PASS: all declared image scans succeeded.'
