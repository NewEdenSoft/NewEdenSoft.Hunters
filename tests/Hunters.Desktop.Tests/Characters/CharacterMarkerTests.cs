using Microsoft.Extensions.Logging.Abstractions;
using NewEdenSoft.Hunters.Desktop.Characters;
using NewEdenSoft.Hunters.Desktop.Settings;
using NewEdenSoft.Hunters.Desktop.Tests.Settings;
using SkiaSharp;

namespace NewEdenSoft.Hunters.Desktop.Tests.Characters;

public sealed class CharacterMarkerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hunters-markers-").FullName;

    private string SettingsPath => Path.Combine(_dir, "desktop-settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private CharacterPreferences NewPreferences() =>
        new(TestProfiles.Open(new JsonFileSettingsStore(SettingsPath, NullLogger<JsonFileSettingsStore>.Instance)));

    [Theory]
    [InlineData("Jita Trader", "J")]
    [InlineData("vex Arkhan", "V")]
    [InlineData("1st Hunter", "S")]
    [InlineData("--", "A")]
    public void Default_is_the_first_letter_of_the_name(string name, string letter)
    {
        Assert.Equal(new CharacterMarker(MarkerKind.Letter, letter), CharacterMarker.Default(name));
    }

    [Fact]
    public void Offers_letters_numbers_and_icons()
    {
        Assert.Equal(26, CharacterMarker.Letters.Count);
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8", "9"], CharacterMarker.Numbers.Select(m => m.Value));
        Assert.All(CharacterMarker.Letters.Concat(CharacterMarker.Numbers).Concat(CharacterMarker.Icons), m => Assert.True(m.IsValid));
    }

    [Fact]
    public void Icon_paths_parse_for_the_map()
    {
        // The map draws icons with Skia; the badge with Avalonia. Both read SVG path data.
        foreach (var icon in CharacterMarker.Icons)
        {
            using var path = SKPath.ParseSvgPathData(icon.IconPath);
            Assert.NotNull(path);
            Assert.InRange(path.Bounds.Left, 0, 24);
            Assert.InRange(path.Bounds.Right, 0, 24);
        }
    }

    [Theory]
    [InlineData(MarkerKind.Letter, "AB")]
    [InlineData(MarkerKind.Letter, "a")]
    [InlineData(MarkerKind.Number, "0")]
    [InlineData(MarkerKind.Number, "10")]
    [InlineData(MarkerKind.Icon, "rocket")]
    public void Other_values_are_not_markers(MarkerKind kind, string value)
    {
        Assert.False(new CharacterMarker(kind, value).IsValid);
    }

    [Fact]
    public void Chosen_markers_are_saved_by_character()
    {
        var star = new CharacterMarker(MarkerKind.Icon, "star");
        long? changed = null;
        var preferences = NewPreferences();
        preferences.MarkerChanged += (_, id) => changed = id;

        preferences.SetMarker(90000001, star);

        Assert.Equal(90000001, changed);
        var reloaded = NewPreferences();
        Assert.Equal(star, reloaded.GetMarker(90000001, "Jita Trader"));
        Assert.Equal(new CharacterMarker(MarkerKind.Letter, "O"), reloaded.GetMarker(90000002, "Other Pilot"));
    }

    [Fact]
    public void An_unknown_saved_marker_falls_back_to_the_default()
    {
        var key = ProfileStore.KeyOf(TestProfiles.Server, TestProfiles.Account);
        File.WriteAllText(SettingsPath, $$"""{ "Profiles": { "{{key}}": { "Characters": { "90000001": { "Marker": { "Kind": "Icon", "Value": "rocket" } } } } } }""");

        Assert.Equal(new CharacterMarker(MarkerKind.Letter, "J"), NewPreferences().GetMarker(90000001, "Jita Trader"));
    }

    [Fact]
    public void Invalid_markers_are_refused()
    {
        Assert.Throws<ArgumentException>(() => NewPreferences().SetMarker(1, new CharacterMarker(MarkerKind.Number, "42")));
    }
}
