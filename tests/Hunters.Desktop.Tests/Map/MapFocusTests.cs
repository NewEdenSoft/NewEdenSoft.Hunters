using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class MapFocusTests
{
    private readonly MapFocus _focus = new();
    private readonly List<long> _requests = [];

    public MapFocusTests() => _focus.FocusRequested += (_, id) => _requests.Add(id);

    [Fact]
    public void Focusing_moves_the_map_once_without_following()
    {
        _focus.Focus(1);

        Assert.Equal(1, _focus.FocusedCharacterId);
        Assert.Null(_focus.FollowedCharacterId);
        Assert.Equal([1L], _requests);
    }

    [Fact]
    public void Following_also_focuses_and_toggles_off()
    {
        _focus.ToggleFollow(1);
        Assert.Equal(1, _focus.FollowedCharacterId);
        Assert.Equal(1, _focus.FocusedCharacterId);
        Assert.Equal([1L], _requests);

        _focus.ToggleFollow(1);
        Assert.Null(_focus.FollowedCharacterId);
        Assert.Equal(1, _focus.FocusedCharacterId);
    }

    [Fact]
    public void Focusing_another_character_stops_following()
    {
        _focus.Follow(1);

        _focus.Focus(1);
        Assert.Equal(1, _focus.FollowedCharacterId);

        _focus.Focus(2);
        Assert.Null(_focus.FollowedCharacterId);
        Assert.Equal(2, _focus.FocusedCharacterId);
    }

    [Fact]
    public void Panning_stops_following_and_dismissing_forgets_the_focus()
    {
        _focus.Follow(1);

        _focus.StopFollowing();
        Assert.Null(_focus.FollowedCharacterId);
        Assert.Equal(1, _focus.FocusedCharacterId);

        _focus.Clear();
        Assert.Null(_focus.FocusedCharacterId);
    }
}
