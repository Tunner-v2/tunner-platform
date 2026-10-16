using System.Text.Json;
using Tunner.Installer;
using Xunit;

namespace Tunner.Installer.Tests;

public sealed class ExecutionReceiptTests
{
    [Theory]
    [InlineData(true, true, true, null, true)]
    [InlineData(true, true, false, null, false)]
    [InlineData(true, false, true, "CLEANUP_FAILED", false)]
    public void BindsActualToolAndExecutionIntervalWithoutPromotingCleanupFailure(
        bool content, bool cleanup, bool bootstrap, string? failure, bool expected)
    {
        var profile = InstallerTests.Profile();
        var started = DateTimeOffset.UtcNow;
        var receipt = R2ExecutionReceipt.Create(profile, started, started.AddSeconds(1), "fixture-object",
            content, cleanup, bootstrap, failure);
        Assert.Equal(expected, receipt.Passed);
        Assert.Matches("^[a-f0-9]{64}$", receipt.ToolSha256);
        Assert.Matches("^[a-f0-9]{40}$", receipt.BuildSourceRevision);
        Assert.Equal(started, receipt.StartedAtUtc);
        Assert.Equal(started.AddSeconds(1), receipt.CompletedAtUtc);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(receipt));
        Assert.Equal(profile.Bucket, json.RootElement.GetProperty("Bucket").GetString());
        Assert.False(json.RootElement.TryGetProperty("SecretKey", out _));
        Assert.False(json.RootElement.TryGetProperty("OpenBaoToken", out _));
    }

    [Fact]
    public void RejectsInvertedExecutionTime()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<InvalidOperationException>(() => R2ExecutionReceipt.Create(InstallerTests.Profile(),
            now, now.AddSeconds(-1), "fixture-object", true, true, true, null));
    }
}
