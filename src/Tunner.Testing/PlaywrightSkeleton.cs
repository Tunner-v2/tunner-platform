using Microsoft.Playwright;

namespace Tunner.Testing;

public static class PlaywrightSkeleton
{
    public static Type ApiContract => typeof(IPlaywright);
    public const string BrowserInstallInstruction = "Build the browser test project, then run its generated playwright.ps1 install command as the local operator.";
}