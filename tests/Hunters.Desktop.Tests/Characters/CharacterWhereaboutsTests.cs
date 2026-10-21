using NewEdenSoft.Hunters.Client.Tracking;
using NewEdenSoft.Hunters.Desktop.Characters;

namespace NewEdenSoft.Hunters.Desktop.Tests.Characters;

public sealed class CharacterWhereaboutsTests : IAsyncLifetime
{
    private const int Jita = 30000142, Ishtar = 12005;
    private const long JitaStation = 60003760, Structure = 1035466617946;

    private readonly EveNames _names = new(new Names());

    public Task InitializeAsync() => _names.ResolveAsync([Jita, JitaStation, Ishtar]);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void Nothing_to_show_until_the_location_is_known()
    {
        Assert.Null(CharacterWhereabouts.From(null, _names));
        Assert.Null(CharacterWhereabouts.From(new TrackedCharacter(1, TrackingStatus.Starting), _names));
    }

    [Theory]
    [InlineData(null, null, "Ishtar", null)]
    [InlineData(JitaStation, null, "Ishtar", "Docked at Jita IV - Moon 4 - Caldari Navy Assembly Plant")]
    [InlineData(null, Structure, "Ishtar", "Docked")]
    public void Docked_shows_the_station_or_just_docked_in_a_structure(long? station, long? structure, string ship, string? docked)
    {
        var where = CharacterWhereabouts.From(State(station, structure, new CharacterShip(Ishtar, "Ishtar", 1)), _names)!;

        Assert.Equal("Jita", where.System);
        Assert.Equal(docked, where.Docked);
        Assert.Equal(ship, where.Ship);
    }

    [Theory]
    [InlineData(Ishtar, "Hunter One", "Ishtar “Hunter One”")]
    [InlineData(Ishtar, "Ishtar", "Ishtar")]
    [InlineData(Ishtar, " ", "Ishtar")]
    [InlineData(999, "Hunter One", "Hunter One")]
    public void Ships_show_their_type_and_their_own_name(int typeId, string name, string expected)
    {
        Assert.Equal(expected, CharacterWhereabouts.From(State(null, null, new CharacterShip(typeId, name, 1)), _names)!.Ship);
    }

    [Fact]
    public async Task Structures_are_not_looked_up()
    {
        var names = new Names();
        var cache = new EveNames(names);

        await CharacterWhereabouts.ResolveNamesAsync(State(null, Structure, new CharacterShip(Ishtar, "Ishtar", 1)), cache);

        Assert.Equal([Ishtar, Jita], names.Asked.Order());
    }

    private static TrackedCharacter State(long? station, long? structure, CharacterShip ship) =>
        new(1, TrackingStatus.Tracking, true, new CharacterLocation(Jita, station, structure), ship);

    private sealed class Names : IEveNameSource
    {
        private static readonly Dictionary<long, string> All = new()
        {
            [Jita] = "Jita",
            [JitaStation] = "Jita IV - Moon 4 - Caldari Navy Assembly Plant",
            [Ishtar] = "Ishtar",
        };

        public List<long> Asked { get; } = [];

        public Task<IReadOnlyDictionary<long, string>> GetNamesAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken)
        {
            Asked.AddRange(ids);
            return Task.FromResult<IReadOnlyDictionary<long, string>>(ids.Where(All.ContainsKey).ToDictionary(id => id, id => All[id]));
        }
    }
}
