using System.ComponentModel;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Desktop.Platform;
using NewEdenSoft.Hunters.Desktop.Settings;

namespace NewEdenSoft.Hunters.Desktop.Widgets;

/// <summary>
/// Owns every widget's layout state and overlay window: floating and docking, applying overlay
/// settings, restoring overlays at login and persisting changes to the account's profile
/// (<see cref="ProfileStore"/>), 500 ms after the last change and when the session ends. UI thread only.
/// </summary>
public sealed partial class WidgetManager : ObservableObject
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private const int ScreenMargin = 24;

    private readonly ProfileStore _settings;
    private readonly IOverlayPlatform _platform;
    private readonly Dictionary<string, OverlayWindow> _windows = [];
    private readonly DispatcherTimer _saveTimer;
    private bool _shuttingDown;

    public WidgetManager(
        IEnumerable<WidgetDefinition> definitions,
        IServiceProvider services,
        ProfileStore settings,
        IOverlayPlatform platform)
    {
        _settings = settings;
        _platform = platform;

        var saved = settings.Load().Widgets;
        Widgets = definitions
            .Select(d => new WidgetState(
                d,
                (WidgetViewModelBase)services.GetRequiredService(d.ViewModelType),
                saved.GetValueOrDefault(d.Id) ?? new WidgetSettings()))
            .ToList();

        foreach (var widget in Widgets)
            widget.PropertyChanged += OnWidgetChanged;

        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => Save();
    }

    public IReadOnlyList<WidgetState> Widgets { get; }

    public bool SupportsClickThrough => _platform.SupportsClickThrough;

    /// <summary>Temporarily hides all overlays without docking them.</summary>
    [ObservableProperty]
    public partial bool OverlaysHidden { get; set; }

    public WidgetState? Find(string id) => Widgets.FirstOrDefault(w => w.Id == id);

    /// <summary>Opens the overlays of widgets saved as floating. Call once, at login.</summary>
    public void RestoreOverlays()
    {
        foreach (var widget in Widgets.Where(w => w.IsFloating))
            ShowOverlay(widget);
    }

    public void Float(WidgetState widget) => widget.IsFloating = true;

    public void Dock(WidgetState widget) => widget.IsFloating = false;

    /// <summary>Moves the overlay back to its automatic position and default size.</summary>
    public void ResetPosition(WidgetState widget)
    {
        widget.X = null;
        widget.Y = null;
        widget.Width = widget.Definition.DefaultWidth;
        widget.Height = widget.Definition.DefaultHeight;

        if (_windows.TryGetValue(widget.Id, out var window))
            Place(window, widget);

        ScheduleSave();
    }

    /// <summary>Saves layout and closes overlays without docking them. Call when the session ends.</summary>
    public void Shutdown()
    {
        _shuttingDown = true;
        Save();
        foreach (var window in _windows.Values.ToList())
            window.Close();
        _windows.Clear();
    }

    partial void OnOverlaysHiddenChanged(bool value)
    {
        foreach (var (id, window) in _windows)
        {
            if (value)
                window.Hide();
            else
                window.Show();

            Find(id)!.Content.IsActive = !value;
        }
    }

    private void OnWidgetChanged(object? sender, PropertyChangedEventArgs e)
    {
        var widget = (WidgetState)sender!;
        switch (e.PropertyName)
        {
            case nameof(WidgetState.IsFloating):
                if (widget.IsFloating)
                    ShowOverlay(widget);
                else
                    CloseOverlay(widget);
                break;

            case nameof(WidgetState.ClickThrough):
                if (_windows.TryGetValue(widget.Id, out var window))
                    _platform.SetClickThrough(window, widget.ClickThrough);
                break;
        }

        ScheduleSave();
    }

    private void ShowOverlay(WidgetState widget)
    {
        if (_windows.ContainsKey(widget.Id))
            return;

        var window = new OverlayWindow { DataContext = widget };
        Place(window, widget);

        window.Opened += (_, _) =>
        {
            _platform.PrepareOverlay(window);
            _platform.SetClickThrough(window, widget.ClickThrough);
        };
        window.PositionChanged += (_, e) =>
        {
            widget.X = e.Point.X;
            widget.Y = e.Point.Y;
            ScheduleSave();
        };
        window.Resized += (_, e) =>
        {
            widget.Width = e.ClientSize.Width;
            widget.Height = e.ClientSize.Height;
            ScheduleSave();
        };
        window.Closed += (_, _) =>
        {
            _windows.Remove(widget.Id);
            widget.Content.IsActive = false;

            // Closed by the system (for example Alt+F4) rather than by docking: dock it.
            if (!_shuttingDown && widget.IsFloating)
                widget.IsFloating = false;
        };

        _windows[widget.Id] = window;
        if (!OverlaysHidden)
        {
            window.Show();
            widget.Content.IsActive = true;
        }
    }

    private void CloseOverlay(WidgetState widget)
    {
        if (_windows.Remove(widget.Id, out var window))
            window.Close();
        widget.Content.IsActive = false;
    }

    /// <summary>Applies the saved position, or stacks new overlays down the right edge of the primary screen.</summary>
    private void Place(OverlayWindow window, WidgetState widget)
    {
        window.Width = widget.Width;
        window.Height = widget.Height;
        window.MinWidth = widget.Definition.MinWidth;
        window.MinHeight = widget.Definition.MinHeight;

        var screens = window.Screens.All;
        if (widget.X is { } x && widget.Y is { } y
            && screens.Any(s => s.WorkingArea.Contains(new PixelPoint(x + ScreenMargin, y + ScreenMargin))))
        {
            window.Position = new PixelPoint(x, y);
            return;
        }

        var screen = window.Screens.Primary ?? screens.FirstOrDefault();
        if (screen is null)
            return;

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var index = Widgets.Where(w => w.IsFloating).ToList().IndexOf(widget);
        var offset = (int)(Math.Max(index, 0) * 40 * scale);

        window.Position = new PixelPoint(
            area.Right - (int)(widget.Width * scale) - (int)(ScreenMargin * scale) - offset,
            area.Y + (int)(ScreenMargin * 4 * scale) + offset);
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Save()
    {
        _saveTimer.Stop();
        _settings.Update(s => s with { Widgets = Widgets.ToDictionary(w => w.Id, w => w.ToSettings()) });
    }
}
