using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Client.Tracking;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Desktop.Characters;
using NewEdenSoft.Hunters.Desktop.Settings;
using NewEdenSoft.Hunters.Desktop.Tests.Settings;

namespace NewEdenSoft.Hunters.Desktop.Tests.Characters;

public sealed class CharacterTrackingTests : IDisposable
{
    private const long Main = 90000001, Alt = 90000002, Expired = 90000003;

    private readonly string _dir = Directory.CreateTempSubdirectory("hunters-tracking-").FullName;
    private readonly CharacterPreferences _preferences;

    public CharacterTrackingTests() =>
        _preferences = new CharacterPreferences(TestProfiles.Open(new JsonFileSettingsStore(Path.Combine(_dir, "settings.json"), NullLogger<JsonFileSettingsStore>.Instance)));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly AccountInfo Account = new(Guid.NewGuid(), Main, false,
    [
        new CharacterInfo(Main, "Main Pilot", true, CharacterStatus.Active, [], DateTimeOffset.UnixEpoch),
        new CharacterInfo(Alt, "Alt Pilot", false, CharacterStatus.Active, [], DateTimeOffset.UnixEpoch),
        new CharacterInfo(Expired, "Expired Pilot", false, CharacterStatus.NeedsReauth, [], DateTimeOffset.UnixEpoch),
    ]);

    [Fact]
    public void Characters_are_tracked_unless_switched_off()
    {
        var changes = new List<long>();
        _preferences.TrackingChanged += (_, id) => changes.Add(id);

        Assert.True(_preferences.IsTracked(Alt));
        _preferences.SetTracked(Alt, false);
        _preferences.SetTracked(Alt, false);

        Assert.False(_preferences.IsTracked(Alt));
        Assert.Equal([Alt], changes);
    }

    [Fact]
    public void Only_usable_characters_that_are_switched_on_are_tracked()
    {
        _preferences.SetTracked(Alt, false);

        Assert.Equal([Main], CharacterTracking.Trackable(Account, _preferences));
    }

    [Fact]
    public async Task A_session_starts_tracking_and_ending_it_drops_its_characters()
    {
        var tracking = new CharacterTracking(new SilentEsi(), _preferences);
        var changes = new ConcurrentQueue<TrackedCharacterChangedEventArgs>();
        tracking.Changed += (_, e) => changes.Enqueue(e);
        var session = new HuntersSession(new HttpClient(new UnreachableServer()), new Uri("https://hunters.example"),
            new DesktopLoginResult("token", Guid.NewGuid(), false, Main, Account));

        tracking.SetSession(session);
        for (var i = 0; i < 300 && tracking.Get(Main) is null; i++)
            await Task.Delay(10);

        Assert.NotNull(tracking.Get(Main));
        Assert.NotNull(tracking.Get(Alt));
        Assert.Null(tracking.Get(Expired));

        // Switching a character off stops it.
        _preferences.SetTracked(Alt, false);
        Assert.Null(tracking.Get(Alt));

        tracking.SetSession(null);
        Assert.Null(tracking.Get(Main));
        Assert.Contains(changes, c => c.CharacterId == Main && c.Current is null);
    }

    [Fact]
    public async Task Names_are_looked_up_once_and_failures_are_retried_later()
    {
        var source = new FakeNames { [30000142] = "Jita", [12005] = "Ishtar" };
        var names = new EveNames(source);

        Assert.True(await names.ResolveAsync([30000142, 12005]));
        Assert.True(await names.ResolveAsync([30000142]));

        Assert.Equal("Jita", names.Get(30000142));
        Assert.Equal("Ishtar", names.Get(12005));
        Assert.Equal(1, source.Requests);

        source.Fail = true;
        Assert.False(await names.ResolveAsync([30000144]));
        Assert.Null(names.Get(30000144));
    }

    private sealed class UnreachableServer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No server in tests.");
    }

    private sealed class SilentEsi : ILocationEsi
    {
        public Task<EsiCall<bool>> GetOnlineAsync(long characterId, string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult(EsiCall<bool>.Ok(false));

        public Task<EsiCall<CharacterLocation>> GetLocationAsync(long characterId, string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult(EsiCall<CharacterLocation>.Ok(new CharacterLocation(30000142, null, null)));

        public Task<EsiCall<CharacterShip>> GetShipAsync(long characterId, string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult(EsiCall<CharacterShip>.Ok(new CharacterShip(670, "Capsule", 1)));
    }

    private sealed class FakeNames : Dictionary<long, string>, IEveNameSource
    {
        public int Requests { get; private set; }

        public bool Fail { get; set; }

        public Task<IReadOnlyDictionary<long, string>> GetNamesAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken)
        {
            Requests++;
            if (Fail)
                throw new HttpRequestException("ESI is down.");
            return Task.FromResult<IReadOnlyDictionary<long, string>>(ids.Where(ContainsKey).ToDictionary(id => id, id => this[id]));
        }
    }
}
