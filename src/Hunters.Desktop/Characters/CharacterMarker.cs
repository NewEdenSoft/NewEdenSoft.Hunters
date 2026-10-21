using System.Text.Json.Serialization;

namespace NewEdenSoft.Hunters.Desktop.Characters;

[JsonConverter(typeof(JsonStringEnumConverter<MarkerKind>))]
public enum MarkerKind
{
    Letter,
    Number,
    Icon,
}

/// <summary>
/// How a character is marked on the map: a letter, a number or an icon, shown in a small badge
/// joined to the character's system. Chosen per character on the Account page; the default is the
/// first letter of the character's name.
/// </summary>
/// <param name="Value">"A"–"Z", "1"–"9", or an icon name from <see cref="MarkerIcons"/>.</param>
public sealed record CharacterMarker(MarkerKind Kind, string Value)
{
    public static readonly IReadOnlyList<CharacterMarker> Letters =
        [.. Enumerable.Range('A', 26).Select(c => new CharacterMarker(MarkerKind.Letter, ((char)c).ToString()))];

    public static readonly IReadOnlyList<CharacterMarker> Numbers =
        [.. Enumerable.Range(1, 9).Select(n => new CharacterMarker(MarkerKind.Number, n.ToString(System.Globalization.CultureInfo.InvariantCulture)))];

    public static readonly IReadOnlyList<CharacterMarker> Icons =
        [.. MarkerIcons.Names.Select(name => new CharacterMarker(MarkerKind.Icon, name))];

    /// <summary>The text shown in the badge; null for icons.</summary>
    [JsonIgnore]
    public string? Text => Kind == MarkerKind.Icon ? null : Value;

    /// <summary>The icon's path data in a 24 × 24 box; null for letters and numbers.</summary>
    [JsonIgnore]
    public string? IconPath => Kind == MarkerKind.Icon ? MarkerIcons.Path(Value) : null;

    /// <summary>One of the markers on offer; anything else (an edited or outdated settings file) is not.</summary>
    [JsonIgnore]
    public bool IsValid => Kind switch
    {
        MarkerKind.Letter => Letters.Contains(this),
        MarkerKind.Number => Numbers.Contains(this),
        MarkerKind.Icon => MarkerIcons.Path(Value) is not null,
        _ => false,
    };

    /// <summary>The first letter of the name, or A when it starts with something else.</summary>
    public static CharacterMarker Default(string characterName)
    {
        var first = characterName.FirstOrDefault(char.IsAsciiLetter);
        return new CharacterMarker(MarkerKind.Letter, first == default ? "A" : char.ToUpperInvariant(first).ToString());
    }
}

/// <summary>
/// Marker icons as SVG path data in a 24 × 24 box, drawn as 2-unit strokes with round joins. Plain
/// path data, so the badge control (Avalonia) and the map (Skia) draw the same shapes.
/// </summary>
public static class MarkerIcons
{
    private static readonly (string Name, string Path)[] All =
    [
        ("target", "M3 12 A9 9 0 1 0 21 12 A9 9 0 1 0 3 12 Z M12 1 L12 7 M12 17 L12 23 M1 12 L7 12 M17 12 L23 12"),
        ("star", "M12 2 L15 8.5 L22 9.3 L16.8 14.1 L18.3 21.1 L12 17.6 L5.7 21.1 L7.2 14.1 L2 9.3 L9 8.5 Z"),
        ("diamond", "M12 2 L21 12 L12 22 L3 12 Z"),
        ("triangle", "M12 3 L22 21 L2 21 Z"),
        ("bolt", "M13 2 L4 14 L11 14 L10 22 L19 10 L12 10 Z"),
        ("shield", "M12 2 L20 5 L20 11 C20 16 16.5 20 12 22 C7.5 20 4 16 4 11 L4 5 Z"),
        ("eye", "M2 12 C2 12 6 5 12 5 C18 5 22 12 22 12 C22 12 18 19 12 19 C6 19 2 12 2 12 Z M9 12 A3 3 0 1 0 15 12 A3 3 0 1 0 9 12 Z"),
        ("heart", "M12 21 C12 21 4 15.5 4 10 A4.5 4.5 0 0 1 12 7.2 A4.5 4.5 0 0 1 20 10 C20 15.5 12 21 12 21 Z"),
    ];

    public static IEnumerable<string> Names => All.Select(i => i.Name);

    public static string? Path(string name) => All.FirstOrDefault(i => i.Name == name).Path;
}
