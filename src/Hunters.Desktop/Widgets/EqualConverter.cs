using System.Globalization;
using Avalonia.Data.Converters;

namespace NewEdenSoft.Hunters.Desktop.Widgets;

/// <summary>True when every bound value equals the first, e.g. an item's id against the selected one.</summary>
public sealed class EqualConverter : IMultiValueConverter
{
    public static readonly EqualConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count > 0 && values.All(v => Equals(v, values[0]));
}
