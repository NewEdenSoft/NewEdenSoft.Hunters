namespace NewEdenSoft.Hunters.Client.Tracking;

public enum TrackingStatus
{
    /// <summary>Tracking began; nothing has been read from ESI yet.</summary>
    Starting,

    /// <summary>ESI answers; the state is current.</summary>
    Tracking,

    /// <summary>The server can no longer refresh the character's token. Tracking stops until the character logs in again.</summary>
    NeedsLogin,

    /// <summary>The last attempt failed (server, ESI or permission); retrying with backoff. The last good state is kept.</summary>
    Failing,
}

/// <summary>Where a character is: its solar system, and the station or structure it is docked in.</summary>
public sealed record CharacterLocation(int SolarSystemId, long? StationId, long? StructureId)
{
    public bool IsDocked => StationId is not null || StructureId is not null;
}

/// <summary>The ship a character is flying.</summary>
public sealed record CharacterShip(int TypeId, string Name, long ItemId);

/// <summary>
/// What the tracker knows about one character. <see cref="Location"/> and <see cref="Ship"/> stay
/// at their last known values while the character is offline or polling fails.
/// </summary>
public sealed record TrackedCharacter(
    long CharacterId,
    TrackingStatus Status,
    bool? IsOnline = null,
    CharacterLocation? Location = null,
    CharacterShip? Ship = null,
    string? Error = null,
    DateTimeOffset? UpdatedAt = null)
{
    /// <summary>The same apart from <see cref="UpdatedAt"/>: nothing a subscriber needs to hear about.</summary>
    internal bool SameAs(TrackedCharacter? other) => other is not null && this with { UpdatedAt = null } == other with { UpdatedAt = null };
}

/// <summary>A tracked character changed. <see cref="Current"/> is null when it is no longer tracked.</summary>
public sealed class TrackedCharacterChangedEventArgs(long characterId, TrackedCharacter? previous, TrackedCharacter? current) : EventArgs
{
    public long CharacterId => characterId;

    public TrackedCharacter? Previous => previous;

    public TrackedCharacter? Current => current;

    /// <summary>The character is in another solar system than before (or its first known one).</summary>
    public bool SystemChanged => current?.Location?.SolarSystemId != previous?.Location?.SolarSystemId;
}
