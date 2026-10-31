using NewEdenSoft.Hunters.Contracts.Hunting;
using NewEdenSoft.Hunters.Desktop.Theming;
using NewEdenSoft.Hunters.Desktop.ViewModels.Pages;

namespace NewEdenSoft.Hunters.Desktop.Tests.Account;

public sealed class InfoProfileTests
{
    private static readonly ProfileStyle Style = new("CV", Rgba.Parse("#ffffff"));

    [Fact]
    public void A_profile_shows_what_it_is_and_what_counts_as_seeing_it()
    {
        var profile = InfoProfile.Create(
            new HuntingProfileInfo("carriers", "Carriers", "Carriers ratting in null-sec.", ["A carrier lost", "A CRAB beacon killed"]),
            Style);

        Assert.Equal("Carriers", profile.Name);
        Assert.True(profile.HasDescription);
        Assert.Equal(["A carrier lost", "A CRAB beacon killed"], profile.Evidence);
        Assert.True(profile.HasEvidence);
    }

    [Fact]
    public void A_profile_without_a_description_or_evidence_shows_neither()
    {
        var profile = InfoProfile.Create(new HuntingProfileInfo("titans", "Titans"), Style);

        Assert.False(profile.HasDescription);
        Assert.False(profile.HasEvidence);
    }
}
