using Tunner.Testing;
using Xunit;

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
    public void PlaywrightSkeletonExposesTheOfficialApiContractWithoutLaunchingABrowser()
    {
        Assert.Equal("IPlaywright", PlaywrightSkeleton.ApiContract.Name);
    }
}
