using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Tunner.Testing;

public static class TestcontainersBaseline
{
    public static IContainer CreatePostgresContainer()
        => new ContainerBuilder("postgres:18-alpine")
            .WithPortBinding(5432, true)
            .Build();
}