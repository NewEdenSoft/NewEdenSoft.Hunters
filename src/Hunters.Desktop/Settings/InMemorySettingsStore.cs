namespace NewEdenSoft.Hunters.Desktop.Settings;

/// <summary>Non-persistent settings, for the XAML previewer and tests.</summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private DesktopSettings _current = new();

    public DesktopSettings Load() => _current;

    public void Update(Func<DesktopSettings, DesktopSettings> change) => _current = change(_current);
}
