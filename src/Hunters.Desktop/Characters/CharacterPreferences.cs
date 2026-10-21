using NewEdenSoft.Hunters.Desktop.Settings;

namespace NewEdenSoft.Hunters.Desktop.Characters;

/// <summary>
/// The user's per-character choices, saved in this computer's desktop settings by character id:
/// the map marker, and whether the character is tracked.
/// </summary>
public sealed class CharacterPreferences(ISettingsStore settings)
{
    /// <summary>A character's marker changed; the argument is the character id.</summary>
    public event EventHandler<long>? MarkerChanged;

    /// <summary>A character was switched to tracked or untracked; the argument is the character id.</summary>
    public event EventHandler<long>? TrackingChanged;

    /// <summary>Tracked unless the user switched it off.</summary>
    public bool IsTracked(long characterId) =>
        settings.Load().Characters.GetValueOrDefault(characterId)?.IsTracked ?? true;

    public void SetTracked(long characterId, bool tracked)
    {
        if (IsTracked(characterId) == tracked)
            return;

        Update(characterId, c => c with { IsTracked = tracked });
        TrackingChanged?.Invoke(this, characterId);
    }

    /// <summary>The chosen marker, or the default for the name when none was chosen.</summary>
    public CharacterMarker GetMarker(long characterId, string characterName) =>
        settings.Load().Characters.GetValueOrDefault(characterId)?.Marker is { IsValid: true } marker
            ? marker
            : CharacterMarker.Default(characterName);

    public void SetMarker(long characterId, CharacterMarker marker)
    {
        if (!marker.IsValid)
            throw new ArgumentException($"'{marker.Value}' is not a {marker.Kind} marker.", nameof(marker));

        Update(characterId, c => c with { Marker = marker });
        MarkerChanged?.Invoke(this, characterId);
    }

    private void Update(long characterId, Func<CharacterSettings, CharacterSettings> change) =>
        settings.Update(s => s with
        {
            Characters = new Dictionary<long, CharacterSettings>(s.Characters)
            {
                [characterId] = change(s.Characters.GetValueOrDefault(characterId) ?? new CharacterSettings()),
            },
        });
}
