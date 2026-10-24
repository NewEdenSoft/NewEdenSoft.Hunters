using System.Net;
using Microsoft.EntityFrameworkCore;
using NewEdenKit.ESI.Services;
using NewEdenSoft.Hunters.Intel.Data;

namespace NewEdenSoft.Hunters.Intel.Kills;

/// <summary>A character's corporation and alliance (0 for none).</summary>
public readonly record struct Affiliation(long CorporationId, long AllianceId);

/// <summary>Who characters fly for now.</summary>
public interface IAffiliationSource
{
    /// <summary>
    /// The affiliations of up to <see cref="EsiAffiliationSource.MaxPerCall"/> characters at a time; a
    /// character ESI doesn't know is left out. Failures throw <see cref="HttpRequestException"/>.
    /// </summary>
    Task<IReadOnlyDictionary<long, Affiliation>> GetAsync(IReadOnlyList<long> characterIds, CancellationToken cancellationToken);
}

/// <summary>ESI's <c>POST /characters/affiliation/</c>, through NewEdenKit.ESI.</summary>
public sealed class EsiAffiliationSource(ICharacter characters) : IAffiliationSource
{
    /// <summary>ESI takes at most this many characters per call.</summary>
    public const int MaxPerCall = 1000;

    public async Task<IReadOnlyDictionary<long, Affiliation>> GetAsync(IReadOnlyList<long> characterIds, CancellationToken cancellationToken)
    {
        var found = new Dictionary<long, Affiliation>();
        await AddAsync(characterIds, found, cancellationToken);
        return found;
    }

    /// <summary>
    /// ESI refuses the whole call if one id is not a character it knows; then each half is asked on its
    /// own, down to the one id, which is left out.
    /// </summary>
    private async Task AddAsync(IReadOnlyList<long> ids, Dictionary<long, Affiliation> found, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return;

        var result = await characters.PostCharactersAffiliation(ids).ExecuteAsync(cancellationToken);
        if (result.IsSuccess)
        {
            foreach (var a in result.Value!)
                found[a.CharacterId] = new Affiliation(a.CorporationId, a.AllianceId ?? 0);
            return;
        }

        if (result.StatusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.NotFound))
            throw new HttpRequestException($"ESI answered {(int?)result.StatusCode}: {result.Error?.Message}");
        if (ids.Count == 1)
            return;

        var half = ids.Count / 2;
        await AddAsync([.. ids.Take(half)], found, cancellationToken);
        await AddAsync([.. ids.Skip(half)], found, cancellationToken);
    }
}

/// <summary>
/// Fills in the attackers' corporations and alliances of kills stored before they were kept, from who
/// the pilots fly for now (close enough for recent kills). New kills have them from the killmail.
/// </summary>
public static class KillAffiliations
{
    /// <summary>Kills read at once.</summary>
    private const int BatchSize = 2000;

    /// <summary>Fills every kill with attackers but no attacker corporations; returns how many were filled.</summary>
    public static async Task<int> FillAsync(IntelDbContext db, IAffiliationSource source, CancellationToken cancellationToken)
    {
        var filled = 0;
        var after = long.MinValue;
        while (true)
        {
            var batch = await db.Kills
                .Where(k => k.KillmailId > after && k.AttackerCount > 0 && k.AttackerCorporationIds.Length == 0)
                .OrderBy(k => k.KillmailId)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
                return filled;

            var pilots = batch.SelectMany(k => k.AttackerCharacterIds).Where(id => id != 0).Distinct().ToList();
            var affiliations = new Dictionary<long, Affiliation>();
            foreach (var chunk in pilots.Chunk(EsiAffiliationSource.MaxPerCall))
            {
                foreach (var (id, affiliation) in await source.GetAsync(chunk, cancellationToken))
                    affiliations[id] = affiliation;
            }

            // Unknown pilots (and NPCs) get 0, so the kill is not asked about again.
            foreach (var kill in batch)
            {
                var known = kill.AttackerCharacterIds.Select(id => affiliations.GetValueOrDefault(id)).ToArray();
                kill.AttackerCorporationIds = [.. known.Select(a => a.CorporationId)];
                kill.AttackerAllianceIds = [.. known.Select(a => a.AllianceId)];
            }

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            filled += batch.Count;
            after = batch[^1].KillmailId;
        }
    }
}
