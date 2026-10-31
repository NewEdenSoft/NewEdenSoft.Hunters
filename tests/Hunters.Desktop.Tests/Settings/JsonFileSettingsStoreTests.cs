using Microsoft.Extensions.Logging.Abstractions;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Settings;

namespace NewEdenSoft.Hunters.Desktop.Tests.Settings;

public sealed class JsonFileSettingsStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hunters-settings-").FullName;

    private string SettingsPath => Path.Combine(_dir, "nested", "desktop-settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private JsonFileSettingsStore NewStore() => new(SettingsPath, NullLogger<JsonFileSettingsStore>.Instance);

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var settings = NewStore().Load();

        Assert.Null(settings.ThemeId);
        Assert.Empty(settings.Profiles);
    }

    [Fact]
    public void Updates_persist_across_instances()
    {
        NewStore().Update(s => s with
        {
            ThemeId = "amarr",
            StartInTray = true,
            Profiles = new Dictionary<string, ProfileSettings>
            {
                ["a"] = new() { Widgets = new Dictionary<string, WidgetSettings> { ["fleet"] = new() { IsFloating = true, X = 10, Y = 20, Opacity = 0.5 } } },
            },
        });

        var reloaded = NewStore().Load();

        Assert.Equal("amarr", reloaded.ThemeId);
        Assert.True(reloaded.StartInTray);
        Assert.Equal(new WidgetSettings { IsFloating = true, X = 10, Y = 20, Opacity = 0.5 }, reloaded.Profiles["a"].Widgets["fleet"]);
    }

    [Fact]
    public void Map_layout_and_filter_default_to_the_2d_map_and_security_and_are_saved_by_name()
    {
        Assert.Equal(MapLayout.Flat, new MapSettings().Layout);
        Assert.Equal(MapFilterId.Security, new MapSettings().Filter);

        NewStore().Update(s => s with { Profiles = new Dictionary<string, ProfileSettings> { ["a"] = new() { Map = new MapSettings { Layout = MapLayout.TopDown, Filter = MapFilterId.NpcRising } } } });

        var json = File.ReadAllText(SettingsPath);
        Assert.Contains("\"TopDown\"", json);
        Assert.Contains("\"NpcRising\"", json);
        Assert.Equal(MapLayout.TopDown, NewStore().Load().Profiles["a"].Map.Layout);
        Assert.Equal(MapFilterId.NpcRising, NewStore().Load().Profiles["a"].Map.Filter);
    }

    [Fact]
    public void Settings_from_before_profiles_are_left_out()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """
            { "ThemeId": "amarr", "Widgets": { "map": { "IsFloating": true } }, "Map": { "Layout": "TopDown" }, "Characters": { "1": { "IsTracked": false } } }
            """);

        var settings = NewStore().Load();

        Assert.Equal("amarr", settings.ThemeId);
        Assert.Empty(settings.Profiles);

        NewStore().Update(s => s with { StartInTray = true });
        Assert.DoesNotContain("\"Widgets\"", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ not json");

        Assert.Null(NewStore().Load().ThemeId);
    }
}
