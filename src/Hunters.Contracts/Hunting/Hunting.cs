namespace NewEdenSoft.Hunters.Contracts.Hunting;

/// <summary>A hunting profile: a kind of target (docs/map-and-activity.md, "Hunting profiles").</summary>
/// <param name="Id">Stable id, for example <c>carriers</c>.</param>
public sealed record HuntingProfileInfo(string Id, string Name);

/// <summary>
/// Where every profile's target is found, for colouring the map by a profile (docs/map-and-activity.md,
/// "Serving hunting"). Sparse: per profile, only the systems with sightings, highest score first.
/// </summary>
/// <param name="UpdatedAt">When the aggregates were computed; the windows and scores are as of then.</param>
public sealed record HuntingOverview(long Version, DateTimeOffset UpdatedAt, IReadOnlyList<HuntingProfileSystems> Profiles);

/// <summary>One profile's systems, as columns: entry <c>i</c> of each list belongs to <c>SystemIds[i]</c>.</summary>
/// <param name="Score">Weighted sightings, each halving every 14 days: how established the habit is.</param>
/// <param name="Week">Weighted sightings in the last 7 days.</param>
/// <param name="Month">Weighted sightings in the last 30 days.</param>
/// <param name="Hours">Per system, 24 weighted sightings by hour of the day (UTC): when it is there.</param>
public sealed record HuntingProfileSystems(
    string Profile,
    IReadOnlyList<int> SystemIds,
    IReadOnlyList<double> Score,
    IReadOnlyList<double> Week,
    IReadOnlyList<double> Month,
    IReadOnlyList<DateTimeOffset> LastSeen,
    IReadOnlyList<IReadOnlyList<double>> Hours);

/// <summary>The profiles seen in one system or region, highest score first; empty when none were.</summary>
public sealed record HuntingPlace(int Id, long Version, DateTimeOffset UpdatedAt, IReadOnlyList<HuntingPlaceProfile> Profiles);

/// <param name="MonthIndicator">The part of <paramref name="Month"/> from indirect evidence (CRAB beacons for Carriers).</param>
/// <param name="Pilots">Different pilots seen in the last 30 days.</param>
/// <param name="Hours">24 weighted sightings by hour of the day (UTC).</param>
/// <param name="MonthLocal">
/// The part of <paramref name="Month"/> from locals: seen in their own alliance's sovereignty, which
/// counts 1.5 times.
/// </param>
/// <param name="LocalPilots">The part of <paramref name="Pilots"/> seen as locals.</param>
public sealed record HuntingPlaceProfile(
    string Profile,
    string Name,
    double Score,
    double Week,
    double Month,
    double MonthIndicator,
    int Pilots,
    DateTimeOffset LastSeen,
    IReadOnlyList<double> Hours,
    double MonthLocal = 0,
    int LocalPilots = 0);

public static class HuntingPaths
{
    public const string Profiles = "/api/hunting/profiles";

    /// <summary>Every profile's systems. Revalidate with the ETag.</summary>
    public const string Overview = "/api/hunting/overview";

    public static string System(int systemId) => $"/api/hunting/systems/{systemId}";

    public static string Region(int regionId) => $"/api/hunting/regions/{regionId}";
}
