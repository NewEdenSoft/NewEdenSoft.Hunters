using Avalonia;
using Avalonia.Media;
using NewEdenSoft.Hunters.Desktop.Settings;

namespace NewEdenSoft.Hunters.Desktop.Theming;

/// <summary>
/// Applies a theme by writing its palette into the application resources as
/// <c>Hud.{Name}Color</c> and <c>Hud.{Name}Brush</c>. Views reference them with
/// <c>DynamicResource</c>, so switching themes updates the UI live.
/// </summary>
public sealed class ThemeService(ThemeCatalog catalog, ISettingsStore settings)
{
    public ThemeCatalog Catalog => catalog;

    public ThemeDefinition Current { get; private set; } = catalog.Get(null);

    public event EventHandler<ThemeDefinition>? ThemeChanged;

    /// <summary>Applies the saved theme, or the default. Call once on the UI thread at startup.</summary>
    public void ApplySaved() => Apply(settings.Load().ThemeId, persist: false);

    public void Apply(string? themeId, bool persist = true)
    {
        var resources = Application.Current?.Resources
            ?? throw new InvalidOperationException("The application has not been created.");

        var theme = catalog.Get(themeId);
        foreach (var (key, rgba) in ThemePalette.Resolve(catalog, theme))
        {
            var name = ThemePalette.ToPascalCase(key);
            var color = ToColor(rgba);
            resources[$"Hud.{name}Color"] = color;
            resources[$"Hud.{name}Brush"] = new SolidColorBrush(color);
        }

        // Built-in Fluent controls follow the theme's primary colour.
        var primary = theme.Primary;
        resources["SystemAccentColor"] = ToColor(primary);
        resources["SystemAccentColorLight1"] = ToColor(OkLab.Mix(primary, 80, Rgba.Parse("#ffffff")));
        resources["SystemAccentColorLight2"] = ToColor(OkLab.Mix(primary, 60, Rgba.Parse("#ffffff")));
        resources["SystemAccentColorLight3"] = ToColor(OkLab.Mix(primary, 40, Rgba.Parse("#ffffff")));
        resources["SystemAccentColorDark1"] = ToColor(OkLab.Mix(primary, 80, theme.Tint));
        resources["SystemAccentColorDark2"] = ToColor(OkLab.Mix(primary, 60, theme.Tint));
        resources["SystemAccentColorDark3"] = ToColor(OkLab.Mix(primary, 40, theme.Tint));

        Current = theme;
        if (persist)
            settings.Update(s => s with { ThemeId = theme.Id });

        ThemeChanged?.Invoke(this, theme);
    }

    public static Color ToColor(Rgba rgba) => Color.FromArgb(rgba.A8, rgba.R8, rgba.G8, rgba.B8);
}
