using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Client.Tracking;
using NewEdenSoft.Hunters.Contracts.Account;

namespace NewEdenSoft.Hunters.Desktop.Characters;

/// <summary>A character the autopilot destination can be set for.</summary>
public sealed record DestinationCharacter(long Id, string Name, bool IsMain, bool IsOnline);

public enum DestinationTone
{
    /// <summary>Under way.</summary>
    Muted,

    Ok,

    /// <summary>At least one character failed.</summary>
    Danger,
}

/// <summary>What setting a destination came to: one line per outcome, who it was set for, then each failure.</summary>
public sealed record DestinationOutcome(DestinationTone Tone, IReadOnlyList<string> Lines);

/// <summary>
/// Sets characters' autopilot destination through ESI (<see cref="IAutopilotEsi"/>), with the tokens the
/// server hands out, like the web's <c>useDestination</c>. A rejected token is renewed once.
/// </summary>
public sealed class Destinations(IAutopilotEsi esi, CharacterTracking tracking)
{
    /// <summary>Every character of the account that can be used (no new login needed), online first, then the main.</summary>
    public IReadOnlyList<DestinationCharacter> Usable(AccountInfo account) => Usable(account, tracking.Get);

    /// <summary>The tracked characters among <see cref="Usable(AccountInfo)"/>, in the same order.</summary>
    public IReadOnlyList<DestinationCharacter> Tracked(AccountInfo account) => Tracked(Usable(account), tracking.Get);

    public static IReadOnlyList<DestinationCharacter> Usable(AccountInfo account, Func<long, TrackedCharacter?> tracked) =>
    [
        .. account.Characters
            .Where(c => c.Status == CharacterStatus.Active)
            .Select(c => new DestinationCharacter(c.Id, c.Name, c.IsMain, tracked(c.Id)?.IsOnline == true))
            .OrderByDescending(c => c.IsOnline)
            .ThenByDescending(c => c.IsMain)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
    ];

    public static IReadOnlyList<DestinationCharacter> Tracked(IReadOnlyList<DestinationCharacter> usable, Func<long, TrackedCharacter?> tracked) =>
        [.. usable.Where(c => tracked(c.Id) is { Status: not TrackingStatus.NeedsLogin })];

    /// <summary>While it is being set: "Setting the destination for A, B…".</summary>
    public static DestinationOutcome Setting(IEnumerable<DestinationCharacter> characters) =>
        new(DestinationTone.Muted, [$"Setting the destination for {string.Join(", ", characters.Select(c => c.Name))}…"]);

    /// <summary>"Destination Tama set for A, B.", or who it was set for and then each failure.</summary>
    public static DestinationOutcome Outcome(string system, IReadOnlyList<string> done, IReadOnlyList<(string Name, string Error)> failed)
    {
        if (failed.Count == 0)
            return new DestinationOutcome(DestinationTone.Ok, [$"Destination {system} set for {string.Join(", ", done)}."]);
        return new DestinationOutcome(
            DestinationTone.Danger,
            [.. done.Count > 0 ? [$"Set for {string.Join(", ", done)}."] : Array.Empty<string>(), .. failed.Select(f => $"{f.Name}: {f.Error}")]);
    }

    /// <summary>Sets the destination for each character at once.</summary>
    public async Task<DestinationOutcome> SetAsync(
        ICharacterTokenProvider tokens, int systemId, string system, IReadOnlyList<DestinationCharacter> characters, CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(characters.Select(async c => (c.Name, Error: await SetOneAsync(tokens, c.Id, systemId, cancellationToken))));
        return Outcome(
            system,
            [.. results.Where(r => r.Error is null).Select(r => r.Name)],
            [.. results.Where(r => r.Error is not null).Select(r => (r.Name, r.Error!))]);
    }

    /// <summary>One character: null when set, else why not.</summary>
    private async Task<string?> SetOneAsync(ICharacterTokenProvider tokens, long characterId, int systemId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await esi.SetDestinationAsync(systemId, await tokens.GetAccessTokenAsync(characterId, cancellationToken), cancellationToken);
            if (result.Status == EsiCallStatus.Unauthorized)
            {
                tokens.InvalidateAccessToken(characterId);
                result = await esi.SetDestinationAsync(systemId, await tokens.GetAccessTokenAsync(characterId, cancellationToken), cancellationToken);
            }

            return result.Status switch
            {
                EsiCallStatus.Ok => null,
                EsiCallStatus.Forbidden => "not allowed to set waypoints; log in with it again",
                _ => result.Error ?? "ESI could not set it.",
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }
}
