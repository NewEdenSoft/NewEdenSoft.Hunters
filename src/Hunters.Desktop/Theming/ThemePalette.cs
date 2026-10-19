namespace NewEdenSoft.Hunters.Desktop.Theming;

/// <summary>Every named colour for one theme: inputs, derived and fixed, keyed as in themes.json.</summary>
public static class ThemePalette
{
    public static IReadOnlyDictionary<string, Rgba> Resolve(ThemeCatalog catalog, ThemeDefinition theme)
    {
        var colors = new Dictionary<string, Rgba>
        {
            ["primary"] = theme.Primary,
            ["secondary"] = theme.Secondary,
            ["tint"] = theme.Tint,
        };

        foreach (var (key, rule) in catalog.Derived)
            colors[key] = OkLab.Mix(Operand(rule.A), rule.Percent, Operand(rule.B));

        foreach (var (key, color) in catalog.Fixed)
            colors[key] = color;

        return colors;

        Rgba Operand(string operand) => operand switch
        {
            "transparent" => Rgba.Transparent,
            "primary" => theme.Primary,
            "secondary" => theme.Secondary,
            "tint" => theme.Tint,
            _ when operand.StartsWith('#') => Rgba.Parse(operand),
            _ => throw new FormatException($"Unknown mix operand '{operand}'."),
        };
    }

    /// <summary><c>surface-2</c> → <c>Surface2</c>, used for resource keys.</summary>
    public static string ToPascalCase(string key) =>
        string.Concat(key.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
