namespace Tunner.Identity.Persistence;

/// <summary>Preserve existing owners and reject conflicting selected originals rather than choosing one.</summary>
internal sealed class ProductAuthorizationStepUpOwnerRouter(IUserStepUpOperationSource fallback, ProductAuthorizationStepUp product)
    : IUserStepUpOperationOutcomeSource, IUserStepUpBrowserCompletionSource
{
    internal static IUserStepUpOperationSource Wrap(IUserStepUpOperationSource fallback, ProductAuthorizationStepUp? product) =>
        product is null ? fallback : new ProductAuthorizationStepUpOwnerRouter(fallback, product);

    public async Task<UserStepUpOperationContext?> ResolveAsync(UserStepUpActor actor, CancellationToken ct)
    {
        var original = await fallback.ResolveAsync(actor, ct).ConfigureAwait(false);
        var candidate = await product.ResolveAsync(actor, ct).ConfigureAwait(false);
        return original is not null && candidate is not null ? null : candidate ?? original;
    }
    public async Task<bool> ApplyAsync(UserStepUpActor actor, UserStepUpOperationContext operation,
        IdentityPersistenceDbContext database, CancellationToken ct)
    {
        using var outcome = await ApplyWithOutcomeAsync(actor, operation, database, ct).ConfigureAwait(false);
        return outcome.Applied;
    }
    public async Task<UserStepUpOperationApplyOutcome> ApplyWithOutcomeAsync(UserStepUpActor actor, UserStepUpOperationContext operation,
        IdentityPersistenceDbContext database, CancellationToken ct)
    {
        if (operation.Binding.Scope.Operation == ProductAuthorizationApplication.AuthorizeOperation)
            throw new InvalidOperationException("Product proof consumption belongs to the native authorization transaction.");
        if (fallback is IUserStepUpOperationOutcomeSource typed)
            return await typed.ApplyWithOutcomeAsync(actor, operation, database, ct).ConfigureAwait(false);
        return await fallback.ApplyAsync(actor, operation, database, ct).ConfigureAwait(false)
            ? UserStepUpOperationApplyOutcome.SessionContinues() : UserStepUpOperationApplyOutcome.Conflict();
    }
    public Task CompleteBrowserAsync(UserStepUpOperationContext operation, bool signInRequired, CancellationToken ct) =>
        operation.Binding.Scope.Operation != ProductAuthorizationApplication.AuthorizeOperation && fallback is IUserStepUpBrowserCompletionSource hook
            ? hook.CompleteBrowserAsync(operation, signInRequired, ct) : Task.CompletedTask;
}
