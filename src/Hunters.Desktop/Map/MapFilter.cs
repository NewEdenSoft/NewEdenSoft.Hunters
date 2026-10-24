using System.Globalization;
using Avalonia.Media;
using NewEdenSoft.Hunters.Contracts.Activity;
using NewEdenSoft.Hunters.Desktop.Theming;

namespace NewEdenSoft.Hunters.Desktop.Map;

public enum MapFilterId
{
    Security,
    Npc,
    NpcRising,
    NpcFalling,
    Ship,
    Jumps,
}

public sealed record MapLegendItem(string Label, Color Colour);

/// <summary>
/// A colour filter decides how each system is drawn: which colour group it belongs to, or whether
/// it is hidden. Security status and the hourly activity are filters; routes and fleet positions
/// become filters too, without changes to the renderer. Matches <c>web/hunters-web/src/map/filters.ts</c>.
/// </summary>
/// <param name="Groups">Colour group per system index: an index into <paramref name="Colours"/>, or <see cref="Hidden"/>.</param>
/// <param name="Notice">Why the filter shows nothing yet (no activity data, no previous hour), or null.</param>
/// <param name="Sizes">Optional radius multiplier per group, so busier systems are also drawn larger.</param>
public sealed record MapFilter(
    MapFilterId Id,
    string Label,
    byte[] Groups,
    IReadOnlyList<Color> Colours,
    IReadOnlyList<MapLegendItem> Legend,
    string? Notice = null,
    IReadOnlyList<float>? Sizes = null)
{
    /// <summary>A system in this group is not drawn, labelled or hoverable (its gates still are).</summary>
    public const byte Hidden = 255;

    /// <summary>The radius multiplier of a system's group: 1 without sizes, or for a hidden system.</summary>
    public float SizeOf(int index) =>
        Sizes is { } sizes && Groups[index] != Hidden && Groups[index] < sizes.Count ? sizes[Groups[index]] : 1;
}

/// <summary>What the filters may need besides the map.</summary>
/// <param name="Activity">The latest hour of activity; null while loading or when the server has none.</param>
/// <param name="MinNpc">For NPC rising and falling: systems with fewer NPC kills in the last hour stay dim.</param>
/// <param name="Quiet">The colour of systems below the first heat band: the theme's faint line colour.</param>
public sealed record MapFilterContext(ActivityCurrent? Activity, int MinNpc, Color Quiet);

/// <param name="NeedsActivity">Needs the server's activity data (the discovery document lists the <c>activity</c> feature).</param>
public sealed record MapFilterDefinition(MapFilterId Id, string Label, bool NeedsActivity);

public static class MapFilters
{
    public static readonly IReadOnlyList<MapFilterDefinition> All =
    [
        new(MapFilterId.Security, "Security", false),
        new(MapFilterId.Npc, "NPC kills", true),
        new(MapFilterId.NpcRising, "NPC rising", true),
        new(MapFilterId.NpcFalling, "NPC falling", true),
        new(MapFilterId.Ship, "Ship kills", true),
        new(MapFilterId.Jumps, "Jumps", true),
    ];

    /// <summary>The minimum NPC kills in the last hour offered for the rising and falling filters.</summary>
    public static readonly IReadOnlyList<int> MinNpcChoices = [0, 25, 100, 250];

    /// <summary>The heat groups' sizes: quiet systems smaller, the busiest twice the normal size.</summary>
    public static readonly IReadOnlyList<float> HeatSizes = [0.8f, 1, 1.2f, 1.45f, 1.7f, 2];

