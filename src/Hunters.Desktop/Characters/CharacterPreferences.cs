using NewEdenSoft.Hunters.Desktop.Settings;

namespace NewEdenSoft.Hunters.Desktop.Characters;

/// <summary>
/// The user's per-character choices, saved in this computer's desktop settings by character id:
/// the map marker so far, and later whether the character is tracked.
/// </summary>
public sealed class CharacterPreferences(ISettingsStore settings)
{
    /// <summary>A character's marker changed; the argument is the character id.</summary>
    public event EventHandler<long>? MarkerChanged;

    /// <summary>The chosen marker, or the default for the name when none was chosen.</summary>
    public CharacterMarker GetMarker(long characterId, string characterName) =>
        settings.Load().Characters.GetValueOrDefault(characterId)?.Marker is { IsValid: true } marker
            ? marker
            : CharacterMarker.Default(characterName);

    public void SetMarker(long characterId, CharacterMarker marker)
    {
        if (!marker.IsValid)
            throw new ArgumentException($"'{marker.Value}' is not a {marker.Kind} marker.", nameof(marker));

        settings.Update(s => s with
        {
            Characters = new Dictionary<long, CharacterSettings>(s.Characters)
            {
                [characterId] = (s.Characters.GetValueOrDefault(characterId) ?? new CharacterSettings()) with { Marker = marker },
            },
        });
        MarkerChanged?.Invoke(this, characterId);
    }
}
