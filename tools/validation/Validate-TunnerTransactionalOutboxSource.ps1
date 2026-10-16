[CmdletBinding()]
param([string]$RepositoryRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$module = Join-Path $PSScriptRoot "../lib/Tunner.RepositoryRoot.psm1"
Import-Module $module -Force
$repositoryRoot = Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
$required = @(
    'src/Tunner.Database/Outbox/OutboxMessage.cs',
    'src/Tunner.Database/Outbox/OutboxModelConfiguration.cs',
    'src/Tunner.Database/Outbox/ITransactionalOutboxContext.cs',
    'src/Tunner.Database/Outbox/ITransactionalOutbox.cs',
    'src/Tunner.Database/Outbox/EfTransactionalOutbox.cs',
    'src/Tunner.Database/TunnerMigrationsDbContext.cs',
    'src/Tunner.Database.Migrations/Migrations/20260930173000_P0MigrationFoundation.cs',
    'src/Tunner.Database.Migrations/Migrations/20261001230000_P1TransactionalOutbox.cs',
    'src/Tunner.Database.Migrations/Migrations/TunnerMigrationsDbContextModelSnapshot.cs',
    'tests/Tunner.Database.IntegrationTests/Tunner.Database.IntegrationTests.csproj',
    'tests/Tunner.Database.IntegrationTests/TransactionalOutboxIntegrationTests.cs',
    'tools/dev/tunner-test.ps1',
    'docs/development/LOCAL_TRANSACTIONAL_OUTBOX.md')
foreach ($relativePath in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath))) {
        throw "Missing TUN-P1-003 artifact: $relativePath"
    }
}
$context = Get-Content -Raw (Join-Path $repositoryRoot 'src/Tunner.Database/TunnerMigrationsDbContext.cs')
$message = Get-Content -Raw (Join-Path $repositoryRoot 'src/Tunner.Database/Outbox/OutboxMessage.cs')
$modelConfiguration = Get-Content -Raw (Join-Path $repositoryRoot 'src/Tunner.Database/Outbox/OutboxModelConfiguration.cs')
$transactionContext = Get-Content -Raw (Join-Path $repositoryRoot 'src/Tunner.Database/Outbox/ITransactionalOutboxContext.cs')
$transactionContract = Get-Content -Raw (Join-Path $repositoryRoot 'src/Tunner.Database/Outbox/ITransactionalOutbox.cs')
$transaction = Get-Content -Raw (Join-Path $repositoryRoot 'src/Tunner.Database/Outbox/EfTransactionalOutbox.cs')
$p0Migration = Get-Content -Raw (Join-Path $repositoryRoot 'src/Tunner.Database.Migrations/Migrations/20260930173000_P0MigrationFoundation.cs')
$p1Migration = Get-Content -Raw (Join-Path $repositoryRoot 'src/Tunner.Database.Migrations/Migrations/20261001230000_P1TransactionalOutbox.cs')
$integrationProject = Get-Content -Raw (Join-Path $repositoryRoot 'tests/Tunner.Database.IntegrationTests/Tunner.Database.IntegrationTests.csproj')
$integrationTests = Get-Content -Raw (Join-Path $repositoryRoot 'tests/Tunner.Database.IntegrationTests/TransactionalOutboxIntegrationTests.cs')
$testCommand = Get-Content -Raw (Join-Path $repositoryRoot 'tools/dev/tunner-test.ps1')
$documentation = Get-Content -Raw (Join-Path $repositoryRoot 'docs/development/LOCAL_TRANSACTIONAL_OUTBOX.md')
$outboxDbSets = [regex]::Matches($context, 'DbSet<OutboxMessage>\s+OutboxMessages\s*=>\s*Set<OutboxMessage>\(\);')
if ($outboxDbSets.Count -ne 1) {
    throw 'TUN-P1-003 must expose exactly one platform-owned OutboxMessage mapping in the shared production migration context.'
}
if ($context -match 'modelBuilder\.Entity<' -or [regex]::Matches($modelConfiguration, 'modelBuilder\.Entity<').Count -ne 1 -or $context -notmatch 'ITransactionalOutboxContext' -or $modelConfiguration -notmatch 'ToTable\("outbox_messages", "platform"\)' -or $modelConfiguration -notmatch 'HasColumnType\("jsonb"\)') {
    throw 'TUN-P1-003 must map only the platform-owned outbox table with a JSON payload through the narrow transactional context.'
}
if ($message -notmatch 'EventSchemaVersion' -or $message -notmatch 'PublishedAtUtc' -or $message -notmatch 'DataClassification' -or $message -notmatch 'JsonDocument\.Parse') {
    throw 'TUN-P1-003 outbox message must retain the approved generic envelope metadata, nullable published_at metadata, and JSON validation.'
}
if ($transactionContext -notmatch 'BeginOutboxTransactionAsync' -or $transactionContext -notmatch 'AddOutboxMessage' -or $transactionContext -notmatch 'SaveChangesAsync') {
    throw 'TUN-P1-003 requires a narrow transaction capability rather than a general persistence context.'
}
foreach ($source in @($transactionContract, $transaction)) {
    if ($source -match 'TunnerMigrationsDbContext|Func<TunnerMigrationsDbContext|ExecuteSql') {
        throw 'The shared transactional-outbox coordinator must not expose a general EF context or raw-SQL escape hatch.'
    }
}
if ($transaction -notmatch 'BeginOutboxTransactionAsync' -or $transaction -notmatch 'CommitAsync' -or $transaction -notmatch 'RollbackAsync' -or $transaction -notmatch 'writeOwnerStateAsync') {
    throw 'TUN-P1-003 must persist the supplied owner write and outbox message in one explicit transaction.'
}
if ($p0Migration -notmatch '(?s)protected override void Up\(MigrationBuilder migrationBuilder\)\s*\{\s*\}' -or $p0Migration -match '(?i)CreateTable|AlterTable|DropTable|AddColumn|DropColumn|CreateIndex|DropIndex') {
    throw 'The immutable P0 migration must remain empty and free of schema operations.'
}
if ([regex]::Matches($p1Migration, '\bCreateTable\s*\(').Count -ne 1 -or $p1Migration -notmatch 'EnsureSchema\(name: "platform"\)' -or $p1Migration -notmatch 'name: "outbox_messages"' -or $p1Migration -notmatch 'published_at = table\.Column<DateTimeOffset>' -or $p1Migration -notmatch 'type: "jsonb"' -or $p1Migration -notmatch 'throw new NotSupportedException') {
    throw 'TUN-P1-003 requires exactly one forward-only platform outbox migration with canonical nullable published_at metadata.'
}
if ($p1Migration -match '\b(AlterTable|DropTable|AddColumn|DropColumn|AlterColumn|RenameTable|RenameColumn|CreateIndex|DropIndex)\b|test_support|outbox_owner_write_probe|Product|Subscription|Finance|Identity') {
    throw 'The P1 production migration must not mutate existing or test-support/Product/domain tables.'
}
if ($integrationProject -notmatch 'Npgsql.EntityFrameworkCore.PostgreSQL' -or $integrationProject -notmatch 'Tunner.Testing') {
    throw 'TUN-P1-003 integration coverage must use the existing PostgreSQL Testcontainers baseline.'
}
if ($integrationTests -notmatch 'MigrateAsync' -or $integrationTests -notmatch 'OutboxAtomicityTestDbContext' -or $integrationTests -notmatch 'OwnerWriteProbes\.Add' -or $integrationTests -notmatch 'CREATE SCHEMA IF NOT EXISTS test_support' -or $integrationTests -notmatch 'await context.SaveChangesAsync\(cancellationToken\)' -or $integrationTests -notmatch 'ThrowsAsync<DbUpdateException>' -or $integrationTests -notmatch 'TUNNER_RUN_CONTAINER_TESTS') {
    throw 'TUN-P1-003 integration coverage must prove commit and rollback with a test-only owned table through an explicit real-database opt-in.'
}
if ($testCommand -notmatch 'Tunner.Database.IntegrationTests' -or $testCommand -notmatch '\$Profile -eq "integration"') {
    throw 'The integration profile must target the transactional-outbox integration tests.'
}
if ($documentation -notmatch 'published_at' -or $documentation -notmatch 'test_support' -or $documentation -notmatch 'narrow') {
    throw 'TUN-P1-003 documentation must explain canonical envelope compatibility, narrow owner context, and the disposable test schema boundary.'
}
foreach ($source in @($message, $transaction, $p1Migration)) {
    if ($source -match '(?i)RabbitMQ|Inbox|DurableWork|WorkerLease|PublisherConfirm|EventDelivery|EventSubscription|BackgroundWorkItem|DurableTimer') {
        throw 'TUN-P1-003 source includes a later messaging, inbox, or durable-work concern.'
    }
}
Write-Host 'TUN-P1-003 static transactional-outbox source validation passed.'
