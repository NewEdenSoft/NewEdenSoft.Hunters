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
    public void Ending_the_session_saves_the_widget_layout()
    {
        var host = _services.GetRequiredService<SessionHost>();
        host.Start(NewSession());
        host.Widgets!.Find(ShellViewModel.MapWidgetId)!.Opacity = 0.5;

        host.End();

        Assert.Equal(0.5, _services.GetRequiredService<ISettingsStore>().Load().Widgets[ShellViewModel.MapWidgetId].Opacity);
    }

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

    private static HuntersSession NewSession(RecordingHandler? handler = null) => new(
        new HttpClient(handler ?? new RecordingHandler()),
        new Uri("https://hunters.example.com/"),
        new DesktopLoginResult("token", Guid.NewGuid(), IsNewAccount: false, CharacterId: 1, new AccountInfo(Guid.NewGuid(), null, false, [])));

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
