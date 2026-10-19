using System.Globalization;
using Avalonia.Data.Converters;

namespace NewEdenSoft.Hunters.Desktop.Widgets;

/// <summary>Upper-cases text for HUD labels (Avalonia has no text-transform).</summary>
public sealed class UpperCaseConverter : IValueConverter
{
    public static readonly UpperCaseConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString()?.ToUpper(culture);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
