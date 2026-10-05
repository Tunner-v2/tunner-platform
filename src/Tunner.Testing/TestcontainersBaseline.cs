using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Tunner.Testing;

public static class TestcontainersBaseline
{
    public static IContainer CreateRedisContainer()
        => new ContainerBuilder("redis:8.10.2-alpine")
            .WithPortBinding(6379, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379))
            .Build();
    public static IContainer CreatePostgresContainer()
        => new ContainerBuilder("postgres:18-alpine")
            .WithEnvironment("POSTGRES_DB", "tunner_outbox_test")
            .WithEnvironment("POSTGRES_USER", "tunner")
            .WithEnvironment("POSTGRES_HOST_AUTH_METHOD", "trust")
            .WithPortBinding(5432, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
            .Build();
}