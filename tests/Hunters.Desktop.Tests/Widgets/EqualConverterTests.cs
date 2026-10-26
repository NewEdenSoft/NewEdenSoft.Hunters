using System.Globalization;
using NewEdenSoft.Hunters.Desktop.Widgets;

namespace NewEdenSoft.Hunters.Desktop.Tests.Widgets;

public sealed class EqualConverterTests
{
    private static object? Convert(params object?[] values) =>
        EqualConverter.Instance.Convert(values, typeof(bool), null, CultureInfo.InvariantCulture);

    [Fact]
    public void Is_true_only_when_the_values_are_equal()
    {
        Assert.Equal(true, Convert(30000142, (int?)30000142));
        Assert.Equal(false, Convert(30000142, (int?)30002813));
        Assert.Equal(false, Convert(30000142, null));
        Assert.Equal(false, Convert());
    }
}
