using NewEdenSoft.Hunters.Desktop.Settings;

namespace NewEdenSoft.Hunters.Desktop.Tests.Settings;

/// <summary>Profile stores opened as a signed-in session would open them.</summary>
public static class TestProfiles
{
    public static readonly Uri Server = new("https://hunters.example.com/");

    public static readonly Guid Account = Guid.Parse("3f2a6c1e-0000-4000-8000-000000000001");

    public static ProfileStore Open(ISettingsStore settings) => Open(settings, Server, Account);

    public static ProfileStore Open(ISettingsStore settings, Uri server, Guid account)
    {
        var profile = new ProfileStore(settings);
        profile.Open(ProfileStore.KeyOf(server, account));
        return profile;
    }
}
