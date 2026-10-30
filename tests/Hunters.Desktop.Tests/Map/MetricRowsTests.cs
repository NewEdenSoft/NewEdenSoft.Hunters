using NewEdenSoft.Hunters.Contracts.Activity;
using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class MetricRowsTests
{
    private static ActivityColumns Columns(int[] last, int[]? previous, double?[]? peakRatio = null, int?[]? hoursToPeak = null) => new(
        last,
        previous,
        [.. last.Select(v => v * 10)],
        [.. last.Select(v => v / 2.0)],
        peakRatio ?? [.. last.Select(_ => (double?)null)],
        hoursToPeak ?? [.. last.Select(_ => (int?)null)],
        [.. last.Select(_ => (int?)null)]);

    [Fact]
    public void Gives_each_metric_its_last_hour_change_day_usual_and_peak()
    {
        var metrics = new ActivityMetrics(
            Columns([5, 120], [5, 80], peakRatio: [null, 0.9]),
            Columns([0, 3], null),
            Columns([0, 1], [0, 2]),
            Columns([10, 400], [12, 380], hoursToPeak: [null, 3]));

        Assert.Equal(
            [
                new MetricRow("NPC kills", 120, 40, 1200, 60, "Peak now"),
                new MetricRow("Ship kills", 3, null, 30, 1.5, null),
                new MetricRow("Pod kills", 1, -1, 10, 0.5, null),
                new MetricRow("Jumps", 400, 20, 4000, 200, "Peak in 3 h"),
            ],
            SystemInfo.MetricRows(metrics, 1));
    }

    [Fact]
    public void Peak_now_from_80_percent_of_the_peak_else_a_peak_within_six_hours()
    {
        Assert.Equal("Peak now", SystemInfo.PeakHint(0.8, null));
        Assert.Equal("Peak in 6 h", SystemInfo.PeakHint(0.5, 6));
        Assert.Null(SystemInfo.PeakHint(0.5, 7));
        Assert.Null(SystemInfo.PeakHint(null, 0));
    }

    [Theory]
    [InlineData(40, "▲ 40")]
    [InlineData(-1, "▼ 1")]
    [InlineData(0, "±0")]
    [InlineData(null, "–")]
    public void The_change_shows_its_direction(int? change, string text) =>
        Assert.Equal(text, new MetricRow("NPC kills", 1, change, 1, null, null).ChangeText);

    [Theory]
    [InlineData(1.5, "2")]
    [InlineData(0.4, "0")]
    [InlineData(null, "–")]
    public void The_usual_is_rounded(double? typical, string text) =>
        Assert.Equal(text, new MetricRow("NPC kills", 1, null, 1, typical, null).UsualText);
}
