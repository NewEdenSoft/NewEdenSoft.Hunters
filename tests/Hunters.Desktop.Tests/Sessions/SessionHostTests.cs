using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Desktop.Characters;
using NewEdenSoft.Hunters.Desktop.Images;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Sessions;
using NewEdenSoft.Hunters.Desktop.Settings;
using NewEdenSoft.Hunters.Desktop.ViewModels;
using NewEdenSoft.Hunters.Desktop.ViewModels.Pages;
using NewEdenSoft.Hunters.Desktop.Widgets.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Sessions;

public sealed class SessionHostTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hunters-sessions-").FullName;
    private readonly ServiceProvider _services;

    public SessionHostTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Hunters:Desktop:SettingsPath"] = Path.Combine(_dir, "desktop-settings.json") })
            .Build();
        var services = new ServiceCollection().AddLogging();
        services.AddDesktopServices(configuration);
        // Validation catches an app-wide service holding on to one session's services.
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public void Dispose()
    {
        _services.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Session_services_are_new_for_each_session_and_app_services_are_shared()
    {
        using var first = _services.CreateScope();
        using var second = _services.CreateScope();
        first.ServiceProvider.GetRequiredService<ProfileStore>().Open("a");
        second.ServiceProvider.GetRequiredService<ProfileStore>().Open("b");

        Assert.NotSame(first.ServiceProvider.GetRequiredService<MapPreferences>(), second.ServiceProvider.GetRequiredService<MapPreferences>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<MapFocus>(), second.ServiceProvider.GetRequiredService<MapFocus>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<CharacterPreferences>(), second.ServiceProvider.GetRequiredService<CharacterPreferences>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<CharacterTracking>(), second.ServiceProvider.GetRequiredService<CharacterTracking>());

        Assert.Same(first.ServiceProvider.GetRequiredService<SavedServers>(), second.ServiceProvider.GetRequiredService<SavedServers>());
        Assert.Same(first.ServiceProvider.GetRequiredService<PortraitCache>(), second.ServiceProvider.GetRequiredService<PortraitCache>());
    }

    [Fact]
    public void Session_services_are_not_there_while_signed_out()
    {
        Assert.Throws<InvalidOperationException>(() => _services.GetRequiredService<MapPreferences>());
        Assert.Throws<InvalidOperationException>(() => _services.GetRequiredService<AccountPageViewModel>());
    }

    [Fact]
    public void Signed_out_the_window_shows_the_login_screen_and_closing_it_quits()
    {
        var window = _services.GetRequiredService<MainWindowViewModel>();

        Assert.False(window.IsSignedIn);
        Assert.Null(window.Shell);
        Assert.True(window.QuitsOnClose);
    }

    [Fact]
    public void A_session_shows_the_shell_and_closing_the_window_hides_it_to_the_tray()
    {
        var host = _services.GetRequiredService<SessionHost>();
        var window = _services.GetRequiredService<MainWindowViewModel>();
        var session = NewSession();

        host.Start(session);

        Assert.True(host.IsSignedIn);
        Assert.Same(session, host.Session);
        Assert.NotNull(host.Widgets);
        Assert.Same(host.Shell, window.Shell);
        Assert.True(window.IsSignedIn);
        Assert.False(window.QuitsOnClose);
        Assert.Same(session, Account(host.Shell!).Session);
    }

    [Fact]
    public void Ending_the_session_returns_to_the_login_screen_and_drops_its_services()
    {
        var host = _services.GetRequiredService<SessionHost>();
        var window = _services.GetRequiredService<MainWindowViewModel>();
        host.Start(NewSession());
        var shell = host.Shell!;
        var account = Account(shell);
        var changes = 0;
        host.Changed += (_, _) => changes++;

        host.End();

        Assert.Equal(1, changes);
        Assert.False(host.IsSignedIn);
        Assert.Null(host.Shell);
        Assert.Null(host.Widgets);
        Assert.Null(window.Shell);
        Assert.True(window.QuitsOnClose);
        // The widgets read the session from the account page; they see it end and stop their work.
        Assert.Null(account.Session);
        Assert.False(shell.IsWindowVisible);
    }

    [Fact]
    public void The_next_session_starts_with_new_widgets()
    {
        var host = _services.GetRequiredService<SessionHost>();
        host.Start(NewSession());
        var first = host.Shell!;
        host.End();

        host.Start(NewSession());

        Assert.NotSame(first, host.Shell);
        Assert.NotSame(first.WidgetPages[0].Widget.Content, host.Shell!.WidgetPages[0].Widget.Content);
    }

    [Fact]
    public void Ending_the_session_writes_a_layout_change_still_waiting_to_be_saved()
    {
        var host = _services.GetRequiredService<SessionHost>();
        var session = NewSession();
        host.Start(session);
        host.Widgets!.Find(ShellViewModel.MapWidgetId)!.Opacity = 0.5;
        // Saved 500 ms after the last change; not yet.
        Assert.False(Profile(session).Widgets.ContainsKey(ShellViewModel.MapWidgetId));

        host.End();

        Assert.Equal(0.5, Profile(session).Widgets[ShellViewModel.MapWidgetId].Opacity);
    }

    [Fact]
    public void The_layout_and_the_feature_settings_come_back_at_the_next_login()
    {
        var host = _services.GetRequiredService<SessionHost>();
        host.Start(NewSession());
        var marker = CharacterMarker.Icons[1];
        Change(host, marker);
        host.End();

        host.Start(NewSession());

        var map = host.Widgets!.Find(ShellViewModel.MapWidgetId)!;
        Assert.Equal((0.4, true, true, 100, 200, 640.0, 480.0), (map.Opacity, map.Locked, map.ClickThrough, map.X, map.Y, map.Width, map.Height));
        var preferences = host.Services!.GetRequiredService<MapPreferences>();
        Assert.Equal(MapLayout.TopDown, preferences.Layout);
        Assert.Equal(MapFilterId.NpcRising, preferences.Filter);
        Assert.Equal(250, preferences.MinNpc);
        Assert.Equal(["carriers"], preferences.HuntingProfiles);
        Assert.Equal(new MapBehaviour(DoubleClickFocus: false, MinJumps: 10), preferences.Behaviour);
        Assert.Equal(new JumpRangeSetting(Enabled: true, Ly: 7), preferences.JumpRange);
        Assert.Equal(new NearbySetting(Jumps: 10, ShowQuiet: true), preferences.Nearby);
        var mapWidget = host.Services!.GetRequiredService<MapWidgetViewModel>();
        Assert.Equal((MapFilterId.NpcRising, 250), (mapWidget.FilterId, mapWidget.MinNpc));
        var characters = host.Services!.GetRequiredService<CharacterPreferences>();
        Assert.Equal(marker, characters.GetMarker(7, "Seven"));
        Assert.False(characters.IsTracked(7));
    }

    [Fact]
    public void Another_account_on_the_same_server_starts_with_its_own_settings()
    {
        var host = _services.GetRequiredService<SessionHost>();
        host.Start(NewSession());
        Change(host, CharacterMarker.Icons[1]);
        host.End();

        host.Start(NewSession(accountId: Guid.NewGuid()));

        var map = host.Widgets!.Find(ShellViewModel.MapWidgetId)!;
        Assert.Equal((0.8, false, false, (int?)null), (map.Opacity, map.Locked, map.ClickThrough, map.X));
        Assert.Equal(new MapSettings().Layout, host.Services!.GetRequiredService<MapPreferences>().Layout);
        Assert.Equal(MapFilterId.Security, host.Services!.GetRequiredService<MapWidgetViewModel>().FilterId);
        Assert.True(host.Services!.GetRequiredService<CharacterPreferences>().IsTracked(7));
    }

    /// <summary>Changes the map widget's layout and every feature setting, as the user would in the session.</summary>
    private static void Change(SessionHost host, CharacterMarker marker)
    {
        var map = host.Widgets!.Find(ShellViewModel.MapWidgetId)!;
        map.Opacity = 0.4;
        map.Locked = true;
        map.ClickThrough = true;
        (map.X, map.Y, map.Width, map.Height) = (100, 200, 640, 480);

        var services = host.Services!;
        var mapWidget = services.GetRequiredService<MapWidgetViewModel>();
        mapWidget.FilterId = MapFilterId.NpcRising;
        mapWidget.MinNpc = 250;
        var preferences = services.GetRequiredService<MapPreferences>();
        preferences.Layout = MapLayout.TopDown;
        preferences.ToggleHuntingProfile("carriers");
        preferences.Behaviour = new MapBehaviour(DoubleClickFocus: false, MinJumps: 10);
        preferences.JumpRange = new JumpRangeSetting(Enabled: true, Ly: 7);
        preferences.Nearby = new NearbySetting(Jumps: 10, ShowQuiet: true);
        var characters = services.GetRequiredService<CharacterPreferences>();
        characters.SetMarker(7, marker);
        characters.SetTracked(7, false);
    }

    private ProfileSettings Profile(HuntersSession session) =>
        _services.GetRequiredService<ISettingsStore>().Load().Profiles.GetValueOrDefault(ProfileStore.KeyOf(session)) ?? new ProfileSettings();

    [Fact]
    public void Quitting_revokes_the_session_on_the_server()
    {
        var host = _services.GetRequiredService<SessionHost>();
        var handler = new RecordingHandler();
        var session = NewSession(handler);
        host.Start(session);

        host.Quit();

        Assert.False(host.IsSignedIn);
        Assert.True(session.IsEnded);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Delete && r.Path == AccountPaths.Session(session.SessionId));
    }

    [Fact]
    public void Quitting_while_signed_out_does_nothing()
    {
        var host = _services.GetRequiredService<SessionHost>();

        host.Quit();

        Assert.False(host.IsSignedIn);
    }

    private static AccountPageViewModel Account(ShellViewModel shell) => shell.SystemPages.OfType<AccountPageViewModel>().Single();

    private static readonly Guid AccountId = Guid.Parse("3f2a6c1e-0000-4000-8000-000000000001");

    private static HuntersSession NewSession(RecordingHandler? handler = null, Guid? accountId = null) => new(
        new HttpClient(handler ?? new RecordingHandler()),
        new Uri("https://hunters.example.com/"),
        new DesktopLoginResult("token", Guid.NewGuid(), IsNewAccount: false, CharacterId: 1, new AccountInfo(accountId ?? AccountId, null, false, [])));

    /// <summary>Answers every request with 204 and remembers it.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
                Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}
