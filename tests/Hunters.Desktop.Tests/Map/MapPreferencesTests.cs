using Microsoft.Extensions.Logging.Abstractions;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Settings;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class MapPreferencesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hunters-map-prefs-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private JsonFileSettingsStore NewStore() => new(Path.Combine(_dir, "desktop-settings.json"), NullLogger<JsonFileSettingsStore>.Instance);

    [Fact]
    public void Map_behaviour_defaults_like_the_web_and_is_saved()
    {
        var preferences = new MapPreferences(NewStore());
        Assert.Equal(new MapBehaviour(EnterKnownSpace: true, LongMove: true, MinJumps: 5, DoubleClickFocus: true), preferences.Behaviour);

        preferences.Behaviour = preferences.Behaviour with { DoubleClickFocus = false, MinJumps = 10 };

        Assert.Equal(new MapBehaviour(DoubleClickFocus: false, MinJumps: 10), new MapPreferences(NewStore()).Behaviour);
    }

    [Fact]
    public void A_minimum_jumps_not_offered_falls_back_to_the_default()
    {
        NewStore().Update(s => s with { Map = s.Map with { Behaviour = new MapBehaviour(LongMove: false, MinJumps: 7) } });

        Assert.Equal(new MapBehaviour(LongMove: false, MinJumps: 5), new MapPreferences(NewStore()).Behaviour);
    }
}
