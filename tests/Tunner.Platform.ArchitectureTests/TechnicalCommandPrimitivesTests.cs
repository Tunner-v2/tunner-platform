using Tunner.Platform.Operations;
using Xunit;

namespace Tunner.Platform.ArchitectureTests;

public sealed class TechnicalCommandPrimitivesTests
{
    [Fact]
    public async Task AuthorizationDoesNotAcceptActorFromRequestAndFailsClosedWithoutServerActor()
    {
        var gate = new TechnicalAuthorizationGate(new ActorAccessor(null), new EvaluatorResolver(null));
        var result = await gate.AuthorizeAsync(new TechnicalAuthorizationRequest("owner-capability", "resource", new Dictionary<string, string>()));
        Assert.False(result.IsAuthorized);
        Assert.Equal(TechnicalProblemCodes.AuthenticationRequired, result.ProblemCode);
    }

    [Fact]
    public async Task MissingEvaluatorFailsClosedWithCanonicalForbiddenScope()
    {
        var gate = new TechnicalAuthorizationGate(new ActorAccessor(new ServerActorContext("server-principal")), new EvaluatorResolver(null));
        var result = await gate.AuthorizeAsync(new TechnicalAuthorizationRequest("owner-capability", null, new Dictionary<string, string>()));
        Assert.False(result.IsAuthorized);
        Assert.Equal(TechnicalProblemCodes.ForbiddenScope, result.ProblemCode);
    }

    [Fact]
    public async Task IdempotencyReusesProtectedOperationOnlyWithinOwnerProvidedOpaqueScope()
    {
        var executor = new InMemoryIdempotencyExecutor();
        var calls = 0;
        var first = await executor.ExecuteAsync(new IdempotencyScope("owner-a"), "duplicate-key", _ => Task.FromResult(++calls));
        var duplicate = await executor.ExecuteAsync(new IdempotencyScope("owner-a"), "duplicate-key", _ => Task.FromResult(++calls));
        var otherOwner = await executor.ExecuteAsync(new IdempotencyScope("owner-b"), "duplicate-key", _ => Task.FromResult(++calls));
        Assert.Equal(1, first);
        Assert.Equal(1, duplicate);
        Assert.Equal(2, otherOwner);
    }

    [Fact]
    public void ExpectedVersionIsDeterministicAndOwnerControlled()
    {
        var conflict = ExpectedVersionGuard.Check(true, 4, 5);
        var current = ExpectedVersionGuard.Check(true, 5, 5);
        var notRequired = ExpectedVersionGuard.Check(false, 4, 5);
        Assert.False(conflict.IsCurrent);
        Assert.Equal(TechnicalProblemCodes.VersionConflict, conflict.ProblemCode);
        Assert.True(current.IsCurrent);
        Assert.True(notRequired.IsCurrent);
        Assert.Null(notRequired.CurrentVersion);
    }

    private sealed class ActorAccessor(ServerActorContext? actor) : IServerActorContextAccessor
    {
        public ServerActorContext? GetCurrent() => actor;
    }

    private sealed class EvaluatorResolver(ITechnicalAuthorizationEvaluator? evaluator) : ITechnicalAuthorizationEvaluatorResolver
    {
        public ITechnicalAuthorizationEvaluator? Resolve(TechnicalAuthorizationRequest request) => evaluator;
    }
}