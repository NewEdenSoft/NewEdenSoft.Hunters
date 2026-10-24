using NewEdenSoft.Hunters.Intel.Kills;

namespace NewEdenSoft.Hunters.Intel.Tests;

/// <summary>ESI's character affiliations, set by the test; records each call.</summary>
public sealed class FakeAffiliations : IAffiliationSource
{
    public Dictionary<long, Affiliation> Known { get; } = [];

    public List<IReadOnlyList<long>> Calls { get; } = [];

    public Task<IReadOnlyDictionary<long, Affiliation>> GetAsync(IReadOnlyList<long> characterIds, CancellationToken cancellationToken)
    {
        Calls.Add(characterIds);
        IReadOnlyDictionary<long, Affiliation> found = characterIds.Where(Known.ContainsKey).ToDictionary(id => id, id => Known[id]);
        return Task.FromResult(found);
    }
}
