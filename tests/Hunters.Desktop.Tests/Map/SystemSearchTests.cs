using NewEdenSoft.Hunters.Contracts.Map;
using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class SystemSearchTests
{
    private static readonly MapModel Model = MapModel.Build(new MapPack(
        1,
        [new MapRegion(1, "Region")],
        [System(1, "Jita"), System(2, "Jitanen"), System(3, "Ajita"), System(4, "1DQ1-A"), System(5, "Amarr"), System(6, "Aakari")],
        []));

    [Fact]
    public void Ranks_the_exact_name_then_prefixes_then_the_rest() =>
        Assert.Equal(["Jita", "Jitanen", "Ajita"], Names(SystemSearch.Find(Model, "JITA")));

    [Fact]
    public void Ignores_case_and_punctuation() =>
        Assert.Equal(["1DQ1-A"], Names(SystemSearch.Find(Model, "1dq1a")));

    [Fact]
    public void Sorts_by_name_within_a_rank() =>
        Assert.Equal(["Aakari", "Ajita", "Amarr"], Names(SystemSearch.Find(Model, "a")).Take(3));

    [Fact]
    public void Keeps_to_the_limit() =>
        Assert.Equal(2, SystemSearch.Find(Model, "a", limit: 2).Count);

    [Theory]
    [InlineData("")]
    [InlineData(" -")]
    public void An_empty_query_finds_nothing(string query) =>
        Assert.Empty(SystemSearch.Find(Model, query));

    private static IEnumerable<string> Names(IEnumerable<int> indexes) => indexes.Select(i => Model.Names[i]);

    private static MapSystem System(int id, string name) => new(id, name, 1, 1, 0.5, new MapPoint(id, 0), new MapPoint(id, 0));
}
