using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NewEdenSoft.Hunters.Desktop.Settings;

/// <summary>Per-user desktop preferences. Secrets never go here; they belong in the OS credential store.</summary>
public sealed record DesktopSettings
{
    public string? ThemeId { get; init; }
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
