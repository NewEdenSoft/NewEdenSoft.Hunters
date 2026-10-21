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
        Assert.Empty(settings.Widgets);
    }

    [Fact]
    public void Updates_persist_across_instances()
    {
        NewStore().Update(s => s with
        {
            ThemeId = "amarr",
            StartInTray = true,
            Widgets = new Dictionary<string, WidgetSettings> { ["fleet"] = new() { IsFloating = true, X = 10, Y = 20, Opacity = 0.5 } },
        });

        var reloaded = NewStore().Load();

        Assert.Equal("amarr", reloaded.ThemeId);
        Assert.True(reloaded.StartInTray);
        Assert.Equal(new WidgetSettings { IsFloating = true, X = 10, Y = 20, Opacity = 0.5 }, reloaded.Widgets["fleet"]);
    }

    [Fact]
    public void Map_layout_defaults_to_the_2d_map_and_is_saved_by_name()
    {
        Assert.Equal(MapLayout.Flat, NewStore().Load().Map.Layout);

        NewStore().Update(s => s with { Map = new MapSettings { Layout = MapLayout.TopDown } });

        Assert.Contains("\"TopDown\"", File.ReadAllText(SettingsPath));
        Assert.Equal(MapLayout.TopDown, NewStore().Load().Map.Layout);
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ not json");

        Assert.Null(NewStore().Load().ThemeId);
    }
}
