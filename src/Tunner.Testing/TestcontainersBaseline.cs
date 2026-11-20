using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Tunner.Testing;

public static class TestcontainersBaseline
{
    public static IContainer CreateRabbitMqContainer(string user, string password)
        => new ContainerBuilder("rabbitmq:4.3.7-management-alpine@sha256:eb338b3fc8f5c251028c0022ac0de3fe1db2d599d1c6a08b45898f756f248881")
            .WithEnvironment("RABBITMQ_DEFAULT_USER", user)
            .WithEnvironment("RABBITMQ_DEFAULT_PASS", password)
            .WithPortBinding(5672, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672))
            .Build();

    public static IContainer CreateRedisContainer()
        => new ContainerBuilder("redis:8.10.3-alpine@sha256:71885d8cc6e42f2bc0aa2eb853bfc4be200b5108a46ac50e117ae1df0531fb80")
            .WithPortBinding(6379, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379))
            .Build();
    public static IContainer CreatePostgresContainer()
        => new ContainerBuilder("postgres:18-alpine")
            .WithEnvironment("POSTGRES_DB", "tunner_outbox_test")
            .WithEnvironment("POSTGRES_USER", "tunner")
            .WithEnvironment("POSTGRES_HOST_AUTH_METHOD", "trust")
            .WithPortBinding(5432, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilInternalTcpPortIsAvailable(5432)
                .UntilCommandIsCompleted("pg_isready", "-h", "127.0.0.1", "-p", "5432", "-U", "tunner", "-d", "tunner_outbox_test"))
            .Build();
}
