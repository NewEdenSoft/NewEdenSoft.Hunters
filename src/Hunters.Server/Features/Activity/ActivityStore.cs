using NewEdenSoft.Hunters.Contracts.Activity;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Server.Features.Map;
using NewEdenSoft.Hunters.Server.Infrastructure.WebHosting;
using ContractMetrics = NewEdenSoft.Hunters.Contracts.Activity.ActivityMetrics;

namespace NewEdenSoft.Hunters.Server.Features.Activity;

/// <summary>One version of the intel service's current activity tables, held in memory.</summary>
public sealed class ActivitySnapshot(
    long version,
    IReadOnlyDictionary<int, SystemActivityCurrent> systems,
    IReadOnlyDictionary<int, RegionActivityCurrent> regions,
    IReadOnlyDictionary<short, ActivityWeekSamples> samples)
{
    public long Version { get; } = version;

    public IReadOnlyDictionary<int, SystemActivityCurrent> Systems { get; } = systems;

    public IReadOnlyDictionary<int, RegionActivityCurrent> Regions { get; } = regions;

    /// <summary>How many hours each hour of week holds, for turning week sums into averages.</summary>
    public IReadOnlyDictionary<short, ActivityWeekSamples> Samples { get; } = samples;

    /// <summary>Every current row carries the same hour; take it from any.</summary>
    public SystemActivityCurrent Header => Systems.Values.First();

    private readonly bool _killsPrevious = systems.Values.Any(r => r.NpcKills.PreviousHour is not null);
    private readonly bool _jumpsPrevious = systems.Values.Any(r => r.ShipJumps.PreviousHour is not null);

    /// <summary>Whether a metric (in <see cref="Metrics(SystemActivityCurrent?)"/> order) has a previous hour; the same for all rows.</summary>
    public bool HasPrevious(int metric) => metric == 3 ? _jumpsPrevious : _killsPrevious;

    /// <summary>NPC kills, ship kills, pod kills, ship jumps; null without a row (no activity).</summary>
    public static ActivityMetric[]? Metrics(SystemActivityCurrent? row) =>
        row is null ? null : [row.NpcKills, row.ShipKills, row.PodKills, row.ShipJumps];

    /// <inheritdoc cref="Metrics(SystemActivityCurrent?)"/>
    public static ActivityMetric[]? Metrics(RegionActivityCurrent? row) =>
        row is null ? null : [row.NpcKills, row.ShipKills, row.PodKills, row.ShipJumps];

    /// <summary>The strong ETag of everything derived from this version.</summary>
    public string ETag => $"\"activity-{Version}\"";
}

/// <summary>
/// The current activity snapshot, and the whole-map response built from it once per (version, map
/// build). Null until the intel service has produced data.
/// </summary>
public sealed class ActivityStore
{
    private volatile ActivitySnapshot? _current;
    private volatile CurrentResponse? _response;

    public ActivitySnapshot? Current => _current;

    public void Publish(ActivitySnapshot? snapshot) => _current = snapshot;

    /// <summary>The whole-map response for this snapshot and map pack, built on first use.</summary>
    public CurrentResponse Response(ActivitySnapshot snapshot, MapPackSnapshot map)
    {
        if (_response is { } cached && cached.Version == snapshot.Version && cached.SdeBuild == map.Build)
            return cached;
        var response = new CurrentResponse(snapshot.Version, map.Build, PrecompressedJson.Create(Build(snapshot, map)));
        _response = response;
        return response;
    }

    public sealed record CurrentResponse(long Version, long SdeBuild, PrecompressedJson Body)
    {
        public string ETag => $"\"activity-{Version}-{SdeBuild}\"";
    }

    private static ActivityCurrent Build(ActivitySnapshot snapshot, MapPackSnapshot map)
    {
        var header = snapshot.Header;
        var systems = map.Pack.Systems.Select(s => snapshot.Systems.GetValueOrDefault(s.Id)).ToList();
        var regions = map.Pack.Regions.Select(r => snapshot.Regions.GetValueOrDefault(r.Id)).ToList();

        return new ActivityCurrent(
            snapshot.Version,
            map.Build,
            header.KillsPeriodEnd,
            header.JumpsPeriodEnd,
            header.KillsDayHours,
            header.JumpsDayHours,
            Metrics(snapshot, systems.Select(ActivitySnapshot.Metrics).ToList()),
            Metrics(snapshot, regions.Select(ActivitySnapshot.Metrics).ToList()));
    }

    private static ContractMetrics Metrics(ActivitySnapshot snapshot, List<ActivityMetric[]?> rows) =>
        new(Columns(snapshot, rows, 0), Columns(snapshot, rows, 1), Columns(snapshot, rows, 2), Columns(snapshot, rows, 3));

    /// <summary>One metric as columns. A missing row (no activity) is zeros with no peak.</summary>
    private static ActivityColumns Columns(ActivitySnapshot snapshot, List<ActivityMetric[]?> rows, int metric)
    {
        var values = rows.Select(r => r?[metric]).ToList();

        // The previous hour and "usual" are missing for every row or none (see the aggregator).
        var hasPrevious = snapshot.HasPrevious(metric);
        var hasTypical = values.Any(v => v?.Typical is not null);

        return new ActivityColumns(
            [.. values.Select(v => v?.LastHour ?? 0)],
            hasPrevious ? [.. values.Select(v => v?.PreviousHour ?? 0)] : null,
            [.. values.Select(v => v?.Day ?? 0)],
            hasTypical ? [.. values.Select(v => Math.Round(v?.Typical ?? 0, 2))] : null,
            [.. values.Select(v => v?.PeakRatio is { } r ? Math.Round(r, 3) : (double?)null)],
            [.. values.Select(v => (int?)v?.HoursToPeak)],
            [.. values.Select(v => (int?)v?.PeakHourOfWeek)]);
    }
}
