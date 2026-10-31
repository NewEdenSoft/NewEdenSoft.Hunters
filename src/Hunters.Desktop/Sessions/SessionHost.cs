using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Desktop.Settings;
using NewEdenSoft.Hunters.Desktop.ViewModels;
using NewEdenSoft.Hunters.Desktop.ViewModels.Pages;
using NewEdenSoft.Hunters.Desktop.Widgets;

namespace NewEdenSoft.Hunters.Desktop.Sessions;

/// <summary>
/// The signed-in part of the app: one dependency injection scope per session, holding the shell, its
/// pages and widgets, the map's state and the character tracking. Nothing of one session is left for the
/// next: when it ends (log out, revoked by the server, or the app quits) the layout is saved, the
/// overlays close, background work stops and the scope is disposed. UI thread only.
/// </summary>
public sealed class SessionHost(IServiceScopeFactory scopes)
{
    /// <summary>How long quitting waits for the server to revoke the session.</summary>
    public static readonly TimeSpan RevokeTimeout = TimeSpan.FromSeconds(3);

    private IServiceScope? _scope;

    /// <summary>The signed-in session; null while signed out.</summary>
    public HuntersSession? Session { get; private set; }

    /// <summary>The main window's content while signed in.</summary>
    public ShellViewModel? Shell { get; private set; }

    /// <summary>The session's widgets and their overlays; null while signed out.</summary>
    public WidgetManager? Widgets { get; private set; }

    public bool IsSignedIn => Session is not null;

    /// <summary>The session's services; null while signed out.</summary>
    public IServiceProvider? Services => _scope?.ServiceProvider;

    /// <summary>A session started or ended; read <see cref="Shell"/> and <see cref="Widgets"/> again.</summary>
    public event EventHandler? Changed;

    /// <summary>Builds the signed-in app for a new session and restores its saved overlays.</summary>
    public void Start(HuntersSession session)
    {
        End();

        var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        // First, so everything built below reads this account's settings on this server.
        services.GetRequiredService<ProfileStore>().Open(ProfileStore.KeyOf(session));
        // The shell creates the pages and widgets, so they all see the session start below.
        var shell = services.GetRequiredService<ShellViewModel>();
        var widgets = services.GetRequiredService<WidgetManager>();

        _scope = scope;
        Session = session;
        Shell = shell;
        Widgets = widgets;
        session.Ended += OnSessionEnded;

        services.GetRequiredService<AccountPageViewModel>().Start(session);
        widgets.RestoreOverlays();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Saves the layout, closes the overlays, stops the session's work and returns to the login screen.</summary>
    public void End()
    {
        if (_scope is not { } scope)
            return;

        Session!.Ended -= OnSessionEnded;
        Widgets!.Shutdown();
        Shell!.IsWindowVisible = false;
        scope.ServiceProvider.GetRequiredService<AccountPageViewModel>().Stop();

        _scope = null;
        Session = null;
        Shell = null;
        Widgets = null;
        Changed?.Invoke(this, EventArgs.Empty);
        scope.Dispose();
    }

    /// <summary>Ends the session and revokes it on the server (best effort, waiting at most <see cref="RevokeTimeout"/>).</summary>
    public void Quit()
    {
        var session = Session;
        End();
        if (session is null || session.IsEnded)
            return;

        using var cancel = new CancellationTokenSource(RevokeTimeout);
        try
        {
            // On the thread pool, so the request never waits for the UI thread that is waiting for it.
            Task.Run(() => session.LogOutAsync(cancel.Token)).Wait(RevokeTimeout);
        }
        catch (AggregateException)
        {
            // Unreachable or too slow: the session expires on the server by itself.
        }
    }

    // Raised by background work too (a token request finding the session revoked).
    private void OnSessionEnded(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (ReferenceEquals(sender, Session))
            End();
    });
}
