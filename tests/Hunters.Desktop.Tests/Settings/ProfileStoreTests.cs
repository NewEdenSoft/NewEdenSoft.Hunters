using Microsoft.Extensions.Logging.Abstractions;
using NewEdenSoft.Hunters.Desktop.Characters;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Settings;

namespace NewEdenSoft.Hunters.Desktop.Tests.Settings;

public sealed class ProfileStoreTests : IDisposable
{
    private static readonly Uri OtherServer = new("http://localhost:5000/");
    private static readonly Guid OtherAccount = Guid.Parse("3f2a6c1e-0000-4000-8000-000000000002");

    private readonly string _dir = Directory.CreateTempSubdirectory("hunters-profiles-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private JsonFileSettingsStore NewStore() => new(Path.Combine(_dir, "desktop-settings.json"), NullLogger<JsonFileSettingsStore>.Instance);

    [Fact]
    public void The_key_is_the_server_and_the_account()
    {
        Assert.Equal("https://hunters.example.com/|3f2a6c1e-0000-4000-8000-000000000001", ProfileStore.KeyOf(TestProfiles.Server, TestProfiles.Account));
    }

    [Fact]
    public void An_account_new_on_this_computer_starts_with_the_defaults()
    {
        var profile = TestProfiles.Open(NewStore());

        Assert.Equal(new ProfileSettings().Map, profile.Load().Map);
        Assert.Empty(profile.Load().Widgets);
        Assert.Empty(profile.Load().Characters);
    }

    [Fact]
    public void Two_accounts_on_one_server_keep_their_own_settings()
    {
        new MapPreferences(TestProfiles.Open(NewStore())).Layout = MapLayout.TopDown;
        new CharacterPreferences(TestProfiles.Open(NewStore())).SetTracked(1, false);

        var other = TestProfiles.Open(NewStore(), TestProfiles.Server, OtherAccount);

        Assert.Equal(MapLayout.Flat, new MapPreferences(other).Layout);
        Assert.True(new CharacterPreferences(other).IsTracked(1));
        Assert.Equal(MapLayout.TopDown, new MapPreferences(TestProfiles.Open(NewStore())).Layout);
        Assert.False(new CharacterPreferences(TestProfiles.Open(NewStore())).IsTracked(1));
    }

    [Fact]
    public void The_same_account_id_on_two_servers_keeps_two_profiles()
    {
        new MapPreferences(TestProfiles.Open(NewStore())).Filter = MapFilterId.Jumps;

        var other = TestProfiles.Open(NewStore(), OtherServer, TestProfiles.Account);

        Assert.Equal(MapFilterId.Security, new MapPreferences(other).Filter);
        Assert.Equal(MapFilterId.Jumps, new MapPreferences(TestProfiles.Open(NewStore())).Filter);
    }

    [Fact]
    public void Writing_one_profile_leaves_the_others_and_the_computer_settings_alone()
    {
        var store = NewStore();
        store.Update(s => s with { ThemeId = "amarr", StartInTray = true });
        new MapPreferences(TestProfiles.Open(store, TestProfiles.Server, OtherAccount)).MinNpc = 100;

        new MapPreferences(TestProfiles.Open(store)).MinNpc = 25;

        var settings = NewStore().Load();
        Assert.Equal("amarr", settings.ThemeId);
        Assert.True(settings.StartInTray);
        Assert.Equal(2, settings.Profiles.Count);
        Assert.Equal(100, settings.Profiles[ProfileStore.KeyOf(TestProfiles.Server, OtherAccount)].Map.MinNpc);
    }

    [Fact]
    public void It_cannot_be_read_before_the_session_opens_it_or_moved_to_another_profile()
    {
        var profile = new ProfileStore(NewStore());

        Assert.Throws<InvalidOperationException>(() => profile.Load());
        Assert.Throws<InvalidOperationException>(() => profile.Update(p => p));

        profile.Open("a");
        profile.Open("a");
        Assert.Throws<InvalidOperationException>(() => profile.Open("b"));
    }
}
