using Avalonia.Media;
using NewEdenSoft.Hunters.Desktop.Characters;
using NewEdenSoft.Hunters.Desktop.Widgets.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Widgets;

public sealed class SystemMenuTests
{
    private static readonly DestinationCharacter Main = new(1, "Ava", IsMain: true, IsOnline: true);
    private static readonly DestinationCharacter Alt = new(2, "Bo", IsMain: false, IsOnline: false);

    private static SystemMenu Menu(IReadOnlyList<DestinationCharacter> tracked, bool isFocused = false, bool isBusy = false) => new(
        new MapSystemInfo(0, "Tama", "0.3", Brushes.White, "The Citadel", null),
        isFocused,
        tracked,
        [Main, Alt],
        isBusy,
        () => { },
        () => { },
        _ => { });

    [Fact]
    public void All_counts_and_names_the_tracked_characters()
    {
        var menu = Menu([Main, Alt]);
        Assert.Equal("All (2)", menu.AllText);
        Assert.Equal("Every tracked character: Ava, Bo", menu.AllTip);
        Assert.True(menu.CanNavigateAll);
    }

    [Fact]
    public void All_is_off_without_tracked_characters()
    {
        var menu = Menu([]);
        Assert.Equal("All", menu.AllText);
        Assert.Equal("No tracked characters", menu.AllTip);
        Assert.False(menu.CanNavigateAll);
    }

    [Fact]
    public void All_waits_while_a_destination_is_being_set() => Assert.False(Menu([Main], isBusy: true).CanNavigateAll);

    [Theory]
    [InlineData(false, "Focus system")]
    [InlineData(true, "Clear focus")]
    public void The_focused_system_offers_to_clear_the_focus(bool isFocused, string text) => Assert.Equal(text, Menu([], isFocused).FocusText);

    [Fact]
    public void An_offline_character_is_told_when_the_destination_shows()
    {
        Assert.Equal("Online", SystemMenu.CharacterTip(Main));
        Assert.StartsWith("Not online", SystemMenu.CharacterTip(Alt));
    }
}
