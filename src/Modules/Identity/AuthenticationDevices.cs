namespace Tunner.Identity.Persistence;

/// <summary>Private final-decision witness, analogous to the existing authenticator-owner witnesses.</summary>
internal sealed record DeviceExecutionValidity(DeviceApplication Owner, Guid OperationId, Guid TransactionId,
    DeviceBrowserExecutionValidity? Browser = null)
{
    internal bool CurrentAt(IdentityPersistenceDbContext database, StepUpCaller caller, StepUpOperation operation, DateTimeOffset at)
        => Owner.RemovalCurrentAt(database, caller, operation, OperationId, TransactionId, at) &&
            (Browser is null || Browser.CurrentAt(caller, operation, at));
    public override string ToString() => nameof(DeviceExecutionValidity);
}
