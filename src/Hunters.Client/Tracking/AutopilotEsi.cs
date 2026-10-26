using NewEdenKit.ESI.Services;

namespace NewEdenSoft.Hunters.Client.Tracking;

/// <summary>Sets a character's autopilot destination in the EVE client, with its own access token (<c>esi-ui.write_waypoint.v1</c>).</summary>
public interface IAutopilotEsi
{
    /// <summary>Clears the other waypoints and sets the solar system as the destination.</summary>
    Task<EsiCall<bool>> SetDestinationAsync(int solarSystemId, string accessToken, CancellationToken cancellationToken);
}

/// <summary><see cref="IAutopilotEsi"/> through NewEdenKit.ESI.</summary>
public sealed class AutopilotEsi(IUserInterface ui) : IAutopilotEsi
{
    public async Task<EsiCall<bool>> SetDestinationAsync(int solarSystemId, string accessToken, CancellationToken cancellationToken) =>
        EsiCalls.Map(
            await ui.PostUiAutopilotWaypoint(addToBeginning: false, clearOtherWaypoints: true, solarSystemId, accessToken).ExecuteAsync(cancellationToken),
            _ => true);
}
