using NewEdenSoft.Hunters.Contracts.Hunting;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Theming;
using NewEdenSoft.Hunters.Desktop.Widgets.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class SystemProfilesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 8, 0, 0, TimeSpan.Zero);

    private static double[] Hours(params (int Hour, double Sightings)[] hours)
    {
        var all = new double[24];
        foreach (var (hour, sightings) in hours)
            all[hour] = sightings;
        return all;
    }

    private static HuntingPlaceProfile Profile(string id, double score, double[] hours, int pilots = 0) =>
        new(id, id, score, 0, 0, 0, pilots, Now.AddHours(-3), hours);

    private static readonly HuntingPlaceProfile Seen = Profile("seen", 9, Hours((3, 1)));
    private static readonly HuntingPlaceProfile Usual = Profile("now", 2, Hours((10, 1), (11, 4)));
    private static readonly HuntingPlaceProfile Peak = Profile("peak", 1, Hours((10, 5)));

    [Fact]
    public void Says_whether_the_profile_is_usually_here_at_this_hour_or_at_its_busiest()
    {
        Assert.Equal("seen", SystemInfo.ProfileStatus(Seen.Score, Seen.Hours, 10));
        Assert.Equal("now", SystemInfo.ProfileStatus(Usual.Score, Usual.Hours, 10));
        Assert.Equal("peak now", SystemInfo.ProfileStatus(Peak.Score, Peak.Hours, 10));
    }

    [Fact]
    public void Puts_peak_now_first_then_now_then_the_rest_by_score() =>
        Assert.Equal(["peak", "now", "seen"], SystemInfo.SortProfiles([Seen, Usual, Peak], 10).Select(p => p.Profile));

    [Theory]
    [InlineData(0.33, "now")]
    [InlineData(12, "12m")]
    [InlineData(180, "3h")]
    [InlineData(47 * 60, "47h")]
    [InlineData(5 * 24 * 60, "5d")]
    public void Counts_minutes_hours_then_days(double minutesAgo, string text) =>
        Assert.Equal(text, SystemInfo.TimeAgo(Now.AddMinutes(-minutesAgo), Now));

    [Theory]
    [InlineData(0, "3h")]
    [InlineData(1, "1 pilot · 3h")]
    [InlineData(4, "4 pilots · 3h")]
    public void A_row_tells_the_pilots_and_how_long_since_it_was_seen(int pilots, string seen)
    {
        var row = SystemProfileRow.Create(Profile("carriers", 1, Hours((10, 1)), pilots), new ProfileStyle("C", default), 10, Now);

        Assert.Equal(seen, row.Seen);
        Assert.True(row.IsLikely);
    }
}
