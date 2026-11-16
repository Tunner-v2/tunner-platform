using System.Text.Json;
using Tunner.Identity.Persistence;
using Tunner.PresentationContent;

namespace Tunner.Admin.Bff;

public sealed record AdminAuthenticationContentPreview(string Screen, ContentPreview Draft, AuthenticationPresentation Published);
public interface IAdminAuthenticationContentPreviewSource
{
    AdminAuthenticationContentPreview RequirePublishedContext(ContentPreview draft);
}

/// <summary>Explicit read-only composition. A proposed text remains a draft and never replaces a runtime publication.</summary>
public sealed class AdminAuthenticationContentPreviewSource(IAuthenticationPolicySource policies, IAuthenticationContentBindingSource bindings,
    IPresentationContentResolver resolver, TimeProvider clock) : IAdminAuthenticationContentPreviewSource
{
    public AdminAuthenticationContentPreview RequirePublishedContext(ContentPreview draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var now = clock.GetUtcNow(); var policy = policies.RequireEffective(now);
        var binding = bindings.RequireQualified(policy, now);
        RequireBinding(binding, policy, draft, now);
        var bindingSnapshot = JsonSerializer.Serialize(binding); var policySnapshot = JsonSerializer.Serialize(policy);
        var published = new PublishedAuthenticationPresentationSource(resolver, bindings, clock).RequirePublished(policy);
        if (!published.Content.ContainsKey(draft.Proposal.Key)) throw new ContentUnavailableException();
        var finalTime = clock.GetUtcNow(); var finalPolicy = policies.RequireEffective(finalTime);
        var finalBinding = bindings.RequireQualified(finalPolicy, finalTime); RequireBinding(finalBinding, finalPolicy, draft, finalTime);
        if (JsonSerializer.Serialize(finalPolicy) != policySnapshot || JsonSerializer.Serialize(finalBinding) != bindingSnapshot)
            throw new ContentUnavailableException();
        return new(draft.Proposal.Key.StartsWith("signedIn.", StringComparison.Ordinal) ? "SIGNED_IN" : "LOGIN", draft, published);
    }

    private static void RequireBinding(AuthenticationContentBinding binding, AuthenticationPolicy policy, ContentPreview draft, DateTimeOffset at)
    {
        if (binding is null || binding.AuthenticationPolicyVersion != policy.Version || binding.ContentScope != draft.Proposal.Scope ||
            binding.VariantReference != draft.Proposal.VariantReference || binding.EffectiveInterval is null || !binding.EffectiveInterval.Contains(at) ||
            string.IsNullOrWhiteSpace(binding.ConfigurationVersionReference) || string.IsNullOrWhiteSpace(binding.QualificationReference))
            throw new ContentUnavailableException();
    }
}

public static class AdminAuthenticationContentPreviewRegistration
{
    public static IServiceCollection AddTunnerAdminAuthenticationContentPreview(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var required in new[] { typeof(IAuthenticationPolicySource), typeof(IAuthenticationContentBindingSource),
            typeof(IPresentationContentResolver), typeof(TimeProvider) })
            if (!services.Any(x => x.ServiceType == required)) throw new InvalidOperationException("Explicit qualified Authentication preview dependencies are required.");
        if (services.Any(x => x.ServiceType == typeof(IAdminAuthenticationContentPreviewSource)))
            throw new InvalidOperationException("Authentication preview composition must be explicit and unique.");
        services.AddScoped<IAdminAuthenticationContentPreviewSource, AdminAuthenticationContentPreviewSource>();
        return services;
    }

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/content/proposals/context-preview", (HttpContext context, PresentationContentApplication application) =>
            AdminContentEndpoints.Guard(context, async (profile, _) =>
            {
                var command = await AdminContentEndpoints.Read<AdminContentProposalCommand>(context, profile).ConfigureAwait(false);
                var operation = Guid.NewGuid().ToString("N");
                var target = new ContentProposalCommand(command.ProposalReference, command.ExpectedRevision, "OP-CONTENT-" + operation, operation);
                var draft = await application.PreviewAsync(target, context.RequestAborted).ConfigureAwait(false);
                var source = context.RequestServices.GetService<IAdminAuthenticationContentPreviewSource>() ?? throw new ContentUnavailableException();
                var result = source.RequirePublishedContext(draft);
                // Recheck the exact draft revision and current caller after resolving the independently owned context.
                var confirmed = await application.PreviewAsync(target, context.RequestAborted).ConfigureAwait(false);
                if (confirmed.Proposal != draft.Proposal || confirmed.Text != draft.Text || confirmed.ChangeReason != draft.ChangeReason)
                    throw new ContentConflictException();
                return Results.Json(result);
            })).RequireAuthorization(policy => policy.AddAuthenticationSchemes(AdminContentAuthorization.Scheme).RequireAuthenticatedUser());
    }
}
