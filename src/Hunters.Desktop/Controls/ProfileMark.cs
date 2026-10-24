using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Theming;

namespace NewEdenSoft.Hunters.Desktop.Controls;

/// <summary>
/// A hunting profile's mark, as on its map badge: its icon (a stroke path) or its code, in its colour.
/// Used in Map controls and the map's hover panel.
/// </summary>
public sealed class ProfileMark : Decorator
{
    public static readonly StyledProperty<ProfileStyle?> ProfileProperty =
        AvaloniaProperty.Register<ProfileMark, ProfileStyle?>(nameof(Profile));

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<ProfileMark, double>(nameof(FontSize), 11);

    public ProfileStyle? Profile
    {
        get => GetValue(ProfileProperty);
        set => SetValue(ProfileProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ProfileProperty || change.Property == FontSizeProperty)
            Child = Build();
    }

    private Control? Build()
    {
        if (Profile is not { } style)
            return null;

        var brush = new SolidColorBrush(ThemeService.ToColor(style.Colour));
        if (ProfileIcons.PathOf(style.Icon) is { } data)
        {
            var size = FontSize + 3;
            return new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(data),
                Stroke = brush,
                StrokeThickness = 1.9,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        return new TextBlock
        {
            Text = style.Code,
            Foreground = brush,
            FontSize = FontSize,
            FontWeight = FontWeight.Bold,
            FontFamily = Application.Current?.FindResource("Hud.DisplayFont") as FontFamily ?? FontFamily.Default,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }
}
