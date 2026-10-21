using System.Globalization;
using Avalonia.Media;
using NewEdenSoft.Hunters.Desktop.Theming;

namespace NewEdenSoft.Hunters.Desktop.Map;

public enum MapFilterId
{
    Security,
}

public sealed record MapLegendItem(string Label, Color Colour);

/// <summary>
/// A colour filter decides how each system is drawn: which colour group it belongs to, or whether
/// it is hidden. Security status is the first; activity, routes and fleet positions become filters
/// too, without changes to the renderer. Matches <c>web/hunters-web/src/map/filters.ts</c>.
/// </summary>
/// <param name="Groups">Colour group per system index: an index into <paramref name="Colours"/>, or <see cref="Hidden"/>.</param>
public sealed record MapFilter(MapFilterId Id, string Label, byte[] Groups, IReadOnlyList<Color> Colours, IReadOnlyList<MapLegendItem> Legend)
{
    /// <summary>A system in this group is not drawn, labelled or hoverable (its gates still are).</summary>
    public const byte Hidden = 255;
}

public static class MapFilters
{
    public static readonly IReadOnlyList<(MapFilterId Id, string Label)> All = [(MapFilterId.Security, "Security")];

    public static MapFilter Create(MapFilterId id, MapModel model, ThemeCatalog catalog) => id switch
    {
        MapFilterId.Security => Security(model, catalog),
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
}
