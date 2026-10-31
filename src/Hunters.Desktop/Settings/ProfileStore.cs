using NewEdenSoft.Hunters.Client.Sessions;

namespace NewEdenSoft.Hunters.Desktop.Settings;

/// <summary>
/// The signed-in account's settings on its server (<see cref="DesktopSettings.Profiles"/>), in the desktop
/// settings file. One per session: <see cref="Sessions.SessionHost"/> opens it for the session before
/// anything reads it, so each session reads and writes only its own profile, and a write that comes late
/// (the widget layout's delayed save) still goes to the profile it belongs to.
/// </summary>
public sealed class ProfileStore(ISettingsStore settings)
{
    /// <summary>The profile's key: <see cref="Open"/> sets it once.</summary>
    public string? Key { get; private set; }

    /// <summary>The key of an account on a server: the server's address and the account's id, "https://hunters.example.com/|3f2a…".</summary>
    public static string KeyOf(Uri server, Guid accountId) => $"{server}|{accountId:D}";

    public static string KeyOf(HuntersSession session) => KeyOf(session.Server, session.Account.Id);

    /// <summary>Chooses the profile this store reads and writes.</summary>
    public void Open(string key)
    {
        if (Key is not null && Key != key)
            throw new InvalidOperationException($"The profile store is already open for {Key}.");
        Key = key;
    }

    /// <summary>The profile's settings; the defaults for an account not seen on this computer before.</summary>
    public ProfileSettings Load() => settings.Load().Profiles.GetValueOrDefault(RequireKey()) ?? new ProfileSettings();

    public void Update(Func<ProfileSettings, ProfileSettings> change)
    {
        var key = RequireKey();
        settings.Update(s => s with
        {
            Profiles = new Dictionary<string, ProfileSettings>(s.Profiles)
            {
                [key] = change(s.Profiles.GetValueOrDefault(key) ?? new ProfileSettings()),
            },
        });
    }

    private string RequireKey() => Key ?? throw new InvalidOperationException("The profile store is read before the session opened it.");
}
