using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

/// <summary>
/// The jump range around the focused system: on or off, and how far, from a ship class's preset or a
/// custom value. Like the web's Jump range panel (<c>JumpRangePanel.tsx</c>); saved in
/// <see cref="MapPreferences.JumpRange"/>.
/// </summary>
public sealed partial class JumpRangeWidgetViewModel : WidgetViewModelBase
{
    private readonly MapPreferences _preferences;
    private readonly MapWidgetViewModel _map;
    private string _customText;

    public JumpRangeWidgetViewModel(MapPreferences preferences, MapWidgetViewModel map)
    {
        _preferences = preferences;
        _map = map;
        _customText = Format(preferences.JumpRange.Ly);
        Presets = JumpRanges.Presets
            .Select(p => new ChoiceViewModel(p.Label, new RelayCommand(() => SetLy(p.Ly)))
            {
                ToolTip = $"{JumpRanges.FormatLy(p.Ly)} with Jump Drive Calibration V",
            })
            .ToList();
        SyncPresets();

        preferences.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MapPreferences.JumpRange))
                return;
            SyncPresets();
            OnPropertyChanged(nameof(IsOn));
            OnPropertyChanged(nameof(ToggleText));
        };
        map.PropertyChanged += OnMapChanged;
    }

    /// <summary>The ship classes' ranges with Jump Drive Calibration V; the one matching the range is highlighted.</summary>
    public IReadOnlyList<ChoiceViewModel> Presets { get; }

    public bool IsOn => _preferences.JumpRange.Enabled;

    /// <summary>"On · 8 ly" or "Off · 8 ly".</summary>
    public string ToggleText => $"{(IsOn ? "On" : "Off")} · {JumpRanges.FormatLy(_preferences.JumpRange.Ly)}";

    /// <summary>
    /// The custom range as typed. A number sets the range, kept within 0.5 to 20 ly; the text itself is
    /// left alone while typing, and follows the range when a preset is chosen.
    /// </summary>
    public string CustomText
    {
        get => _customText;
        set
        {
            if (!SetProperty(ref _customText, value))
                return;
            if (double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var ly) && double.IsFinite(ly))
                _preferences.JumpRange = _preferences.JumpRange with { Ly = ly };
        }
    }

    /// <summary>What the blue means, or that a system must be focused first.</summary>
    public string Hint => _map.FocusedSystem is { } focused
        ? $"Blue: systems a jump drive can land in from {focused.Name} (not high-sec, Pochven or Zarzakh)."
        : "Focus a system (double click it) to see what is in range of it.";

    [RelayCommand]
    private void Toggle() => _preferences.JumpRange = _preferences.JumpRange with { Enabled = !IsOn };

    private void SetLy(double ly)
    {
        _preferences.JumpRange = _preferences.JumpRange with { Ly = ly };
        _customText = Format(_preferences.JumpRange.Ly);
        OnPropertyChanged(nameof(CustomText));
    }

    private void SyncPresets()
    {
        for (var i = 0; i < Presets.Count; i++)
            Presets[i].IsSelected = JumpRanges.Presets[i].Ly == _preferences.JumpRange.Ly;
    }

    private void OnMapChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapWidgetViewModel.FocusedSystem))
            OnPropertyChanged(nameof(Hint));
    }

    private static string Format(double ly) => ly.ToString("0.##", CultureInfo.InvariantCulture);
}