    public static MapFilter Create(MapFilterId id, MapModel model, ThemeCatalog catalog, MapFilterContext context) => id switch
    {
        MapFilterId.Security => Security(model, catalog),
        MapFilterId.Npc => Count(id, "NPC kills", model, catalog, context, m => m.NpcKills, "npcKills"),
        MapFilterId.NpcRising => Change(id, "NPC rising", model, catalog, context, 1),
        MapFilterId.NpcFalling => Change(id, "NPC falling", model, catalog, context, -1),
        MapFilterId.Ship => Count(id, "Ship kills", model, catalog, context, m => m.ShipKills, "shipKills"),
        MapFilterId.Jumps => Count(id, "Jumps", model, catalog, context, m => m.ShipJumps, "jumps"),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, null),
    };

    /// <summary>Colours systems by security status as EVE shows it, from 1.0 down to null security.</summary>
    public static MapFilter Security(MapModel model, ThemeCatalog catalog)
    {
        var groups = new byte[model.Count];
        for (var i = 0; i < model.Count; i++)
            groups[i] = (byte)SecurityStatus.Bucket(model.Security[i]);

        var colours = Enumerable.Range(0, SecurityStatus.BucketCount)
            .Select(bucket => ThemeService.ToColor(bucket == SecurityStatus.NullBucket ? catalog.SecurityNull : catalog.SecuritySteps[bucket]))
            .ToList();

        var legend = new List<MapLegendItem>();
        for (var bucket = 10; bucket >= 0; bucket--)
            legend.Add(new MapLegendItem((bucket / 10.0).ToString("0.0", CultureInfo.InvariantCulture), colours[bucket]));
        legend.Add(new MapLegendItem("Null", colours[SecurityStatus.NullBucket]));

        return new MapFilter(MapFilterId.Security, "Security", groups, colours, legend);
    }

    /// <summary>The activity columns for this map's systems, or null when there are none that line up with it.</summary>
    public static ActivityMetrics? ActivityFor(MapModel model, ActivityCurrent? activity) =>
        ColumnsFor(model, activity, out _);

    /// <summary>The heat group of a value: 0 below the first bound, else the band it reaches (1–5).</summary>
    public static byte HeatGroup(int value, IReadOnlyList<int> bands)
    {
        byte group = 0;
        for (var b = 0; b < bands.Count; b++)
        {
            if (value >= bands[b])
                group = (byte)(b + 1);
        }

        return group;
    }

    /// <summary>A count in the last hour, in heat bands.</summary>
    private static MapFilter Count(
        MapFilterId id, string label, MapModel model, ThemeCatalog catalog, MapFilterContext context,
        Func<ActivityMetrics, ActivityColumns> metric, string bandsOf)
    {
        if (ColumnsFor(model, context.Activity, out var notice) is not { } systems)
            return Unavailable(id, label, model, catalog, context, notice!);

        var values = metric(systems).LastHour;
        var bands = catalog.Heat[bandsOf];
        var groups = new byte[model.Count];
        for (var i = 0; i < model.Count; i++)
            groups[i] = HeatGroup(values[i], bands);

        var colours = HeatColours(catalog, context);
        return new MapFilter(id, label, groups, colours, HeatLegend(bands, colours, b => $"{b}+"), Sizes: HeatSizes);
    }

    /// <summary>
    /// NPC kills rising (direction 1) or falling (-1) from the hour before, in heat bands of the change.
    /// Systems with fewer than <see cref="MapFilterContext.MinNpc"/> NPC kills in the last hour stay dim,
    /// so small changes in quiet systems don't light up.
    /// </summary>
    private static MapFilter Change(MapFilterId id, string label, MapModel model, ThemeCatalog catalog, MapFilterContext context, int direction)
    {
        if (ColumnsFor(model, context.Activity, out var notice) is not { } systems)
            return Unavailable(id, label, model, catalog, context, notice!);
        if (systems.NpcKills.PreviousHour is not { } previous)
            return Unavailable(id, label, model, catalog, context, "The hour before is missing (downtime or a gap), so there is no change to show.");

        var last = systems.NpcKills.LastHour;
        var bands = catalog.Heat["npcChange"];
        var groups = new byte[model.Count];
        for (var i = 0; i < model.Count; i++)
        {
            if (last[i] >= context.MinNpc)
                groups[i] = HeatGroup((last[i] - previous[i]) * direction, bands);
        }

        var sign = direction > 0 ? "+" : "−";
        var colours = HeatColours(catalog, context);
        return new MapFilter(id, label, groups, colours, HeatLegend(bands, colours, b => $"{sign}{b}"), Sizes: HeatSizes);
    }

    /// <summary>Every system dim, with a reason: the activity is not there (yet).</summary>
    private static MapFilter Unavailable(MapFilterId id, string label, MapModel model, ThemeCatalog catalog, MapFilterContext context, string notice) =>
        new(id, label, new byte[model.Count], HeatColours(catalog, context), [], notice);

    /// <summary>The activity columns for this map, or null with the reason there are none.</summary>
    private static ActivityMetrics? ColumnsFor(MapModel model, ActivityCurrent? activity, out string? notice)
    {
        notice = null;
        if (activity is null)
        {
            notice = "No activity data from the server yet.";
            return null;
        }

        // The columns line up with the map pack they were built for.
        if (activity.SdeBuild != model.SdeBuild || activity.Systems.NpcKills.LastHour.Count != model.Count)
        {
            notice = "The activity is for another map version; it updates shortly.";
            return null;
        }

        return activity.Systems;
    }

    /// <summary>Group 0 is quiet (below the first band), drawn faint so any activity stands out; groups 1–5 are the heat bands.</summary>
    private static List<Color> HeatColours(ThemeCatalog catalog, MapFilterContext context) =>
        [context.Quiet, .. Enumerable.Range(1, 5).Select(b => ThemeService.ToColor(catalog.Fixed[$"heat-{b}"]))];

    private static List<MapLegendItem> HeatLegend(IReadOnlyList<int> bands, List<Color> colours, Func<int, string> format) =>
        [new MapLegendItem("0", colours[0]), .. bands.Select((bound, b) => new MapLegendItem(format(bound), colours[b + 1]))];
}
