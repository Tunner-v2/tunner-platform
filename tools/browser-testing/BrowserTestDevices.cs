using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Identity.Persistence;
using Tunner.Platform.Policies;

/// <summary>Explicit synthetic persistent-local qualification; never registered in production or a finite run.</summary>
internal sealed class BrowserTestDevices(string origin) : IDeviceEvidencePolicySource, IDeviceSafeCharacteristicSource,
    IDeviceTrustEligibilitySource, IDeviceHttpBudgetSource, IDevicePresentationSource
{
    internal const string AssuranceVersion = "browser-test-device-trust-revoke-v1";
    private const string Version = "SYNTHETIC_LOCAL_DEVICE_EVIDENCE_V1";
    private const string EligibilityVersion = "SYNTHETIC_LOCAL_DEVICE_ELIGIBILITY_V1";
    private const string Provenance = "SYNTHETIC-ISOLATED-LOCAL-BROWSER-TESTING-ONLY";
    private readonly EffectivePolicyInterval interval = new(DateTimeOffset.UtcNow.AddDays(-1), true, DateTimeOffset.UtcNow.AddDays(1), false);
    private static readonly string[] Families = ["Chromium", "Firefox", "WebKit", "Unknown"];
    private PolicyVersionCandidate<string> Metadata(string version, string scope) => new(version, "ACTIVE", scope, interval, null, [Provenance]);
    internal static void Configure(IServiceCollection services, string origin)
    {
        var profile = new BrowserTestDevices(origin);
        services.AddSingleton(profile);
        services.AddSingleton<IDeviceEvidencePolicySource>(profile); services.AddSingleton<IDeviceSafeCharacteristicSource>(profile);
        services.AddSingleton<IDeviceTrustEligibilitySource>(profile); services.AddSingleton<IDeviceHttpBudgetSource>(profile);
        services.AddSingleton<IDevicePresentationSource>(profile); services.AddTunnerDevices();
    }
    internal static AssurancePolicyVersion AssurancePolicy()
    {
        var existing = BrowserTestPasskeys.AssurancePolicies().First();
        return new(new(AssuranceVersion, "ACTIVE", new("DEVICE_TRUST_REVOKE", "USER_BFF"), existing.Metadata.EffectiveInterval, null, [Provenance]),
            existing.RequiredAssurance, existing.ProofLifetime, existing.MethodAssurances);
    }
    public DeviceEvidencePolicy RequireCurrent(string requestedOrigin, DateTimeOffset at)
    {
        if (requestedOrigin != origin || !interval.Contains(at)) throw new InvalidOperationException("Synthetic local Device profile is unavailable.");
        return new(Metadata(Version, origin), origin, "SYNTHETIC-OBSERVED-BROWSER-FAMILY-COLLECTION",
            "SYNTHETIC-LOCAL-DEVICE-INVENTORY", "SYNTHETIC-LOCAL-DEVICE-PROCESSING",
            "SYNTHETIC-LOCAL-DEVICE-RETAINED-IN-ISOLATED-TEST-DATABASE-NO-PRODUCTION-RETENTION-POLICY", "RESTRICTED_SECURITY",
            "SYNTHETIC-LOCAL-DEVICE-USER-PERMISSION", EligibilityVersion, AssuranceVersion,
            "SYNTHETIC-EXPLICIT-TRUST-REMOVAL-PRESERVES-EXISTING-SESSIONS", TimeSpan.FromDays(1), TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(10), 20, 4, new Dictionary<string, IReadOnlyList<string>> { ["BrowserFamily"] = Families });
    }
    public IReadOnlyDictionary<string, string> Observe(HttpContext context, DeviceEvidencePolicy policy)
    {
        // Request headers are untrusted observations. Persist only this explicitly qualified coarse vocabulary.
        var agent = context.Request.Headers.UserAgent.ToString();
        var family = agent.Contains("Firefox/", StringComparison.Ordinal) ? "Firefox" :
            agent.Contains("Chrome/", StringComparison.Ordinal) || agent.Contains("Chromium/", StringComparison.Ordinal) ? "Chromium" :
            agent.Contains("Safari/", StringComparison.Ordinal) ? "WebKit" : "Unknown";
        return new Dictionary<string, string> { ["BrowserFamily"] = family };
    }
    public DeviceTrustEligibilityDecision RequireCurrent(DeviceTrustEligibilityFacts facts, DateTimeOffset at)
    {
        var knownCategory = Families.Any(family => facts.CharacteristicsDigest == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new Dictionary<string, string> { ["BrowserFamily"] = family })))));
        var authenticated = facts.AuthenticationMethods == "pwd" && facts.Assurance == "test-only-password-assurance" ||
            facts.AuthenticationMethods == "passkey" && facts.Assurance == "test-only-passkey-assurance";
        var allowed = facts.Origin == origin && facts.PolicyVersion == Version && facts.AccountVersion > 0 && facts.DeviceVersion > 0 &&
            facts.AuthenticationSessionId != Guid.Empty && facts.AuthenticatedAt <= at && at - facts.AuthenticatedAt < TimeSpan.FromMinutes(30) &&
            facts.PriorTrustState != "REVOKED" && knownCategory && authenticated;
        return new(facts.Digest, allowed, EligibilityVersion, "SYNTHETIC-EXACT-AUTHENTICATED-ACCOUNT-DEVICE-FACTS-NOT-HARDWARE-TRUST", interval);
    }
    public DeviceHttpBudget RequireCurrent(DateTimeOffset at) => new(Metadata("SYNTHETIC_LOCAL_DEVICE_HTTP_V1", DeviceHttpBudget.Scope),
        20, 8192, 4096, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10));
    public DevicePresentation RequirePublished(AuthenticationPolicy authenticationPolicy, string devicePolicyVersion)
    {
        if (authenticationPolicy.Version != "browser-test-auth-v1" || devicePolicyVersion != Version)
            throw new InvalidOperationException("Synthetic persistent local Devices publication only.");
        return new("en", DevicePresentation.RequiredKeys.Select(key => new RegistrationPublishedText(key, "synthetic-local-device",
            "synthetic-local-device-v1", Provenance, "en", "PUBLISHED", PublishedText[key])));
    }
    private static readonly Dictionary<string, string> PublishedText = Text();
    private static Dictionary<string, string> Text()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["title"] = "Devices", ["description"] = "Review browser installations recognized by Tunner and manage their saved trust.",
            ["loading"] = "Loading devices…", ["empty"] = "No devices loaded", ["unavailable"] = "Devices could not be loaded. Try again.",
            ["changed"] = "This device information has changed. Refresh to review it.", ["unknown"] = "Trust removal is not confirmed",
            ["check"] = "Check this request", ["review.title"] = "Remove trusted device?", ["review.description"] = "Remove the saved trust for this browser installation?",
            ["confirm"] = "Remove trust", ["cancel"] = "Keep trusted", ["completed"] = "Trust removed", ["trust.trusted"] = "Trusted",
            ["trust.untrusted"] = "Not trusted", ["trust.revoked"] = "Trust removed", ["sessionUnchanged"] = "Removing trust does not sign it out. Existing sessions stay signed in.",
            ["observedInstallation"] = "Each entry represents an observed browser installation. It does not verify a physical device or its hardware.",
            ["eyebrow"] = "SESSIONS & DEVICES", ["details"] = "Device details", ["details.description"] = "Review the observed information and saved trust for this entry.",
            ["label"] = "Device …{reference}", ["collection"] = "Recognized devices", ["collection.description"] = "Entries loaded so far. A browser installation is not a verified physical device.",
            ["about"] = "About recognized devices", ["trust.heading"] = "Device trust", ["trust.description"] = "This is saved trust for an observed browser installation.",
            ["information"] = "Device information", ["currentObservation"] = "Current observation", ["trust.separate"] = "Trust and sign-in are separate",
            ["trust.notAuthentication"] = "Trust does not sign you in or replace identity verification.", ["current"] = "Observed with your current session",
            ["current.heading"] = "OBSERVED WITH YOUR CURRENT SESSION", ["firstObserved"] = "First observed", ["lastObserved"] = "Last observed", ["reference"] = "Device reference",
            ["browserFamily"] = "Browser family", ["browserUnavailable"] = "Browser family unavailable", ["recordedTrust"] = "Trust when saved", ["effectiveTrust"] = "Trust at last check",
            ["checkedAt"] = "Checked {time}", ["refresh"] = "Refresh devices", ["loadMore"] = "Load more", ["allLoaded"] = "No more devices in this response.",
            ["moreAvailable"] = "More devices are available.", ["viewDetails"] = "View details", ["back"] = "Back to devices", ["open"] = "Review devices",
            ["sessionReview"] = "Review Sessions separately to manage signed-in access.", ["review.verification"] = "You may be asked to verify your identity.",
            ["preparing"] = "Preparing trust removal…", ["checking"] = "Checking this request…", ["result.description"] = "Trust was removed for this original request. Existing sessions were kept signed in.",
            ["unknown.description"] = "We could not confirm the result of this request. Check its status before starting another removal. Checking does not send a new removal request.",
            ["changedOriginal.description"] = "This original request has changed. Trust removal is not confirmed. Check this request before starting another removal.",
            ["close"] = "Back to devices", ["signOut"] = "Sign out", ["signOutPending"] = "Signing out…",
            ["signOutUnknown"] = "We could not confirm whether you signed out. Check your sign-in status before trying again.", ["checkSignIn"] = "Check sign-in status",
            ["header.accountCenter"] = "ACCOUNT CENTER", ["header.search"] = "Search account settings", ["header.notifications"] = "Notifications", ["header.account"] = "Account owner",
            ["header.menu"] = "Account menu", ["navigation.account"] = "ACCOUNT", ["navigation.overview"] = "Overview", ["navigation.profile"] = "Profile & identity", ["navigation.security"] = "Security",
            ["navigation.sessions"] = "Sessions & devices", ["navigation.billing"] = "Billing & payments", ["navigation.notifications"] = "Notifications", ["navigation.products"] = "Products",
            ["navigation.activity"] = "Account activity", ["navigation.help"] = "NEED HELP?", ["navigation.helpDescription"] = "Account, billing, or security issue?", ["navigation.contactSupport"] = "Contact support"
        };
        var result = values.ToDictionary(x => "account.devices." + x.Key, x => x.Value, StringComparer.Ordinal); result.Add("brand.tunner", "Tunner"); return result;
    }
}
