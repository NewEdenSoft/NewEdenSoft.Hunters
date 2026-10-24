using System.Text.Json;
using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Theming;

public sealed record ThemeDefinition(string Id, string Name, string Group, Rgba Primary, Rgba Secondary, Rgba Tint);

/// <summary>A derived colour: <c>Percent</c>% of operand <c>A</c> mixed with operand <c>B</c>.</summary>
public sealed record MixRule(string A, double Percent, string B);

/// <summary>
/// How a hunting profile is marked on the map: a two-letter code and a colour, or an icon from
/// <see cref="ProfileIcons"/> drawn instead of the code.
/// </summary>
public sealed record ProfileStyle(string Code, Rgba Colour, string? Icon = null);

/// <summary>The contents of <c>design/themes.json</c>, shared with the web app.</summary>
public sealed class ThemeCatalog
{
    public const string ResourceName = "NewEdenSoft.Hunters.Desktop.themes.json";

    public required string DefaultThemeId { get; init; }
    public required IReadOnlyList<ThemeDefinition> Themes { get; init; }
    public required IReadOnlyDictionary<string, MixRule> Derived { get; init; }
    public required IReadOnlyDictionary<string, Rgba> Fixed { get; init; }

    /// <summary>Index 0 = security 0.0 … index 10 = security 1.0.</summary>
    public required IReadOnlyList<Rgba> SecuritySteps { get; init; }
    public required Rgba SecurityNull { get; init; }

    /// <summary>
    /// The lower bounds of the five heat bands per activity metric (<c>npcKills</c>, <c>shipKills</c>,
    /// <c>jumps</c>, <c>npcChange</c>), drawn in <c>heat-1</c> to <c>heat-5</c>.
    /// </summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<int>> Heat { get; init; }

    /// <summary>Per hunting profile id: its code, colour and icon.</summary>
    public required IReadOnlyDictionary<string, ProfileStyle> Profiles { get; init; }

    /// <summary>
    /// A profile's style; a profile without an entry gets the first letters of its name in the glow colour,
    /// as on the web (<c>profileStyle</c> in <c>web/hunters-web/src/theme/themes.ts</c>).
    /// </summary>
    public ProfileStyle ProfileStyle(string id, string name)
    {
        if (Profiles.TryGetValue(id, out var style))
            return style;

        var letters = new string(name.Where(char.IsAsciiLetter).Take(2).ToArray()).ToUpperInvariant();
        return new ProfileStyle(letters.Length == 0 ? "?" : letters, Fixed["hunt-glow"]);
    }

    public ThemeDefinition Get(string? id) =>
        Themes.FirstOrDefault(t => t.Id == id) ?? Themes.First(t => t.Id == DefaultThemeId);

    /// <summary>Colour for a system's security status as EVE displays it (<see cref="SecurityStatus.Display"/>).</summary>
    public Rgba SecurityColor(double security) =>
        SecurityStatus.Bucket(security) is var bucket && bucket == SecurityStatus.NullBucket ? SecurityNull : SecuritySteps[bucket];

    /// <summary>Loads the catalog embedded in this assembly.</summary>
    public static ThemeCatalog LoadEmbedded()
    {
        using var stream = typeof(ThemeCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        return Load(stream);
    }

    public static ThemeCatalog Load(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var themes = root.GetProperty("themes").EnumerateArray()
            .Select(t => new ThemeDefinition(
                t.GetProperty("id").GetString()!,
                t.GetProperty("name").GetString()!,
                t.GetProperty("group").GetString()!,
                Rgba.Parse(t.GetProperty("primary").GetString()!),
                Rgba.Parse(t.GetProperty("secondary").GetString()!),
                Rgba.Parse(t.GetProperty("tint").GetString()!)))
            .ToList();

        var derived = root.GetProperty("derived").EnumerateObject()
            .ToDictionary(
                p => p.Name,
                p =>
                {
                    var mix = p.Value.GetProperty("mix");
                    return new MixRule(mix[0].GetString()!, mix[1].GetDouble(), mix[2].GetString()!);
                });

        var fixedColors = root.GetProperty("fixed").EnumerateObject()
            .ToDictionary(p => p.Name, p => Rgba.Parse(p.Value.GetString()!));

        var security = root.GetProperty("security");

        return new ThemeCatalog
        {
            DefaultThemeId = root.GetProperty("defaultTheme").GetString()!,
            Themes = themes,
            Derived = derived,
            Fixed = fixedColors,
            SecuritySteps = security.GetProperty("steps").EnumerateArray().Select(s => Rgba.Parse(s.GetString()!)).ToList(),
            SecurityNull = Rgba.Parse(security.GetProperty("null").GetString()!),
            Profiles = root.GetProperty("profiles").EnumerateObject()
                .ToDictionary(
                    p => p.Name,
                    p => new ProfileStyle(
                        p.Value.GetProperty("code").GetString()!,
                        Rgba.Parse(p.Value.GetProperty("colour").GetString()!),
                        p.Value.TryGetProperty("icon", out var icon) ? icon.GetString() : null)),
            Heat = root.GetProperty("heat").EnumerateObject()
                .ToDictionary(p => p.Name, IReadOnlyList<int> (p) => p.Value.EnumerateArray().Select(b => b.GetInt32()).ToList()),
        };
    }
}
