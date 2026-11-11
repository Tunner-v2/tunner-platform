using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

// PUBLIC NONFUNCTIONAL FIXTURES ONLY: no database, account, native verifier, cookie, network or provider.
public sealed class RecoveryCodeResponseTests
{
    private sealed class PublicFixture : IDisposable
    {
        internal DefaultHttpContext Http { get; } = new();
        internal StepUpBrowserOperation Frame { get; }
        internal RecoveryCodeFirstDelivery Delivery { get; }
        internal PublicFixture()
        {
            var session = Guid.Parse("11111111-1111-1111-1111-111111111111");
            const string account = "ACC-public-nonfunctional"; const string origin = "https://public-recovery.example.test";
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, account),
                new(AuthenticationIdentity.SessionClaim, session.ToString("N")), new(AuthenticationIdentity.OriginClaim, origin)], AuthenticationIdentity.Scheme));
            var caller = StepUpCaller.FromPrincipal(principal, origin, new('B', 64));
            var row = new RecoveryCodeOperationRow { OperationId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                RequestId = Guid.Parse("33333333-3333-3333-3333-333333333333"), SetReference = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                AccountId = account, SessionId = session, Origin = origin, Kind = "GENERATE", ExpectedVersion = 1, ContextDigest = new('A', 64),
                CodesPerSet = 1, MaximumCodesPerSet = 1, FormatProfileVersion = "NATIVE_HEX32_V1", DeliveryProfileVersion = "PUBLIC-NONFUNCTIONAL" };
            using var prepared = new RecoveryCodePreparedSet([new(Guid.Parse("66666666-6666-6666-6666-666666666666"),
                "PUBLIC-NONFUNCTIONAL-NOT-A-NATIVE-VERIFIER", new(new string('A', 32).ToCharArray()))]);
            Delivery = new(row, prepared);
            var operation = RecoveryCodeApplication.Operation(row);
            var context = new UserStepUpOperationContext(new(caller), operation.OperationId, operation.CorrelationId, operation.Scope,
                operation.ResourceId, operation.ExpectedVersion, operation.ContextDigest, "PUBLIC-NONFUNCTIONAL");
            Frame = new(caller, context.Actor, context, new(Guid.Parse("55555555-5555-5555-5555-555555555555"), new('B', 64), "PUBLIC-NONFUNCTIONAL",
                new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));
            Http.Response.Body = new MemoryStream();
        }
        public void Dispose() { Delivery.Dispose(); Http.Response.Body.Dispose(); }
    }

    [Fact]
    public async Task WinningResponsePreservesNativeIdsAndIsOneShotWithNoGenericSerialization()
    {
        using var fixture = new PublicFixture(); var native = new NativeProofCompletion(fixture.Frame.Browser.RequestId,
            Guid.Parse("77777777-7777-7777-7777-777777777777"), Guid.Parse("88888888-8888-8888-8888-888888888888"));
        using var result = new RecoveryCodeProofCompletedResult(fixture.Http, fixture.Frame, fixture.Delivery, native);
        Assert.True(JsonSerializer.Serialize(fixture.Delivery) == "{}" && fixture.Delivery.ToString() == nameof(RecoveryCodeFirstDelivery));
        await result.ExecuteAsync(fixture.Http);
        fixture.Http.Response.Body.Position = 0; using var body = await JsonDocument.ParseAsync(fixture.Http.Response.Body); var root = body.RootElement;
        Assert.True(root.GetProperty("requestId").GetGuid() == native.RequestId && root.GetProperty("attemptId").GetGuid() == native.AttemptId && root.GetProperty("ceremonyId").GetGuid() == native.CeremonyId);
        var delivery = root.GetProperty("recoveryCodeFirstDelivery");
        Assert.True(delivery.GetProperty("stepUpRequestId").GetGuid() == fixture.Frame.Browser.RequestId && delivery.GetProperty("codes").GetArrayLength() == 1 && !root.GetProperty("signInRequired").GetBoolean());
        Assert.True(fixture.Http.Response.Headers.CacheControl == "no-store" && fixture.Http.Response.Headers["Referrer-Policy"] == "no-referrer");
        Assert.True(JsonSerializer.Serialize(result) == "{}" && result.ToString() == nameof(RecoveryCodeProofCompletedResult));
        var repeated = false; try { await result.ExecuteAsync(fixture.Http); } catch (InvalidOperationException) { repeated = true; }
        Assert.True(repeated);
    }

    [Fact]
    public async Task CancellationBeforeWriteDisposesAndWritesNoPayload()
    {
        using var fixture = new PublicFixture(); using var canceled = new CancellationTokenSource(); canceled.Cancel(); fixture.Http.RequestAborted = canceled.Token;
        using var result = new RecoveryCodeProofCompletedResult(fixture.Http, fixture.Frame, fixture.Delivery, null);
        var stopped = false; try { await result.ExecuteAsync(fixture.Http); } catch (OperationCanceledException) { stopped = true; }
        Assert.True(stopped && fixture.Http.Response.Body.Length == 0 && !fixture.Delivery.Matches(fixture.Frame.Caller, fixture.Frame.Operation.Binding));
        result.Dispose();
    }

    [Fact]
    public async Task DisposedUnexecutedResponseCannotLaterWrite()
    {
        using var fixture = new PublicFixture(); var result = new RecoveryCodeProofCompletedResult(fixture.Http, fixture.Frame, fixture.Delivery, null); result.Dispose();
        var rejected = false; try { await result.ExecuteAsync(fixture.Http); } catch (InvalidOperationException) { rejected = true; }
        Assert.True(rejected && fixture.Http.Response.Body.Length == 0 && !fixture.Delivery.Matches(fixture.Frame.Caller, fixture.Frame.Operation.Binding));
    }

    private sealed class PublicWriteFailure : Stream
    {
        internal int Writes { get; private set; }
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { Writes++; throw new IOException("Public write interruption."); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { Writes++; return ValueTask.FromException(new IOException("Public write interruption.")); }
    }
    [Fact]
    public async Task MidWriteFailureDisposesAndCannotRedisclose()
    {
        using var fixture = new PublicFixture(); using var failed = new PublicWriteFailure(); fixture.Http.Response.Body = failed;
        using var result = new RecoveryCodeProofCompletedResult(fixture.Http, fixture.Frame, fixture.Delivery, null);
        var interrupted = false; try { await result.ExecuteAsync(fixture.Http); } catch (IOException) { interrupted = true; }
        Assert.True(interrupted && failed.Writes > 0 && !fixture.Delivery.Matches(fixture.Frame.Caller, fixture.Frame.Operation.Binding));
        var repeated = false; try { await result.ExecuteAsync(fixture.Http); } catch (InvalidOperationException) { repeated = true; }
        Assert.True(repeated);
    }

    [Fact]
    public void NativeOutcomeClonesPreserveUnrelatedWitnessAndClosedShapeRejectsMixedOwners()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111"); const string marker = "PUBLIC-NONFUNCTIONAL";
        var expiry = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var passkey = new PasskeyExecutionValidity(id, marker, id, "https://public-recovery.example.test", marker, marker, new('A', 64), expiry, null, null, null);
        var totp = new TotpExecutionValidity(id, marker, id, "https://public-recovery.example.test", marker, marker, new('A', 64), expiry, null, null, null);
        using var first = UserStepUpOperationApplyOutcome.SessionContinues().ForPasskey(passkey); using var cloned = first.ForTotp(totp);
        Assert.True(ReferenceEquals(cloned.PasskeyValidity, passkey) && ReferenceEquals(cloned.TotpValidity, totp));
        var scope = new StepUpOperation(marker, marker, new(marker, marker), marker, 1, new('A', 64));
        Assert.True(!cloned.ClosedFor(scope));
    }

    [Fact]
    public async Task PrivateRecoveryCohortIsNotDiscoverableAndRejectsAnUnownedFixtureBeforeAllocation()
    {
        // Passive metadata only: never instantiate or dispatch the private cohort.
        var privateType = typeof(RecoveryCodePrivateIntegrationTests);
        Assert.True(!privateType.GetInterfaces().Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IClassFixture<>)));
        Assert.True(privateType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
            .All(method => method.GetCustomAttributesData().All(attribute => !typeof(FactAttribute).IsAssignableFrom(attribute.AttributeType))));
        var fixture = new IdentityPostgresFixture(); var rejected = false;
        try { await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); }
        catch (InvalidOperationException) { rejected = true; }
        catch (Exception error) when (error is not OutOfMemoryException) { }
        finally { await fixture.DisposeAsync(); } // No InitializeAsync and no container exists.
        Assert.True(rejected);
    }
}
