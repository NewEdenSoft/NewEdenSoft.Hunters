using CommunityToolkit.Mvvm.ComponentModel;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>
/// Which character the map is on, shared by everything that can point the map at a character: the
/// Characters widget, the markers on the map, and the main window (which brings a docked map into
/// view). Focusing moves the map to the character once; following keeps it there as the character
/// moves, until the user pans the map or stops following.
/// </summary>
public sealed class MapFocus : ObservableObject
{
    private long? _focused;
    private long? _followed;

    /// <summary>The character last focused, shown on the map with a follow button; null when dismissed.</summary>
    public long? FocusedCharacterId
    {
        get => _focused;
        private set => SetProperty(ref _focused, value);
    }

    /// <summary>The character the map follows, or null.</summary>
    public long? FollowedCharacterId
    {
        get => _followed;
        private set => SetProperty(ref _followed, value);
    }

    /// <summary>The map should move to a character; the argument is the character id.</summary>
    public event EventHandler<long>? FocusRequested;

    /// <summary>Moves the map to the character. Focusing another character stops following.</summary>
    public void Focus(long characterId)
    {
        if (FollowedCharacterId != characterId)
            FollowedCharacterId = null;
        FocusedCharacterId = characterId;
        FocusRequested?.Invoke(this, characterId);
    }

    /// <summary>Moves the map to the character and keeps it there as the character moves.</summary>
    public void Follow(long characterId)
    {
        FollowedCharacterId = characterId;
        Focus(characterId);
    }

    public void ToggleFollow(long characterId)
    {
        if (FollowedCharacterId == characterId)
            StopFollowing();
        else
            Follow(characterId);
    }

    public void StopFollowing() => FollowedCharacterId = null;

    /// <summary>Forgets the focus and stops following (dismissed, or another session).</summary>
    public void Clear()
    {
        FollowedCharacterId = null;
        FocusedCharacterId = null;
    }
}
