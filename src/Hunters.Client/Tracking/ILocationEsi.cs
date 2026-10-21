using System.Net;
using NewEdenKit.ESI.Abstractions.Results;
using NewEdenKit.ESI.Services;

namespace NewEdenSoft.Hunters.Client.Tracking;

public enum EsiCallStatus
{
    Ok,

    /// <summary>ESI rejected the access token (401): ask the server for a new one.</summary>
    Unauthorized,

    /// <summary>ESI refused the call (403), usually a scope the character has not granted.</summary>
    Forbidden,

    /// <summary>Any other failure: ESI errors, error limits, the network.</summary>
    Failed,
}

/// <summary>The outcome of one ESI call. <see cref="Value"/> is set only when <see cref="Status"/> is <see cref="EsiCallStatus.Ok"/>.</summary>
public readonly record struct EsiCall<T>(EsiCallStatus Status, T? Value = default, string? Error = null)
{
    public static EsiCall<T> Ok(T value) => new(EsiCallStatus.Ok, value);
}

/// <summary>The three ESI calls behind character tracking, all with the character's own access token.</summary>
public interface ILocationEsi
{
    Task<EsiCall<bool>> GetOnlineAsync(long characterId, string accessToken, CancellationToken cancellationToken);

    Task<EsiCall<CharacterLocation>> GetLocationAsync(long characterId, string accessToken, CancellationToken cancellationToken);

    Task<EsiCall<CharacterShip>> GetShipAsync(long characterId, string accessToken, CancellationToken cancellationToken);
}

/// <summary><see cref="ILocationEsi"/> through NewEdenKit.ESI.</summary>
public sealed class LocationEsi(ILocation location) : ILocationEsi
{
    public async Task<EsiCall<bool>> GetOnlineAsync(long characterId, string accessToken, CancellationToken cancellationToken) =>
        Map(await location.GetCharactersCharacterIdOnline(characterId, accessToken).ExecuteAsync(cancellationToken), v => v.Online);

    public async Task<EsiCall<CharacterLocation>> GetLocationAsync(long characterId, string accessToken, CancellationToken cancellationToken) =>
        Map(await location.GetCharactersCharacterIdLocation(characterId, accessToken).ExecuteAsync(cancellationToken),
            v => new CharacterLocation((int)v.SolarSystemId, v.StationId, v.StructureId));

    public async Task<EsiCall<CharacterShip>> GetShipAsync(long characterId, string accessToken, CancellationToken cancellationToken) =>
        Map(await location.GetCharactersCharacterIdShip(characterId, accessToken).ExecuteAsync(cancellationToken),
            v => new CharacterShip((int)v.ShipTypeId, v.ShipName, v.ShipItemId));

    private static EsiCall<TOut> Map<TIn, TOut>(EsiResult<TIn> result, Func<TIn, TOut> select)
    {
        if (result.IsSuccess)
            return EsiCall<TOut>.Ok(select(result.Value!));

        var status = result.StatusCode switch
        {
            HttpStatusCode.Unauthorized => EsiCallStatus.Unauthorized,
            HttpStatusCode.Forbidden => EsiCallStatus.Forbidden,
            _ => EsiCallStatus.Failed,
        };
        // An unresolved 304 has no body to read; treat it as a miss and try again next time.
        var message = result.IsNotModified ? "ESI sent no new data." : result.Error?.Message ?? $"ESI answered {(int?)result.StatusCode}.";
        return new EsiCall<TOut>(status, Error: message);
    }
}
