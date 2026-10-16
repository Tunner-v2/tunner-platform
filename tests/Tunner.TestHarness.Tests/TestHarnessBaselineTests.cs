using Tunner.Testing;
using Xunit;
using Xunit.Sdk;

namespace Tunner.TestHarness.Tests;

public sealed class TestHarnessBaselineTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    [InlineData("1", true)]
    public void ContainerExecutionRequiresExplicitOptIn(string? value, bool expected)
        => AssertOptIn(TestHarnessProfiles.ContainerOptInVariable, value, expected, TestHarnessProfiles.IsContainerExecutionEnabled);

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    [InlineData("1", true)]
    public void BrowserExecutionRequiresExplicitOptIn(string? value, bool expected)
        => AssertOptIn(TestHarnessProfiles.BrowserOptInVariable, value, expected, TestHarnessProfiles.IsBrowserExecutionEnabled);

    private static void AssertOptIn(string variable, string? value, bool expected, Func<bool> enabled)
    {
        var original = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, value);
            Assert.Equal(expected, enabled());
        }
        finally { Environment.SetEnvironmentVariable(variable, original); }
    }

    [Fact]
    public async Task TestcontainersBaselineUsesAThrowawayPostgresDescriptor()
    {
        await using var container = TestcontainersBaseline.CreatePostgresContainer();
        Assert.NotNull(container);
    }

    [Fact]
    public async Task StartedPostgresFixtureAcceptsAnImmediateTcpSqlQuery()
    {
        if (!TestHarnessProfiles.IsContainerExecutionEnabled())
            throw SkipException.ForSkip("Set TUNNER_RUN_CONTAINER_TESTS=1 to run the disposable PostgreSQL readiness probe.");

        await using var container = TestcontainersBaseline.CreatePostgresContainer();
        await container.StartAsync();
        var result = await container.ExecAsync([
            "psql", "-h", "127.0.0.1", "-U", "tunner", "-d", "tunner_outbox_test",
            "-v", "ON_ERROR_STOP=1", "-tAc", "SELECT 1"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1", result.Stdout.Trim());
    }

    [Fact]
    public void PlaywrightSkeletonExposesTheOfficialApiContractWithoutLaunchingABrowser()
    {
        Assert.Equal("IPlaywright", PlaywrightSkeleton.ApiContract.Name);
    }
}
