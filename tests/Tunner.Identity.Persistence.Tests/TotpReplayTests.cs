using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;
using static Tunner.Identity.Persistence.Tests.TotpTests;

namespace Tunner.Identity.Persistence.Tests;

public sealed class TotpReplayTests(IdentityPostgresFixture fixture) : IClassFixture<IdentityPostgresFixture>
{
    private static async Task<string?> Accept(IdentityTestDatabase db, AuthenticationTestHost host, Profile profile, Guid reference, string code, bool rollback = false)
    {
        await using var context = db.Context(); await using var tx = await context.Database.BeginTransactionAsync();
        var account = await context.Set<TotpAuthenticatorRow>().Where(x => x.AuthenticatorReference == reference).Select(x => x.AccountId).SingleAsync();
        _ = await context.Accounts.FromSqlInterpolated($"SELECT * FROM accounts.accounts WHERE account_id={account} FOR NO KEY UPDATE").SingleAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"totp-owner:" + account},0))");
        var factor = await context.Set<TotpAuthenticatorRow>().SingleAsync(x => x.AuthenticatorReference == reference);
        var error = await new TotpNativeVerifier(context, host.Clock).AcceptAsync(factor, profile.Policy, profile.Seed(reference), code, CancellationToken.None);
        await context.SaveChangesAsync(); if (rollback) await tx.RollbackAsync(); else await tx.CommitAsync(); return error;
    }
    [Fact]
    public async Task NativeFullWindowBoundedHistoryRejectsAToBToAAndRetainsActivationClaim()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile(); await using var host = await StartTotp(db, profile); await host.SeedAsync(db);
        var enrolled = await Enroll(host, profile, await Login(host)); var seed = profile.Seed(enrolled.Reference);
        var first = NativeCode(seed, -1); var second = NativeCode(seed, 1);
        Assert.Null(await Accept(db, host, profile, enrolled.Reference, first)); Assert.Null(await Accept(db, host, profile, enrolled.Reference, second));
        Assert.Equal("CODE_REPLAYED", await Accept(db, host, profile, enrolled.Reference, first));
        Assert.Equal("CODE_REPLAYED", await Accept(db, host, profile, enrolled.Reference, NativeCode(seed)));
        Assert.Null(await Accept(db, host, profile, enrolled.Reference, NativeCode(seed, -2))); Assert.Null(await Accept(db, host, profile, enrolled.Reference, NativeCode(seed, 2)));
        await using var read = db.Context(); var claims = await read.Set<TotpUsedCodeRow>().ToArrayAsync(); Assert.Equal(5, claims.Length);
        Assert.All(claims, x => Assert.True(x.ExpiresAt - x.AcceptedAt >= TimeSpan.FromSeconds(150) + profile.Policy.MaximumClockDeviation * 2 + profile.Policy.MaximumVerificationDuration));
    }
    [Fact]
    public async Task ConcurrentIdenticalNativeCodesHaveExactlyOneCommittedAcceptance()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile(); await using var host = await StartTotp(db, profile); await host.SeedAsync(db);
        var enrolled = await Enroll(host, profile, await Login(host)); var code = NativeCode(profile.Seed(enrolled.Reference), 1);
        var result = await Task.WhenAll(Accept(db, host, profile, enrolled.Reference, code), Accept(db, host, profile, enrolled.Reference, code));
        Assert.Single(result, x => x is null); Assert.Single(result, x => x == "CODE_REPLAYED");
        await using var read = db.Context(); Assert.Equal(2, await read.Set<TotpUsedCodeRow>().CountAsync());
    }
    [Fact]
    public async Task RollbackRemovesReplayClaimAndUsageThenOriginalCodeCanBeAccepted()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile(); await using var host = await StartTotp(db, profile); await host.SeedAsync(db);
        var enrolled = await Enroll(host, profile, await Login(host)); var code = NativeCode(profile.Seed(enrolled.Reference), 1);
        Assert.Null(await Accept(db, host, profile, enrolled.Reference, code, rollback: true));
        await using (var read = db.Context()) { Assert.Equal(1, await read.Set<TotpUsedCodeRow>().CountAsync()); Assert.Equal(1, (await read.Set<TotpAuthenticatorRow>().SingleAsync()).UsageVersion); }
        Assert.Null(await Accept(db, host, profile, enrolled.Reference, code));
    }
    [Fact]
    public async Task CapacityClockAndUnsupportedProfileFailClosedWithoutEvictingLiveClaims()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile(); profile.Policy = profile.Policy with { MaximumLiveReplayClaims = 2 };
        await using var host = await StartTotp(db, profile); await host.SeedAsync(db); var enrolled = await Enroll(host, profile, await Login(host)); var seed = profile.Seed(enrolled.Reference);
        Assert.Null(await Accept(db, host, profile, enrolled.Reference, NativeCode(seed, 1)));
        Assert.Equal("REPLAY_CAPACITY_EXHAUSTED", await Accept(db, host, profile, enrolled.Reference, NativeCode(seed, 2)));
        host.Clock.Advance(TimeSpan.FromHours(1)); Assert.Equal("CLOCK_OUT_OF_BOUNDS", await Accept(db, host, profile, enrolled.Reference, NativeCode(seed, 2)));
        await using var read = db.Context(); Assert.Equal(2, await read.Set<TotpUsedCodeRow>().CountAsync());
        Assert.Throws<InvalidOperationException>(() => (profile.Policy with { ReplayRetention = TimeSpan.FromSeconds(149) }).Validate());
        Assert.Throws<InvalidOperationException>(() => (profile.Policy with { MaximumLiveReplayClaims = 1024, ReplayHashIterations = 1_000_000 }).Validate());
    }
    [Theory]
    [InlineData("１２３４５６")] [InlineData("12345")] [InlineData("1234567")] [InlineData("\t123456")] [InlineData("123\u00a0456")]
    public void CanonicalCodeRejectsUnsupportedNumericAndWhitespaceForms(string value) => Assert.Null(TotpNativeVerifier.CanonicalCode(value));
    [Fact]
    public void CanonicalCodePreservesSixAsciiDigitsAndOnlyApprovedGrouping() => Assert.Equal("012345", TotpNativeVerifier.CanonicalCode("01 23-45"));
}
