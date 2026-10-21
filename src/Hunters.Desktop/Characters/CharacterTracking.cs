using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Client.Tracking;
using NewEdenSoft.Hunters.Contracts.Account;

namespace NewEdenSoft.Hunters.Desktop.Characters;

/// <summary>
/// The app's one <see cref="CharacterTracker"/>, for the signed-in session: it tracks the account's
/// characters that can be tracked (logged in, all permissions) and that the user has not switched
/// off, and follows changes to the account and to those switches.
/// </summary>
/// <remarks>
/// <see cref="Changed"/> is raised on the polling thread, like the tracker's; UI code marshals it.
/// </remarks>
public sealed class CharacterTracking(ILocationEsi esi, CharacterPreferences preferences)
{
    private readonly Lock _lock = new();
    private CharacterTracker? _tracker;
    private HuntersSession? _session;

    /// <summary>A tracked character changed, or stopped being tracked.</summary>
    public event EventHandler<TrackedCharacterChangedEventArgs>? Changed;

    public TrackedCharacter? Get(long characterId)
    {
        lock (_lock)
            return _tracker?.Get(characterId);
    }

    /// <summary>Starts tracking for a new session, replacing any earlier one; null stops tracking.</summary>
    public void SetSession(HuntersSession? session)
    {
        CharacterTracker? old;
        lock (_lock)
        {
            if (ReferenceEquals(session, _session))
                return;

            if (_session is not null)
            {
                _session.AccountChanged -= OnAccountChanged;
                preferences.TrackingChanged -= OnTrackingChanged;
            }

            old = _tracker;
            _session = session;
            _tracker = null;
            if (session is not null)
            {
                _tracker = new CharacterTracker(esi, session);
                _tracker.Changed += OnTrackerChanged;
                session.AccountChanged += OnAccountChanged;
                preferences.TrackingChanged += OnTrackingChanged;
            }
        }

        if (old is not null)
        {
            old.Changed -= OnTrackerChanged;
            // Let the views drop the old session's characters.
            foreach (var character in old.Characters)
                Changed?.Invoke(this, new TrackedCharacterChangedEventArgs(character.CharacterId, character, null));
            _ = old.DisposeAsync().AsTask();
        }

        Sync();
    }

    /// <summary>The characters to track: on the account, usable, and not switched off.</summary>
    public static IEnumerable<long> Trackable(AccountInfo account, CharacterPreferences preferences) =>
        account.Characters.Where(c => c.Status == CharacterStatus.Active && preferences.IsTracked(c.Id)).Select(c => c.Id);

    private void Sync()
    {
        lock (_lock)
        {
            if (_tracker is not null && _session is { IsEnded: false } session)
                _tracker.Track(Trackable(session.Account, preferences));
        }
    }

    private void OnAccountChanged(object? sender, EventArgs e) => Sync();

    private void OnTrackingChanged(object? sender, long characterId) => Sync();

    private void OnTrackerChanged(object? sender, TrackedCharacterChangedEventArgs e) => Changed?.Invoke(this, e);
}
