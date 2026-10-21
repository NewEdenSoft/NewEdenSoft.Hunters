using System.Collections.Concurrent;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;

namespace NewEdenSoft.Hunters.Server.Tests.Fakes;

public sealed class FakeAffiliations : IEveAffiliationService
{
    public ConcurrentDictionary<long, CharacterAffiliation> Known { get; } = new();

    /// <summary>Simulates ESI being down.</summary>
    public bool Unavailable { get; set; }

    public void Set(long characterId, long corporationId, long? allianceId = null) =>
        Known[characterId] = new CharacterAffiliation(characterId, corporationId, allianceId);

    public Task<IReadOnlyDictionary<long, CharacterAffiliation>> GetAsync(IReadOnlyCollection<long> characterIds, CancellationToken cancellationToken)
    {
        if (Unavailable)
            throw new HttpRequestException("ESI unavailable");

        IReadOnlyDictionary<long, CharacterAffiliation> result = characterIds
            .Where(Known.ContainsKey)
            .Distinct()
            .ToDictionary(id => id, id => Known[id]);
        return Task.FromResult(result);
    }
}
