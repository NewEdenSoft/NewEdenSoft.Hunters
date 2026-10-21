using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Contracts.Auth;

namespace NewEdenSoft.Hunters.Client.Tracking;

/// <summary>
/// Polls ESI for the online status, location and ship of the characters it is told to track, keeps
/// the latest state of each, and raises <see cref="Changed"/> when something changes. The client
/// makes these calls itself with tokens from the server (docs/decisions.md, D11); one tracker serves
/// every view in the app, and later the location reporter for fleets.
/// </summary>
/// <remarks>
/// Online characters are polled every <see cref="LocationInterval"/>, the cache time of ESI's
/// location and ship. While a character is offline only its online status is polled, every
/// <see cref="OnlineInterval"/> (ESI caches it that long); its last location and ship are read once
/// at the start. Failures back off up to <see cref="MaxBackoff"/>. Events are raised on the polling
/// thread; UI code marshals them itself.
/// </remarks>
public sealed class CharacterTracker : IAsyncDisposable
{
    public static readonly TimeSpan LocationInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan OnlineInterval = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(2);

    private readonly ILocationEsi _esi;
    private readonly ICharacterTokenProvider _tokens;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private readonly Dictionary<long, TrackedCharacter> _states = [];
    private readonly Dictionary<long, (CancellationTokenSource Cancel, Task Loop)> _loops = [];
    private bool _disposed;

    public CharacterTracker(ILocationEsi esi, ICharacterTokenProvider tokens, TimeProvider? time = null)
    {
        _esi = esi;
        _tokens = tokens;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>A tracked character's state changed, or it stopped being tracked.</summary>
    public event EventHandler<TrackedCharacterChangedEventArgs>? Changed;

    /// <summary>The latest state of every tracked character.</summary>
    public IReadOnlyList<TrackedCharacter> Characters
    {
        get
        {
            lock (_lock)
                return [.. _states.Values];
        }
    }

    public TrackedCharacter? Get(long characterId)
    {
        lock (_lock)
            return _states.GetValueOrDefault(characterId);
    }

    /// <summary>
    /// Tracks exactly these characters: starts the new ones and stops the rest. Call it again when
    /// the account or the user's choice changes; a character that needed a login starts again.
    /// </summary>
    public void Track(IEnumerable<long> characterIds)
    {
        var wanted = characterIds.ToHashSet();
        List<long> stopped;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            stopped = [.. _loops.Keys.Where(id => !wanted.Contains(id))];
            foreach (var id in stopped)
            {
                _loops.Remove(id, out var loop);
                loop.Cancel.Cancel();
            }

            // A loop that ended (the character needed a login) is started again.
            foreach (var id in wanted.Where(id => !_loops.TryGetValue(id, out var loop) || loop.Loop.IsCompleted))
            {
                var cancel = new CancellationTokenSource();
                _loops[id] = (cancel, Task.Run(() => RunAsync(id, cancel.Token)));
            }
        }

        foreach (var id in stopped)
            Publish(id, null);
    }

    public async ValueTask DisposeAsync()
    {
        Task[] loops;
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (var (cancel, _) in _loops.Values)
                cancel.Cancel();
            loops = [.. _loops.Values.Select(l => l.Loop)];
            _loops.Clear();
        }

        await Task.WhenAll(loops);
    }

    private async Task RunAsync(long characterId, CancellationToken cancellationToken)
    {
        // Started again after a login: keep the last known location and ship until ESI answers.
        var state = (Get(characterId) ?? new TrackedCharacter(characterId, TrackingStatus.Starting)) with { Status = TrackingStatus.Starting, Error = null };
        Publish(characterId, state);

        var nextOnlineCheck = DateTimeOffset.MinValue;
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                var token = await _tokens.GetAccessTokenAsync(characterId, cancellationToken);
                var now = _time.GetUtcNow();

                var isOnline = state.IsOnline;
                if (now >= nextOnlineCheck)
                {
                    isOnline = Require(await _esi.GetOnlineAsync(characterId, token, cancellationToken), characterId);
                    nextOnlineCheck = now + OnlineInterval;
                }

                var location = state.Location;
                var ship = state.Ship;
                // Online: follow them. Offline: read where they logged off, once.
                if (isOnline == true || location is null)
                {
                    location = Require(await _esi.GetLocationAsync(characterId, token, cancellationToken), characterId);
                    ship = Require(await _esi.GetShipAsync(characterId, token, cancellationToken), characterId);
                }

                state = new TrackedCharacter(characterId, TrackingStatus.Tracking, isOnline, location, ship, UpdatedAt: now);
                failures = 0;
                delay = isOnline == true ? LocationInterval : nextOnlineCheck - now;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (HuntersApiException e) when (e.Code == AuthErrorCodes.NeedsReauth || e.Code == HuntersApiException.SessionEnded)
            {
                // The session already flags the character; nothing to poll until it logs in again.
                Publish(characterId, state with { Status = TrackingStatus.NeedsLogin, Error = e.Message });
                return;
            }
            catch (Exception e) when (e is HuntersApiException or TrackingException)
            {
                failures++;
                state = state with { Status = TrackingStatus.Failing, Error = e.Message };
                delay = Backoff(failures);
            }

            Publish(characterId, state);

            try
            {
                await Task.Delay(delay, _time, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private T Require<T>(EsiCall<T> call, long characterId)
    {
        switch (call.Status)
        {
            case EsiCallStatus.Ok:
                return call.Value!;
            case EsiCallStatus.Unauthorized:
                // Ask the server for a fresh token on the next attempt.
                _tokens.InvalidateAccessToken(characterId);
                throw new TrackingException($"ESI rejected the access token: {call.Error}");
            case EsiCallStatus.Forbidden:
                throw new TrackingException($"ESI refused to share the location; the character may be missing a permission. {call.Error}");
            default:
                throw new TrackingException($"ESI did not answer: {call.Error}");
        }
    }

    /// <summary>5, 10, 20, 40, 80 seconds, then <see cref="MaxBackoff"/>.</summary>
    public static TimeSpan Backoff(int failures) =>
        TimeSpan.FromSeconds(Math.Min(LocationInterval.TotalSeconds * Math.Pow(2, failures - 1), MaxBackoff.TotalSeconds));

    private void Publish(long characterId, TrackedCharacter? state)
    {
        TrackedCharacter? previous;
        lock (_lock)
        {
            previous = _states.GetValueOrDefault(characterId);
            // A stopped loop may still finish an attempt; it no longer owns the character.
            if (state is not null && (!_loops.ContainsKey(characterId) || _disposed))
                return;

            if (state is null)
                _states.Remove(characterId);
            else if (state.SameAs(previous))
            {
                _states[characterId] = state;
                return;
            }
            else
                _states[characterId] = state;
        }

        if (previous is not null || state is not null)
            Changed?.Invoke(this, new TrackedCharacterChangedEventArgs(characterId, previous, state));
    }

    private sealed class TrackingException(string message) : Exception(message);
}
