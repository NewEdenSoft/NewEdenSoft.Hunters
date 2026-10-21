using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Client.Tracking;
using NewEdenSoft.Hunters.Contracts.Auth;

namespace NewEdenSoft.Hunters.Desktop.Tests.Tracking;

public sealed class CharacterTrackerTests : IAsyncDisposable
{
    private const long Pilot = 90000001;
    private static readonly CharacterLocation Jita = new(30000142, 60003760, null);
    private static readonly CharacterLocation Perimeter = new(30000144, null, null);
    private static readonly CharacterShip Ishtar = new(12005, "Ishtar", 1000000001);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeEsi _esi = new();
    private readonly FakeTokens _tokens = new();
    private readonly ConcurrentQueue<TrackedCharacterChangedEventArgs> _changes = new();
    private readonly CharacterTracker _tracker;

    public CharacterTrackerTests()
    {
        _tracker = new CharacterTracker(_esi, _tokens, _time);
        _tracker.Changed += (_, e) => _changes.Enqueue(e);
    }

    public ValueTask DisposeAsync() => _tracker.DisposeAsync();

    [Fact]
    public async Task Online_characters_are_followed_every_five_seconds()
    {
        _esi.Set(Pilot, online: true, Jita, Ishtar);

        _tracker.Track([Pilot]);
        var first = await WaitForAsync(() => _tracker.Get(Pilot) is { Status: TrackingStatus.Tracking } s ? s : null);

        Assert.True(first.IsOnline);
        Assert.Equal(Jita, first.Location);
        Assert.Equal(Ishtar, first.Ship);
        Assert.Contains(_changes, c => c.SystemChanged && c.Current?.Location == Jita);

        _esi.Set(Pilot, online: true, Perimeter, Ishtar);
        await AdvanceUntilAsync(CharacterTracker.LocationInterval, () => _tracker.Get(Pilot)?.Location == Perimeter);

        Assert.Equal(2, _esi.LocationCalls(Pilot));
        Assert.Contains(_changes, c => c.SystemChanged && c.Previous?.Location == Jita && c.Current?.Location == Perimeter);
    }

    [Fact]
    public async Task Unchanged_polls_raise_no_event()
    {
        _esi.Set(Pilot, online: true, Jita, Ishtar);
        _tracker.Track([Pilot]);
        await WaitForAsync(() => _tracker.Get(Pilot) is { Status: TrackingStatus.Tracking } s ? s : null);
        var before = _changes.Count;

        await AdvanceUntilAsync(CharacterTracker.LocationInterval, () => _esi.LocationCalls(Pilot) == 2);
        await AdvanceUntilAsync(CharacterTracker.LocationInterval, () => _esi.LocationCalls(Pilot) == 3);

        Assert.Equal(before, _changes.Count);
    }

    [Fact]
    public async Task Offline_characters_are_read_once_then_only_checked_for_coming_online()
    {
        _esi.Set(Pilot, online: false, Jita, Ishtar);

        _tracker.Track([Pilot]);
        var first = await WaitForAsync(() => _tracker.Get(Pilot) is { Status: TrackingStatus.Tracking } s ? s : null);
        Assert.False(first.IsOnline);
        Assert.Equal(Jita, first.Location);

        // Within the online cache time, nothing is polled.
        _time.Advance(CharacterTracker.LocationInterval * 3);
        await Task.Delay(50);
        Assert.Equal(1, _esi.OnlineCalls(Pilot));
        Assert.Equal(1, _esi.LocationCalls(Pilot));

        // Logged in and moved: seen at the next online check.
        _esi.Set(Pilot, online: true, Perimeter, Ishtar);
        await AdvanceUntilAsync(CharacterTracker.OnlineInterval, () => _tracker.Get(Pilot)?.Location == Perimeter);
        Assert.True(_tracker.Get(Pilot)!.IsOnline);
        Assert.Equal(2, _esi.OnlineCalls(Pilot));
    }

    [Fact]
    public async Task A_character_that_needs_a_login_stops_until_tracked_again()
    {
        _tokens.Fail(Pilot, new HuntersApiException(AuthErrorCodes.NeedsReauth, "Log in again."));

        _tracker.Track([Pilot]);
        await WaitForAsync(() => _tracker.Get(Pilot) is { Status: TrackingStatus.NeedsLogin } s ? s : null);
        _time.Advance(CharacterTracker.MaxBackoff);
        await Task.Delay(50);
        Assert.Equal(1, _tokens.Requests(Pilot));

        _tokens.Fail(Pilot, null);
        _esi.Set(Pilot, online: true, Jita, Ishtar);
        _tracker.Track([Pilot]);

        var state = await WaitForAsync(() => _tracker.Get(Pilot) is { Status: TrackingStatus.Tracking } s ? s : null);
        Assert.Equal(Jita, state.Location);
    }

