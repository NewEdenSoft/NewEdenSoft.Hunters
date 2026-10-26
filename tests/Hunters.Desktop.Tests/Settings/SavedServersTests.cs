using Microsoft.Extensions.Logging.Abstractions;
using NewEdenSoft.Hunters.Client;
using NewEdenSoft.Hunters.Desktop.Settings;

namespace NewEdenSoft.Hunters.Desktop.Tests.Settings;

public sealed class SavedServersTests
{
    private readonly InMemorySettingsStore _settings = new();

    private static Uri Server(string address) => ServerAddress.TryParse(address, out var server) ? server : throw new ArgumentException(address);

    [Fact]
    public void Saves_the_address_with_its_key_last_saved_first()
    {
        var servers = new SavedServers(_settings);

        servers.Save(Server("one.example.com"), "key-1");
        servers.Save(Server("localhost:5000"), "  ");

        Assert.Equal(
            [new SavedServer("http://localhost:5000/"), new SavedServer("https://one.example.com/", "key-1")],
            servers.All);
    }

    [Fact]
    public void Saving_a_server_again_replaces_it_and_puts_it_first()
    {
        var servers = new SavedServers(_settings);
        servers.Save(Server("one.example.com"), "old");
        servers.Save(Server("two.example.com"), null);

        servers.Save(Server("https://ONE.example.com/"), "new");

        Assert.Equal(["one.example.com", "two.example.com"], servers.All.Select(s => s.Label));
        Assert.Equal("new", servers.All[0].AccessKey);
    }

    [Fact]
    public void Forgets_a_server()
    {
        var servers = new SavedServers(_settings);
        var changes = 0;
        servers.Changed += (_, _) => changes++;
        var one = servers.Save(Server("one.example.com"), "key");
        servers.Save(Server("two.example.com"), null);

        servers.Remove(one);

        Assert.Equal(["two.example.com"], servers.All.Select(s => s.Label));
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Labels_show_the_scheme_only_when_it_is_not_https()
    {
        Assert.Equal("hunters.example.com", new SavedServer("https://hunters.example.com/").Label);
        Assert.Equal("http://localhost:5000", new SavedServer("http://localhost:5000/").Label);
        Assert.Equal(" · key", new SavedServer("https://a/", "k").KeyTag);
        Assert.Equal("", new SavedServer("https://a/").KeyTag);
    }

    [Fact]
    public void Are_kept_in_the_settings_file_without_the_display_properties()
    {
        var dir = Directory.CreateTempSubdirectory("hunters-servers-").FullName;
        try
        {
            var path = Path.Combine(dir, "settings.json");
            new SavedServers(new JsonFileSettingsStore(path, NullLogger<JsonFileSettingsStore>.Instance)).Save(Server("one.example.com"), "key-1");

            var json = File.ReadAllText(path);
            Assert.DoesNotContain("Label", json);
            Assert.Equal(
                [new SavedServer("https://one.example.com/", "key-1")],
                new SavedServers(new JsonFileSettingsStore(path, NullLogger<JsonFileSettingsStore>.Instance)).All);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
