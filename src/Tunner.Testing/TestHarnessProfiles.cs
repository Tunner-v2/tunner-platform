namespace Tunner.Testing;

public static class TestHarnessProfiles
{
    public const string ContainerOptInVariable = "TUNNER_RUN_CONTAINER_TESTS";
    public const string BrowserOptInVariable = "TUNNER_RUN_BROWSER_TESTS";

    public static bool IsContainerExecutionEnabled() => IsEnabled(ContainerOptInVariable);
    public static bool IsBrowserExecutionEnabled() => IsEnabled(BrowserOptInVariable);

    private static bool IsEnabled(string variable) => string.Equals(Environment.GetEnvironmentVariable(variable), "1", StringComparison.Ordinal);
}