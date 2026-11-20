using Tunner.Testing.Totp;
using Xunit;
using Xunit.Abstractions;

namespace Tunner.Identity.Persistence.Tests;

// No database fixture: this checks fixture inputs, not backend runtime qualification.
public sealed class NativeTotpCodeFixtureTests(ITestOutputHelper output)
{
    [Fact]
    public void CapturedInstantPreservesCounterIdentityAcrossWallClockBoundary()
    {
        output.WriteLine("Identity version: " + NativeTotpCodeFixture.IdentityVersion);
        output.WriteLine("Identity informational version: " + NativeTotpCodeFixture.IdentityInformationalVersion);
        var instant = new DateTimeOffset(2026, 11, 1, 12, 0, 29, TimeSpan.Zero);
        var seed = new string('A', 32); // Fixed synthetic input only.
        foreach (var offset in Enumerable.Range(-2, 5))
        {
            var captured = NativeTotpCodeFixture.NativeCode(seed, instant, offset);
            Assert.True(captured == NativeTotpCodeFixture.NativeCode(seed, instant.AddSeconds(-29), offset),
                "Instants in the same period must select the same native counter.");
            Assert.True(captured == NativeTotpCodeFixture.NativeCode(seed, instant.AddSeconds(1), offset - 1),
                "A boundary rollover plus a compensating offset must select the captured counter.");
        }
    }
}
