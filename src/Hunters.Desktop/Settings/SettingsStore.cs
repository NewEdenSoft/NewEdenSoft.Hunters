using System.Text.Json;
using Microsoft.Extensions.Logging;
using NewEdenSoft.Hunters.Desktop.Characters;
using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Settings;

/// <summary>
/// Per-user desktop preferences. Sessions and tokens never go here (docs/decisions.md, D20); the only
/// secret is a saved server's access key, as plain text, when the user saves one. What the login screen
/// needs is kept for this computer; the rest is kept per server and account, in <see cref="Profiles"/>.
/// </summary>
public sealed record DesktopSettings
{
    /// <summary>The servers saved on the login screen, last saved first.</summary>
    public IReadOnlyList<SavedServer> Servers { get; init; } = [];

    public string? ThemeId { get; init; }

    /// <summary>Start hidden in the system tray instead of opening the main window.</summary>
    public bool StartInTray { get; init; }

    /// <summary>The settings of each server and account, keyed by <see cref="ProfileStore.KeyOf"/>.</summary>
    public IReadOnlyDictionary<string, ProfileSettings> Profiles { get; init; } = new Dictionary<string, ProfileSettings>();
}

/// <summary>What one account on one server chose: the widgets' layout, the map and the characters.</summary>
public sealed record ProfileSettings
{
    /// <summary>Layout of each widget, keyed by widget id.</summary>
    public IReadOnlyDictionary<string, WidgetSettings> Widgets { get; init; } = new Dictionary<string, WidgetSettings>();

    public MapSettings Map { get; init; } = new();

    /// <summary>Per-character choices, keyed by character id.</summary>
    public IReadOnlyDictionary<long, CharacterSettings> Characters { get; init; } = new Dictionary<long, CharacterSettings>();
}

public sealed record CharacterSettings
{
    /// <summary>The character's map marker; null uses the default (the first letter of its name).</summary>
    public CharacterMarker? Marker { get; init; }

    /// <summary>Poll the character's online status, location and ship while the app runs.</summary>
    public bool IsTracked { get; init; } = true;
}

public sealed record MapSettings
{
    /// <summary>The map layout; CCP's 2D map unless the user picks another.</summary>
    public MapLayout Layout { get; init; } = MapLayout.Flat;

    /// <summary>The colour filter, chosen in Map controls; security unless the user picks another.</summary>
    public MapFilterId Filter { get; init; } = MapFilterId.Security;

    /// <summary>For NPC rising and falling: the minimum NPC kills in the last hour (one of <see cref="MapFilters.MinNpcChoices"/>).</summary>
    public int MinNpc { get; init; }

    /// <summary>The hunting profiles shown on the map (glow and badges), by id, in the order they were turned on.</summary>
    public IReadOnlyList<string> HuntingProfiles { get; init; } = [];

    /// <summary>When the focused system changes by itself, and whether a double click focuses.</summary>
    public MapBehaviour Behaviour { get; init; } = new();

    /// <summary>The jump range around the focused system: off by default, 8 ly.</summary>
    public JumpRangeSetting JumpRange { get; init; } = new();

    /// <summary>What the Nearby widget looks for around the focused system.</summary>
    public NearbySetting Nearby { get; init; } = new();
}

/// <summary>Where and how one widget is shown.</summary>
public sealed record WidgetSettings
{
    /// <summary>Shown as an overlay window instead of a page in the main window.</summary>
    public bool IsFloating { get; init; }

    /// <summary>Overlay position in screen pixels; null places it automatically.</summary>
    public int? X { get; init; }
    public int? Y { get; init; }

    /// <summary>Overlay size in device-independent pixels; null uses the widget's default.</summary>
    public double? Width { get; init; }
    public double? Height { get; init; }

    /// <summary>Opacity of the overlay's background panel, 0.2–1. Text stays fully opaque.</summary>
    public double Opacity { get; init; } = 0.8;

    /// <summary>Mouse input passes through the overlay to the window underneath.</summary>
    public bool ClickThrough { get; init; }

    /// <summary>The overlay cannot be moved or resized.</summary>
    public bool Locked { get; init; }
}

public interface ISettingsStore
{
    DesktopSettings Load();

    void Update(Func<DesktopSettings, DesktopSettings> change);
}

/// <summary>Stores <see cref="DesktopSettings"/> as JSON in the user's local application data.</summary>
public sealed partial class JsonFileSettingsStore(string path, ILogger<JsonFileSettingsStore> logger) : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Lock _gate = new();
    private DesktopSettings? _current;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewEdenSoft", "Hunters", "desktop-settings.json");

    public DesktopSettings Load()
    {
        lock (_gate)
            return _current ??= Read();
    }

    public void Update(Func<DesktopSettings, DesktopSettings> change)
    {
        lock (_gate)
        {
            _current = change(_current ?? Read());
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_current, JsonOptions));
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogWriteFailed(ex, path);
            }
        }
    }

    private DesktopSettings Read()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(path), JsonOptions) ?? new DesktopSettings()
                : new DesktopSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LogReadFailed(ex, path);
            return new DesktopSettings();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read settings from {Path}; using defaults")]
    private partial void LogReadFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write settings to {Path}")]
    private partial void LogWriteFailed(Exception exception, string path);
}
