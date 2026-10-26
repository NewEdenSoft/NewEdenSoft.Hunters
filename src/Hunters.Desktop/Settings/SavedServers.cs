using System.Text.Json.Serialization;

namespace NewEdenSoft.Hunters.Desktop.Settings;

/// <summary>A server saved on the login screen: its address and, when one was entered, its access key.</summary>
/// <param name="Address">The server's base URL, as <see cref="Client.ServerAddress.TryParse"/> gives it ("https://hunters.example.com/").</param>
/// <param name="AccessKey">Stored as plain text in the settings file; null when none was entered.</param>
public sealed record SavedServer(string Address, string? AccessKey = null)
{
    /// <summary>The address as typed: "hunters.example.com", or with its scheme when it isn't https ("http://localhost:5000").</summary>
    [JsonIgnore]
    public string Label
    {
        get
        {
            var text = Address.TrimEnd('/');
            return text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? text["https://".Length..] : text;
        }
    }

    [JsonIgnore]
    public bool HasAccessKey => !string.IsNullOrEmpty(AccessKey);

    /// <summary>" · key" after the label when an access key is saved, else empty.</summary>
    [JsonIgnore]
    public string KeyTag => HasAccessKey ? " · key" : "";
}

/// <summary>
/// The servers saved on the login screen, last saved first, in the desktop settings. Nothing is saved
/// until the user presses Save; sessions are never saved (docs/decisions.md, D20).
/// </summary>
public sealed class SavedServers(ISettingsStore settings)
{
    public event EventHandler? Changed;

    public IReadOnlyList<SavedServer> All => settings.Load().Servers;

    /// <summary>Saves the server with the key (none when empty), replacing an earlier save of it, and puts it first.</summary>
    public SavedServer Save(Uri server, string? accessKey)
    {
        var saved = new SavedServer(server.ToString(), string.IsNullOrWhiteSpace(accessKey) ? null : accessKey.Trim());
        settings.Update(s => s with { Servers = [saved, .. s.Servers.Where(o => !Same(o, saved))] });
        Changed?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    public void Remove(SavedServer server)
    {
        settings.Update(s => s with { Servers = [.. s.Servers.Where(o => !Same(o, server))] });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool Same(SavedServer a, SavedServer b) => string.Equals(a.Address, b.Address, StringComparison.OrdinalIgnoreCase);
}
