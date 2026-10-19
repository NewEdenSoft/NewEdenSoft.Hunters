using System.Text.Json;

namespace NewEdenSoft.Hunters.Desktop.Theming;

public sealed record ThemeDefinition(string Id, string Name, string Group, Rgba Primary, Rgba Secondary, Rgba Tint);

/// <summary>A derived colour: <c>Percent</c>% of operand <c>A</c> mixed with operand <c>B</c>.</summary>
public sealed record MixRule(string A, double Percent, string B);

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

    public ThemeDefinition Get(string? id) =>
        Themes.FirstOrDefault(t => t.Id == id) ?? Themes.First(t => t.Id == DefaultThemeId);

    /// <summary>Colour for a system's security status, rounded to one decimal as EVE displays it.</summary>
    public Rgba SecurityColor(double security)
    {
        var rounded = Math.Round(security, 1, MidpointRounding.AwayFromZero);
        if (rounded < 0)
            return SecurityNull;
        return SecuritySteps[(int)Math.Round(Math.Min(rounded, 1) * 10)];
    }

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
        };
    }
}
