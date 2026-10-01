using Tunner.Testing;
using Xunit;

namespace Tunner.TestHarness.Tests;

public sealed class TestHarnessBaselineTests
{
    [Fact]
    public void ContainerExecutionIsDisabledWithoutExplicitOptIn()
    {
        Assert.False(TestHarnessProfiles.IsContainerExecutionEnabled());
    }

    [Fact]
    public void BrowserExecutionIsDisabledWithoutExplicitOptIn()
    {
        Assert.False(TestHarnessProfiles.IsBrowserExecutionEnabled());
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