[CmdletBinding()]
param([Parameter(Mandatory)][string]$RepositoryRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
function Require-File([string]$relativePath) { $path = Join-Path $repositoryRoot $relativePath; if (-not (Test-Path -LiteralPath $path)) { throw "TUN-P1-007 missing required file: $relativePath" }; return Get-Content -Raw -LiteralPath $path }

$durableStore = Require-File 'src/Tunner.Database/DurableWork/EfDurableWorkStore.cs'
$inboxStore = Require-File 'src/Tunner.Database/DurableWork/EfConsumerInboxStore.cs'
$model = Require-File 'src/Tunner.Database/DurableWork/DurableWorkModelConfiguration.cs'
$migration = Require-File 'src/Tunner.Database.Migrations/Migrations/20261002190000_P1DurableWorkInbox.cs'
$tests = Require-File 'tests/Tunner.Database.IntegrationTests/DurableWorkInboxIntegrationTests.cs'
$runbook = Require-File 'docs/development/LOCAL_DURABLE_WORK_INBOX.md'
foreach ($marker in @('ClaimAvailableAsync', 'TryCompleteAsync', 'TryReleaseAsync', 'ExecuteUpdateAsync', 'RetryEligibleAtUtc', 'LeaseToken')) { if (-not $durableStore.Contains($marker)) { throw "TUN-P1-007 durable-work store is missing required marker: $marker" } }
foreach ($marker in @('TryReserveAsync', 'ON CONFLICT', 'TryCompleteAsync', 'TryReleaseAsync', 'consumer_inbox')) { if (-not $inboxStore.Contains($marker)) { throw "TUN-P1-007 inbox store is missing required marker: $marker" } }
foreach ($marker in @('durable_work_items', 'consumer_inbox', 'IX_durable_work_items_claim')) { if (-not $model.Contains($marker) -or -not $migration.Contains($marker)) { throw "TUN-P1-007 mapping or migration is missing required marker: $marker" } }
foreach ($marker in @('ConcurrentClaims', 'ReclaimsAnExpiredLease', 'ReleasesWorkAtCallerSuppliedEligibility', 'ConsumerInbox', 'RejectsInvalidLeaseArguments')) { if (-not $tests.Contains($marker)) { throw "TUN-P1-007 integration coverage is missing required scenario marker: $marker" } }
foreach ($forbidden in @('BasicConsume', 'AsyncEventingBasicConsumer', 'IModel', 'RabbitMQ.Client', 'Common', 'Utils', 'Helpers', 'http://', 'https://')) { if ($durableStore.Contains($forbidden) -or $inboxStore.Contains($forbidden)) { throw "TUN-P1-007 persistence source contains forbidden scope marker: $forbidden" } }
if (-not $runbook.Contains('does not start a worker')) { throw 'TUN-P1-007 runbook does not state the runtime boundary.' }
Write-Output 'TUN-P1-007 static durable-work/inbox source validation passed.'