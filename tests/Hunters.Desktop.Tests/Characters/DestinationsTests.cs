using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NewEdenKit.ESI.Services;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Client.Tracking;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Desktop.Characters;
using NewEdenSoft.Hunters.Desktop.Settings;
using NewEdenSoft.Hunters.Desktop.Tests.Settings;

namespace NewEdenSoft.Hunters.Desktop.Tests.Characters;

public sealed class DestinationsTests : IDisposable
{
    private const long Main = 1, Alt = 2, Other = 3, Expired = 4;
    private const int Tama = 30002813;

    private readonly string _dir = Directory.CreateTempSubdirectory("hunters-destinations-").FullName;
    private readonly FakeAutopilot _esi = new();
    private readonly FakeTokens _tokens = new();
    private readonly Destinations _destinations;

    public DestinationsTests()
    {
        var preferences = new CharacterPreferences(TestProfiles.Open(new JsonFileSettingsStore(Path.Combine(_dir, "settings.json"), NullLogger<JsonFileSettingsStore>.Instance)));
        _destinations = new Destinations(_esi, new CharacterTracking(new NoLocations(), preferences));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly AccountInfo Account = new(Guid.NewGuid(), Main, false,
    [
        new CharacterInfo(Other, "Bravo", false, CharacterStatus.Active, [], DateTimeOffset.UnixEpoch),
        new CharacterInfo(Main, "Main Pilot", true, CharacterStatus.Active, [], DateTimeOffset.UnixEpoch),
        new CharacterInfo(Alt, "Alpha", false, CharacterStatus.Active, [], DateTimeOffset.UnixEpoch),
        new CharacterInfo(Expired, "Expired Pilot", false, CharacterStatus.NeedsReauth, [], DateTimeOffset.UnixEpoch),
    ]);

    private static readonly Dictionary<long, TrackedCharacter> Tracker = new()
    {
        [Other] = new TrackedCharacter(Other, TrackingStatus.Tracking, IsOnline: true),
        [Main] = new TrackedCharacter(Main, TrackingStatus.Tracking, IsOnline: false),
        [Alt] = new TrackedCharacter(Alt, TrackingStatus.NeedsLogin),
    };

    [Fact]
    public void Usable_characters_are_online_first_then_the_main_then_by_name()
    {
        var usable = Destinations.Usable(Account, Tracker.GetValueOrDefault);

        Assert.Equal(["Bravo", "Main Pilot", "Alpha"], usable.Select(c => c.Name));
        Assert.True(usable[0].IsOnline);
    }

    [Fact]
    public void Tracked_leaves_out_the_untracked_and_those_needing_a_login()
    {
        var usable = Destinations.Usable(Account, Tracker.GetValueOrDefault);

        Assert.Equal([Other, Main], Destinations.Tracked(usable, Tracker.GetValueOrDefault).Select(c => c.Id));
    }

    [Fact]
    public void Outcome_names_who_it_was_set_for_then_each_failure()
    {
        Assert.Equal(new[] { "Destination Tama set for A, B." }, Destinations.Outcome("Tama", ["A", "B"], []).Lines);
        Assert.Equal(DestinationTone.Ok, Destinations.Outcome("Tama", ["A"], []).Tone);

        var failed = Destinations.Outcome("Tama", ["A"], [("B", "ESI is down.")]);
        Assert.Equal(DestinationTone.Danger, failed.Tone);
        Assert.Equal(["Set for A.", "B: ESI is down."], failed.Lines);
        Assert.Equal(["B: ESI is down."], Destinations.Outcome("Tama", [], [("B", "ESI is down.")]).Lines);
    }

    [Fact]
    public async Task Sets_it_for_each_character_and_renews_a_rejected_token_once()
    {
        _esi.Answers[(Main, 1)] = EsiCallStatus.Unauthorized;

        var outcome = await _destinations.SetAsync(_tokens, Tama, "Tama", [Character(Main, "Main Pilot"), Character(Other, "Bravo")]);

        Assert.Equal(DestinationTone.Ok, outcome.Tone);
        Assert.Equal([Main], _tokens.Invalidated);
        Assert.Equal(2, _esi.Calls.Count(c => c.Character == Main));
        Assert.All(_esi.Calls, c => Assert.Equal(Tama, c.System));
    }

    [Fact]
    public async Task Says_why_a_character_could_not_set_it()
    {
        _esi.Answers[(Main, 1)] = EsiCallStatus.Forbidden;
        _tokens.Fail.Add(Other);

        var outcome = await _destinations.SetAsync(_tokens, Tama, "Tama", [Character(Main, "Main Pilot"), Character(Other, "Bravo")]);

        Assert.Equal(DestinationTone.Danger, outcome.Tone);
        Assert.Equal(["Main Pilot: not allowed to set waypoints; log in with it again", "Bravo: Log in with Bravo again."], outcome.Lines);
    }

    [Fact]
    public void The_ESI_registration_has_the_user_interface_calls()
    {
        var services = new ServiceCollection();
        services.AddEveEsi(o =>
        {
            o.ApplicationName = "Tests";
            o.Contact = "https://example.com";
        });

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IUserInterface>());
    }

    private static DestinationCharacter Character(long id, string name) => new(id, name, id == Main, false);

    private sealed class FakeAutopilot : IAutopilotEsi
    {
        /// <summary>The answer to a character's n-th call (from 1); Ok otherwise.</summary>
        public Dictionary<(long Character, int Call), EsiCallStatus> Answers { get; } = [];

        public List<(long Character, int System)> Calls { get; } = [];

        public Task<EsiCall<bool>> SetDestinationAsync(int solarSystemId, string accessToken, CancellationToken cancellationToken)
        {
            var character = long.Parse(accessToken.Split(':')[0]);
            lock (Calls)
            {
                Calls.Add((character, solarSystemId));
                var status = Answers.GetValueOrDefault((character, Calls.Count(c => c.Character == character)), EsiCallStatus.Ok);
                return Task.FromResult(status == EsiCallStatus.Ok ? EsiCall<bool>.Ok(true) : new EsiCall<bool>(status, Error: $"ESI said {status}."));
            }
        }
    }

    private sealed class FakeTokens : ICharacterTokenProvider
    {
        public HashSet<long> Fail { get; } = [];

        public List<long> Invalidated { get; } = [];

        public ValueTask<string> GetAccessTokenAsync(long characterId, CancellationToken cancellationToken = default) =>
            Fail.Contains(characterId)
                ? throw new HuntersApiException("needs_reauth", "Log in with Bravo again.")
                : ValueTask.FromResult($"{characterId}:{Invalidated.Count}");

        public void InvalidateAccessToken(long characterId)
        {
            lock (Invalidated)
                Invalidated.Add(characterId);
        }
    }

    private sealed class NoLocations : ILocationEsi
    {
        public Task<EsiCall<bool>> GetOnlineAsync(long characterId, string accessToken, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<EsiCall<CharacterLocation>> GetLocationAsync(long characterId, string accessToken, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<EsiCall<CharacterShip>> GetShipAsync(long characterId, string accessToken, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
    }
}
