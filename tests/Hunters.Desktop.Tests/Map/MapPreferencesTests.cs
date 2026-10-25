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

    [Fact]
    public void Jump_range_is_off_at_8_ly_by_default_kept_within_its_limits_and_saved()
    {
        var preferences = new MapPreferences(NewStore());
        Assert.Equal(new JumpRangeSetting(Enabled: false, Ly: 8), preferences.JumpRange);

        preferences.JumpRange = new JumpRangeSetting(Enabled: true, Ly: 25);
        Assert.Equal(20, preferences.JumpRange.Ly);
        preferences.JumpRange = preferences.JumpRange with { Ly = 0.1 };
        Assert.Equal(0.5, preferences.JumpRange.Ly);

        Assert.Equal(new JumpRangeSetting(Enabled: true, Ly: 0.5), new MapPreferences(NewStore()).JumpRange);
    }

    [Fact]
    public void Nearby_setting_is_saved_with_gate_jumps_off()
    {
        var preferences = new MapPreferences(NewStore());
        Assert.Equal(new NearbySetting(Jumps: 5, ByJumpRange: false, Sort: "activity", ShowQuiet: false), preferences.Nearby);

        preferences.Nearby = new NearbySetting(Jumps: null, ByJumpRange: true, Sort: "carriers");

        Assert.Equal(new NearbySetting(Jumps: null, ByJumpRange: true, Sort: "carriers"), new MapPreferences(NewStore()).Nearby);
    }

    [Fact]
    public void A_saved_jump_range_out_of_limits_falls_back_to_8_ly()
    {
        NewStore().Update(s => s with { Map = s.Map with { JumpRange = new JumpRangeSetting(Enabled: true, Ly: 50) } });

        Assert.Equal(new JumpRangeSetting(Enabled: true, Ly: 8), new MapPreferences(NewStore()).JumpRange);
    }
}
