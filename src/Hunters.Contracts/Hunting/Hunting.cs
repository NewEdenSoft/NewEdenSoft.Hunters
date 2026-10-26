namespace NewEdenSoft.Hunters.Contracts.Hunting;

/// <summary>A hunting profile: a kind of target (docs/map-and-activity.md, "Hunting profiles").</summary>
/// <param name="Id">Stable id, for example <c>carriers</c>.</param>
/// <param name="Description">What the target is, in a sentence; empty when the intel service has none.</param>
/// <param name="Evidence">What counts as seeing it, one line per way: the profile's rules in players' words.</param>
public sealed record HuntingProfileInfo(string Id, string Name, string Description = "", IReadOnlyList<string>? Evidence = null);

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
/// <param name="Reliable">
/// Per system, 24 values by hour of the day (UTC): the strength of the pilots with a routine there whose
/// window covers the hour. High when someone reliably is there at that time: a target worth landing on now.
/// </param>
public sealed record HuntingProfileSystems(
    string Profile,
    IReadOnlyList<int> SystemIds,
    IReadOnlyList<double> Score,
    IReadOnlyList<double> Week,
    IReadOnlyList<double> Month,
    IReadOnlyList<DateTimeOffset> LastSeen,
    IReadOnlyList<IReadOnlyList<double>> Hours,
    IReadOnlyList<IReadOnlyList<double>>? Reliable = null);

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
/// <param name="Regulars">
/// For a system, the pilots with a routine there (session windows first, then the strongest); empty for a
/// region.
/// </param>
/// <param name="Reliable">For a system, 24 values by hour of the day (UTC): how reliably someone is there then; empty for a region.</param>
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
    int LocalPilots = 0,
    IReadOnlyList<HuntingRegular>? Regulars = null,
    IReadOnlyList<double>? Reliable = null);

/// <summary>
/// A pilot seen in a system on different days at about the same time of day, over the last 30 days: a
/// routine, likely to be back in that window.
/// </summary>
/// <param name="Days">Different days (UTC) seen there.</param>
/// <param name="DaysInWindow">The days seen inside the window.</param>
/// <param name="WindowStart">The window's first hour of the day (UTC); it may run past midnight.</param>
/// <param name="WindowHours">4 for a play session's window, 6 for the extended one.</param>
/// <param name="Local">Seen as a local (in its own alliance's sovereignty) at least once.</param>
/// <param name="Strength">
/// How reliable the routine is: the days in the window, each fading with a 14-day half-life, at half for
/// an extended window.
/// </param>
public sealed record HuntingRegular(
    long PilotId,
    int Days,
    int DaysInWindow,
    int WindowStart,
    int WindowHours,
    DateTimeOffset LastSeen,
    bool Local,
    double Strength);

public static class HuntingPaths
{
    public const string Profiles = "/api/hunting/profiles";

    /// <summary>Every profile's systems. Revalidate with the ETag.</summary>
    public const string Overview = "/api/hunting/overview";

    public static string System(int systemId) => $"/api/hunting/systems/{systemId}";

    public static string Region(int regionId) => $"/api/hunting/regions/{regionId}";
}
