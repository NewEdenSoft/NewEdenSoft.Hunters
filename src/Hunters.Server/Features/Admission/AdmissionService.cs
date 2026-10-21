using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Server.Features.Auth;
using NewEdenSoft.Hunters.Server.Configuration;
using NewEdenSoft.Hunters.Server.Infrastructure.Persistence;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;

namespace NewEdenSoft.Hunters.Server.Features.Admission;

public sealed record AdmissionResult(bool Allowed, string? Reason)
{
    public static readonly AdmissionResult Allow = new(true, null);

    public static AdmissionResult Deny(string reason) => new(false, reason);
}

/// <summary>
/// The server's optional admission policy. Access keys gate account creation; the other rules
/// (affiliation, character allow list) are combined by <see cref="AdmissionOptions.Mode"/>.
/// The deny list always wins.
/// </summary>
public sealed partial class AdmissionService(
    HuntersDbContext db,
    IEveAffiliationService affiliations,
    IOptionsSnapshot<HuntersOptions> options,
    TimeProvider time,
    ILogger<AdmissionService> logger)
{
    private AdmissionOptions Options => options.Value.Admission;

    public bool AccessKeyRequiredForNewAccounts => Options.AccessKey.Enabled;

    /// <summary>A usable access key matching <paramref name="key"/>, or null.</summary>
    public async Task<AccessKey?> FindUsableAccessKeyAsync(string? key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var hash = Secrets.Sha256(key.Trim());
        var accessKey = await db.AccessKeys.FirstOrDefaultAsync(k => k.KeyHash == hash, cancellationToken);
        return accessKey?.IsUsable(time.GetUtcNow()) == true ? accessKey : null;
    }

    /// <summary>
    /// The character whose affiliation decides for an account: the main for
    /// <see cref="AffiliationScope.FirstCharacter"/>, otherwise the character logging in.
    /// </summary>
    public long SubjectFor(long? mainCharacterId, long loginCharacterId) =>
        Options.Affiliation.Scope == AffiliationScope.FirstCharacter && mainCharacterId is { } main ? main : loginCharacterId;

    /// <summary>Checks a login. Fails closed when affiliations cannot be looked up.</summary>
    public async Task<AdmissionResult> CheckAsync(long subjectCharacterId, long loginCharacterId, CancellationToken cancellationToken)
    {
        var deny = Options.Characters.Deny;
        if (deny.Contains(loginCharacterId) || deny.Contains(subjectCharacterId))
            return AdmissionResult.Deny("This character is not allowed on this server.");

        IReadOnlyDictionary<long, CharacterAffiliation>? affiliation = null;
        if (Options.Affiliation.Enabled)
        {
            try
            {
                affiliation = await affiliations.GetAsync([subjectCharacterId], cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                LogAffiliationLookupFailed(ex);
                return AdmissionResult.Deny("Your corporation could not be checked right now. Try again in a few minutes.");
            }
        }

        return Evaluate(subjectCharacterId, affiliation);
    }

    /// <summary>
    /// Evaluates the combinable rules for one character. <paramref name="affiliations"/> must
    /// contain the character when affiliation checks are enabled.
    /// </summary>
    public AdmissionResult Evaluate(long subjectCharacterId, IReadOnlyDictionary<long, CharacterAffiliation>? affiliations)
    {
        if (Options.Characters.Deny.Contains(subjectCharacterId))
            return AdmissionResult.Deny("This character is not allowed on this server.");

        var results = new List<bool>();

        if (Options.Affiliation.Enabled)
        {
            var match = affiliations?.GetValueOrDefault(subjectCharacterId) is { } a
                && (Options.Affiliation.CorporationIds.Contains(a.CorporationId)
                    || (a.AllianceId is { } alliance && Options.Affiliation.AllianceIds.Contains(alliance)));
            results.Add(match);
        }

        if (Options.Characters.Allow.Length > 0)
            results.Add(Options.Characters.Allow.Contains(subjectCharacterId));

        var allowed = results.Count == 0
            || (Options.Mode == AdmissionMode.All ? results.All(r => r) : results.Any(r => r));

        return allowed
            ? AdmissionResult.Allow
            : AdmissionResult.Deny("This server is limited to members of specific corporations or alliances.");
    }

    public bool HasCombinableRules => Options.Affiliation.Enabled || Options.Characters.Allow.Length > 0 || Options.Characters.Deny.Length > 0;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Affiliation lookup failed during admission")]
    private partial void LogAffiliationLookupFailed(Exception exception);
}
