using System.Text.Json;
using Xunit;

namespace Tunner.Platform.ArchitectureTests;

public sealed class OpenApiContractFoundationTests
{
    [Fact]
    public void BuildPublishesAnOpenApi31ContractWithoutProductOperations()
    {
        var repositoryRoot = FindRepositoryRoot();
        var apiIntermediateDirectory = Path.Combine(repositoryRoot.FullName, "src", "Tunner.Api", "obj");
        var contractPath = Directory
            .GetFiles(apiIntermediateDirectory, "tunner-openapi.json", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        Assert.False(string.IsNullOrWhiteSpace(contractPath));

        using var contract = JsonDocument.Parse(File.ReadAllText(contractPath!));
        var openApiVersion = contract.RootElement.GetProperty("openapi").GetString() ?? string.Empty;
        Assert.StartsWith("3.1", openApiVersion);

        Assert.True(contract.RootElement.TryGetProperty("paths", out var paths));
        Assert.All(paths.EnumerateObject(), path => Assert.Equal("/health", path.Name));
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".tunner-root"))) return directory;
        }

        throw new InvalidOperationException("Tunner repository root marker was not found from the test output directory.");
    }
}