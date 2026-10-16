[CmdletBinding()]
param([string]$RepositoryRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$module = Join-Path $PSScriptRoot "../lib/Tunner.RepositoryRoot.psm1"
Import-Module $module -Force
$repositoryRoot = Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
$required = @(
    'Directory.Packages.props',
    'src/Tunner.Database/Outbox/PendingOutboxMessage.cs',
    'src/Tunner.Database/Outbox/IOutboxDispatchStore.cs',
    'src/Tunner.Database/Outbox/EfOutboxDispatchStore.cs',
    'src/Tunner.Worker/Tunner.Worker.csproj',
    'src/Tunner.Worker/Messaging/IConfirmedOutboxPublisher.cs',
    'src/Tunner.Worker/Messaging/OutboxDispatchCoordinator.cs',
    'src/Tunner.Worker/Messaging/RabbitMqConfirmedOutboxPublisher.cs',
    'tests/Tunner.Worker.Tests/Tunner.Worker.Tests.csproj',
    'tests/Tunner.Worker.Tests/OutboxDispatchCoordinatorTests.cs',
    'docs/development/LOCAL_RABBITMQ_OUTBOX_DISPATCH.md')
foreach ($relativePath in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath))) {
        throw "Missing TUN-P1-006 artifact: $relativePath"
    }
}
$packages = Get-Content -Raw (Join-Path $repositoryRoot 'Directory.Packages.props')
$workerProject = Get-Content -Raw (Join-Path $repositoryRoot 'src/Tunner.Worker/Tunner.Worker.csproj')
$store = Get-Content -Raw (Join-Path $repositoryRoot 'src/Tunner.Database/Outbox/EfOutboxDispatchStore.cs')
$coordinator = Get-Content -Raw (Join-Path $repositoryRoot 'src/Tunner.Worker/Messaging/OutboxDispatchCoordinator.cs')
$publisher = Get-Content -Raw (Join-Path $repositoryRoot 'src/Tunner.Worker/Messaging/RabbitMqConfirmedOutboxPublisher.cs')
$tests = Get-Content -Raw (Join-Path $repositoryRoot 'tests/Tunner.Worker.Tests/OutboxDispatchCoordinatorTests.cs')
$documentation = Get-Content -Raw (Join-Path $repositoryRoot 'docs/development/LOCAL_RABBITMQ_OUTBOX_DISPATCH.md')
if ($packages -notmatch 'PackageVersion Include="RabbitMQ\.Client" Version="7\.2\.1"' -or $workerProject -notmatch 'PackageReference Include="RabbitMQ\.Client"' -or $workerProject -notmatch 'ProjectReference Include="\.\./Tunner\.Database/Tunner\.Database\.csproj"') {
    throw 'TUN-P1-006 requires centrally managed RabbitMQ.Client 7.2.1 and the explicit Worker-to-Database technical dependency.'
}
if ($store -notmatch 'AsNoTracking\(\)' -or $store -notmatch 'PublishedAtUtc == null' -or $store -notmatch 'OrderBy\(message => message\.OccurredAtUtc\)' -or $store -notmatch 'ThenBy\(message => message\.EventId\)' -or $store -notmatch 'ExecuteUpdateAsync' -or $store -notmatch 'Guid\.Empty') {
    throw 'TUN-P1-006 dispatch storage must read pending rows deterministically and conditionally mark only a stable pending event.'
}
$publishIndex = $coordinator.IndexOf('PublishConfirmedAsync', [StringComparison]::Ordinal)
$markIndex = $coordinator.IndexOf('TryMarkPublishedAsync', [StringComparison]::Ordinal)
if ($publishIndex -lt 0 -or $markIndex -lt 0 -or $publishIndex -ge $markIndex -or $coordinator -notmatch 'ArgumentOutOfRangeException\.ThrowIfLessThan') {
    throw 'TUN-P1-006 must publish with confirmation before conditionally marking a bounded positive batch.'
}
if ($publisher -notmatch 'SemaphoreSlim' -or $publisher -notmatch 'CreateChannelOptions\(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true\)' -or $publisher -notmatch 'ExchangeDeclareAsync' -or $publisher -notmatch 'ExchangeType\.Topic' -or $publisher -notmatch 'BasicPublishAsync' -or $publisher -notmatch 'mandatory: true' -or $publisher -notmatch 'DeliveryModes\.Persistent' -or $publisher -notmatch 'CreateEnvelopeHeaders' -or $publisher -notmatch 'envelope_version' -or $publisher -notmatch 'event_schema_version' -or $publisher -notmatch 'occurred_at_utc' -or $publisher -notmatch 'correlation_id' -or $publisher -notmatch 'connection is null || !connection.IsOpen' -or $publisher -notmatch 'ThrowIfDisposed') {
    throw 'TUN-P1-006 requires serialized RabbitMQ.Client confirmed publishing through a durable internal topic exchange.'
}
foreach ($forbidden in @('QueueDeclareAsync', 'QueueBindAsync', 'BasicConsumeAsync', 'AsyncEventingBasicConsumer', 'AsyncDefaultBasicConsumer', 'BasicAckAsync', 'BasicNackAsync', 'Inbox', 'Retry', 'Redrive', 'DeadLetter', 'Tunner\.Api')) {
    if ($publisher -match $forbidden -or $coordinator -match $forbidden -or $store -match $forbidden) {
        throw "TUN-P1-006 source contains a deferred concern: $forbidden"
    }
}
if ($tests -notmatch 'LeavesTheFailedAndRemainingMessagesPendingWhenConfirmationFails' -or $tests -notmatch 'DoesNotCountAConditionalMarkThatAnotherDispatcherAlreadyCompleted' -or $tests -notmatch 'RejectsANonPositiveBatchWithoutReadingTheStore') {
    throw 'TUN-P1-006 deterministic tests must cover publisher failure, a conditional-mark race, and invalid batch handling.'
}
if ($documentation -notmatch 'at-least-once' -or $documentation -notmatch 'TUN-P1-007' -or $documentation -notmatch 'not automatically registered' -or $documentation -notmatch 'TUN-P1-016') {
    throw 'TUN-P1-006 documentation must state at-least-once behavior and the deferred registration/consumer boundaries.'
}
Write-Host 'TUN-P1-006 static RabbitMQ dispatch source validation passed.'
