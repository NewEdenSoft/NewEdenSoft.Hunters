using NewEdenKit.ESI.Services;

namespace NewEdenSoft.Hunters.Server.Infrastructure.Eve;

public sealed record CharacterAffiliation(long CharacterId, long CorporationId, long? AllianceId);

/// <summary>Corporation and alliance of characters, from ESI's public affiliation endpoint.</summary>
public interface IEveAffiliationService
{
    /// <summary>Affiliations of the given characters. Unknown characters are omitted.</summary>
    Task<IReadOnlyDictionary<long, CharacterAffiliation>> GetAsync(IReadOnlyCollection<long> characterIds, CancellationToken cancellationToken);
}

public sealed class EsiAffiliationService(ICharacter characters) : IEveAffiliationService
{
    private const int BatchSize = 1000;

    public async Task<IReadOnlyDictionary<long, CharacterAffiliation>> GetAsync(IReadOnlyCollection<long> characterIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, CharacterAffiliation>();
        foreach (var batch in characterIds.Distinct().Chunk(BatchSize))
        {
            var response = await characters.PostCharactersAffiliation(batch).ExecuteAsync(cancellationToken);
            if (!response.IsSuccess)
                throw new HttpRequestException($"ESI affiliation lookup failed ({response.StatusCode}).");

            foreach (var item in response.Value)
                result[item.CharacterId] = new CharacterAffiliation(item.CharacterId, item.CorporationId, item.AllianceId);
        }

        return result;
    }
}
