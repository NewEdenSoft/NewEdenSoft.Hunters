using NewEdenSoft.Hunters.Desktop.Theming;

namespace NewEdenSoft.Hunters.Desktop.Tests.Theming;

/// <summary>Validates design/themes.json as embedded in the desktop app.</summary>
public sealed class ThemeCatalogTests
{
    private readonly ThemeCatalog _catalog = ThemeCatalog.LoadEmbedded();

    [Fact]
    public void Default_theme_exists()
    {
        Assert.Contains(_catalog.Themes, t => t.Id == _catalog.DefaultThemeId);
    }

    [Fact]
    public void Theme_ids_are_unique()
    {
        Assert.Equal(_catalog.Themes.Count, _catalog.Themes.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void Every_theme_resolves_every_derived_and_fixed_colour()
    {
        foreach (var theme in _catalog.Themes)
        {
            var palette = ThemePalette.Resolve(_catalog, theme);

            foreach (var key in _catalog.Derived.Keys.Concat(_catalog.Fixed.Keys))
                Assert.True(palette.ContainsKey(key), $"{theme.Id} is missing {key}");
        }
    }

    [Fact]
    public void Unknown_theme_falls_back_to_default()
    {
        Assert.Equal(_catalog.DefaultThemeId, _catalog.Get("no-such-theme").Id);
    }

    [Theory]
    [InlineData(1.0, "#2fefef")]
    [InlineData(0.5, "#efef00")]
    [InlineData(0.45, "#efef00")]
    [InlineData(0.44, "#d77700")]
    [InlineData(0.03, "#a33a45")]
    [InlineData(0.0, "#8b3061")]
    [InlineData(-0.04, "#8b3061")]
    [InlineData(-0.05, "#8b3061")]
    [InlineData(-1.0, "#8b3061")]
    public void Security_colour_rounds_like_eve(double security, string expected)
    {
        Assert.Equal(expected, _catalog.SecurityColor(security).ToHex());
    }

    [Theory]
    [InlineData("surface-2", "Surface2")]
    [InlineData("fg-muted", "FgMuted")]
    [InlineData("bg", "Bg")]
    public void Resource_names_are_pascal_case(string key, string expected)
    {
        Assert.Equal(expected, ThemePalette.ToPascalCase(key));
    }
}