    [Fact]
    public async Task A_rejected_token_is_dropped_and_polling_backs_off_then_recovers()
    {
        _esi.Set(Pilot, online: true, Jita, Ishtar);
        _esi.FailNext(Pilot, EsiCallStatus.Unauthorized);

        _tracker.Track([Pilot]);
        var failing = await WaitForAsync(() => _tracker.Get(Pilot) is { Status: TrackingStatus.Failing } s ? s : null);

        Assert.Contains("access token", failing.Error);
        Assert.Equal(1, _tokens.Invalidations(Pilot));

        await AdvanceUntilAsync(CharacterTracker.Backoff(1), () => _tracker.Get(Pilot)?.Status == TrackingStatus.Tracking);
        Assert.Null(_tracker.Get(Pilot)!.Error);
    }

    [Fact]
    public async Task Characters_no_longer_tracked_are_removed()
    {
        _esi.Set(Pilot, online: true, Jita, Ishtar);
        _tracker.Track([Pilot]);
        await WaitForAsync(() => _tracker.Get(Pilot) is { Status: TrackingStatus.Tracking } s ? s : null);

        _tracker.Track([]);

        Assert.Empty(_tracker.Characters);
        Assert.Contains(_changes, c => c.CharacterId == Pilot && c.Current is null);
        var calls = _esi.LocationCalls(Pilot);
        _time.Advance(CharacterTracker.LocationInterval * 2);
        await Task.Delay(50);
        Assert.Equal(calls, _esi.LocationCalls(Pilot));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(5, 80)]
    [InlineData(6, 120)]
    [InlineData(20, 120)]
    public void Backoff_doubles_up_to_two_minutes(int failures, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), CharacterTracker.Backoff(failures));
    }

    /// <summary>Advances the fake clock by one step at a time until the condition holds (the loops run on the thread pool).</summary>
    private async Task AdvanceUntilAsync(TimeSpan step, Func<bool> condition)
    {
        for (var i = 0; i < 20 && !condition(); i++)
        {
            _time.Advance(step);
            for (var wait = 0; wait < 20 && !condition(); wait++)
                await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static async Task<T> WaitForAsync<T>(Func<T?> read) where T : class
    {
        for (var i = 0; i < 300; i++)
        {
            if (read() is { } value)
                return value;
            await Task.Delay(10);
        }

        throw new TimeoutException("The tracker did not reach the expected state.");
    }

    private sealed class FakeEsi : ILocationEsi
    {
        private readonly ConcurrentDictionary<long, (bool Online, CharacterLocation Location, CharacterShip Ship)> _characters = new();
        private readonly ConcurrentDictionary<long, EsiCallStatus> _failNext = new();
        private readonly ConcurrentDictionary<(long, string), int> _calls = new();

        public void Set(long id, bool online, CharacterLocation location, CharacterShip ship) => _characters[id] = (online, location, ship);

        public void FailNext(long id, EsiCallStatus status) => _failNext[id] = status;

        public int OnlineCalls(long id) => _calls.GetValueOrDefault((id, "online"));

        public int LocationCalls(long id) => _calls.GetValueOrDefault((id, "location"));

        public Task<EsiCall<bool>> GetOnlineAsync(long characterId, string accessToken, CancellationToken cancellationToken) =>
            Answer(characterId, "online", c => c.Online);

        public Task<EsiCall<CharacterLocation>> GetLocationAsync(long characterId, string accessToken, CancellationToken cancellationToken) =>
            Answer(characterId, "location", c => c.Location);

        public Task<EsiCall<CharacterShip>> GetShipAsync(long characterId, string accessToken, CancellationToken cancellationToken) =>
            Answer(characterId, "ship", c => c.Ship);

        private Task<EsiCall<T>> Answer<T>(long id, string call, Func<(bool Online, CharacterLocation Location, CharacterShip Ship), T> select)
        {
            _calls.AddOrUpdate((id, call), 1, (_, n) => n + 1);
            if (_failNext.TryRemove(id, out var status))
                return Task.FromResult(new EsiCall<T>(status, Error: "token expired"));
            return Task.FromResult(EsiCall<T>.Ok(select(_characters[id])));
        }
    }

    private sealed class FakeTokens : ICharacterTokenProvider
    {
        private readonly ConcurrentDictionary<long, Exception?> _failures = new();
        private readonly ConcurrentDictionary<long, int> _requests = new();
        private readonly ConcurrentDictionary<long, int> _invalidations = new();

        public void Fail(long id, Exception? exception) => _failures[id] = exception;

        public int Requests(long id) => _requests.GetValueOrDefault(id);

        public int Invalidations(long id) => _invalidations.GetValueOrDefault(id);

        public ValueTask<string> GetAccessTokenAsync(long characterId, CancellationToken cancellationToken = default)
        {
            _requests.AddOrUpdate(characterId, 1, (_, n) => n + 1);
            return _failures.GetValueOrDefault(characterId) is { } e ? ValueTask.FromException<string>(e) : ValueTask.FromResult($"token-{characterId}");
        }

        public void InvalidateAccessToken(long characterId) => _invalidations.AddOrUpdate(characterId, 1, (_, n) => n + 1);
    }
}
