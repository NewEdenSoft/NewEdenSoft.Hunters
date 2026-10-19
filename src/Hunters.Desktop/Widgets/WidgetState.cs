using CommunityToolkit.Mvvm.ComponentModel;
using NewEdenSoft.Hunters.Desktop.Settings;

namespace NewEdenSoft.Hunters.Desktop.Widgets;

/// <summary>
/// Live layout state of one widget, bound by its main-window page and its overlay window.
/// Owned by <see cref="WidgetManager"/>, which persists changes.
/// </summary>
public sealed partial class WidgetState : ObservableObject
{
    public const double MinOpacity = 0.2;

    public WidgetState(WidgetDefinition definition, WidgetViewModelBase content, WidgetSettings settings)
    {
        Definition = definition;
        Content = content;
        IsFloating = settings.IsFloating;
        Opacity = Math.Clamp(settings.Opacity, MinOpacity, 1);
        ClickThrough = settings.ClickThrough;
        Locked = settings.Locked;
        X = settings.X;
        Y = settings.Y;
        Width = Math.Max(settings.Width ?? definition.DefaultWidth, definition.MinWidth);
        Height = Math.Max(settings.Height ?? definition.DefaultHeight, definition.MinHeight);
    }

    public WidgetDefinition Definition { get; }

    public WidgetViewModelBase Content { get; }

    public string Id => Definition.Id;

    public string Title => Definition.Title;

    /// <summary>Shown as an overlay window rather than as a page in the main window.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeLabel))]
    public partial bool IsFloating { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpacityPercent))]
    public partial double Opacity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMove), nameof(ModeLabel))]
    public partial bool ClickThrough { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMove), nameof(ModeLabel))]
    public partial bool Locked { get; set; }

    /// <summary>Overlay position in screen pixels; null until placed.</summary>
    public int? X { get; set; }
    public int? Y { get; set; }

    /// <summary>Overlay size in device-independent pixels.</summary>
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>The overlay can be dragged and resized.</summary>
    public bool CanMove => !Locked && !ClickThrough;

    public int OpacityPercent => (int)Math.Round(Opacity * 100);

    /// <summary>Short status shown in the overlay header.</summary>
    public string ModeLabel => ClickThrough ? "CLICK-THROUGH" : Locked ? "LOCKED" : "MOVABLE";

    public WidgetSettings ToSettings() => new()
    {
        IsFloating = IsFloating,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        Opacity = Opacity,
        ClickThrough = ClickThrough,
        Locked = Locked,
    };
}
