using NewEdenSoft.Hunters.Contracts.Hunting;
using NewEdenSoft.Hunters.Contracts.Map;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Theming;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

/// <summary>The same cases as <c>web/hunters-web/src/map/hunting.test.ts</c>.</summary>
public sealed class HuntingLayerTests
{
    private static readonly MapModel Model = MapModel.Build(new MapPack(
        1,
        [new MapRegion(1, "Region")],
        [.. new[] { 1, 2, 3, 4 }.Select(id => new MapSystem(id, $"S{id}", 1, 1, -0.5, new MapPoint(0, 0), new MapPoint(0, 0)))],
        []));

    /// <summary>Carriers: strong in S1 at 19:00, weaker in S2 at 19:00, in S3 only at 03:00. CRAB: S2 at 19:00. S99 is not on the map.</summary>
    private static readonly HuntingOverview Overview = new(1, DateTimeOffset.UnixEpoch,
    [
        Profile("carriers", [1, 2, 3, 99], [8, 2, 5, 9], [At((19, 4)), At((19, 1), (20, 1)), At((3, 2)), At((19, 1))]),
        Profile("crab", [2], [3], [At((19, 1))]),
    ]);

    [Fact]
    public void Likely_now_is_the_score_times_this_hours_share_of_the_sightings()
    {
        Assert.Equal(6, HuntingLayer.LikelyNow(8, At((19, 3), (20, 1)), 19));
        Assert.Equal(0, HuntingLayer.LikelyNow(8, At((19, 3), (20, 1)), 3));
        Assert.Equal(0, HuntingLayer.LikelyNow(8, At(), 19));
    }

    [Fact]
    public void Glows_by_the_strongest_shown_profile_likely_now_relative_to_its_strongest_system()
    {
        var layer = HuntingLayer.Build(Model, Overview, ["carriers"], 19, Style);

        // Likely now: S1 8, S2 1 (half its sightings are at 19:00), S3 0.
        Assert.Equal([1, (float)Math.Sqrt(1 / 8.0), 0, 0], layer.Glow);
        Assert.Equal(1, HuntingLayer.Build(Model, Overview, ["carriers", "crab"], 19, Style).Glow[1]);
    }

    [Fact]
    public void Badges_every_shown_profile_seen_faint_when_not_usually_there_at_this_hour()
    {
        var layer = HuntingLayer.Build(Model, Overview, ["carriers", "crab"], 19, Style);

        var bySystem = layer.Badges.ToDictionary(g => Model.Names[g.SystemIndex], g => g.Badges.Select(b => b.Code + (b.Faint ? " faint" : "")));
        Assert.Equal(["CA"], bySystem["S1"]);
        Assert.Equal(["CA", "CR"], bySystem["S2"]);
        Assert.Equal(["CA faint"], bySystem["S3"]);
        Assert.Equal(3, bySystem.Count);
    }

    [Fact]
    public void Shows_nothing_for_profiles_not_chosen_or_not_in_the_overview()
    {
        var layer = HuntingLayer.Build(Model, Overview, ["ishtars"], 19, Style);

        Assert.Empty(layer.Badges);
        Assert.All(layer.Glow, g => Assert.Equal(0, g));
    }

    [Fact]
    public void Lists_the_shown_profiles_at_a_system_likely_now_first_then_by_score()
    {
        var bySystem = HuntingLayer.ProfilesBySystem(Model, Overview, ["carriers", "crab"], 19, Style, id => id.ToUpperInvariant());

        // S2: carriers likely 1 (score 2), CRAB likely 3 (score 3).
        Assert.Equal(["CRAB 3 1 peak now", "CARRIERS 1 1 peak now"], bySystem[1].Select(p => $"{p.Name} {p.Likely} {p.PeakNow} {p.When}"));
        // S3: carriers seen, but only at 03:00.
        Assert.Equal(["CARRIERS 0 0 seen"], bySystem[2].Select(p => $"{p.Name} {p.Likely} {p.PeakNow} {p.When}"));
        Assert.False(bySystem.ContainsKey(3));
        Assert.Equal([1], HuntingLayer.ProfilesBySystem(Model, Overview, ["crab"], 19, Style, id => id).Keys);
    }

    [Fact]
    public void Profiles_have_their_style_from_the_theme_or_one_from_their_name()
    {
        var catalog = ThemeCatalog.LoadEmbedded();

        Assert.Equal("CV", catalog.ProfileStyle("carriers", "Carriers").Code);
        Assert.NotNull(ProfileIcons.PathOf(catalog.ProfileStyle("smartbomb-camps", "Smartbomb camps").Icon));
        var unknown = catalog.ProfileStyle("titans", "Titans");
        Assert.Equal("TI", unknown.Code);
        Assert.Equal(catalog.Fixed["hunt-glow"], unknown.Colour);
    }

    private static ProfileStyle Style(string profile) => new(profile[..2].ToUpperInvariant(), Rgba.Parse("#ffffff"));

    private static double[] At(params (int Hour, double Weight)[] weights)
    {
        var hours = new double[24];
        foreach (var (hour, weight) in weights)
            hours[hour] = weight;
        return hours;
    }

    private static HuntingProfileSystems Profile(string profile, int[] ids, double[] score, double[][] hours) =>
        new(profile, ids, score, [], [], [], hours);
}
