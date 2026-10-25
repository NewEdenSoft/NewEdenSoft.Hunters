using System.Globalization;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>A ship class's jump range with Jump Drive Calibration V, in light years.</summary>
public sealed record JumpRangePreset(string Id, string Label, double Ly);

/// <summary>Whether the jump range is drawn around the focused system, and how far it reaches. Off by default, 8 ly.</summary>
public sealed record JumpRangeSetting(bool Enabled = false, double Ly = 8);

/// <summary>Jump ranges, like the web's <c>map/jumpRange.ts</c>.</summary>
public static class JumpRanges
{
    public static readonly IReadOnlyList<JumpRangePreset> Presets =
    [
        new("blops", "Black Ops", 8),
        new("capital", "Carrier · Dread · FAX", 7),
        new("super", "Super · Titan", 6),
        new("jf", "Jump freighter · Rorqual", 10),
    ];

    /// <summary>The custom range is kept within this (light years).</summary>
    public const double MinLy = 0.5, MaxLy = 20;

    public static double Clamp(double ly) => Math.Clamp(ly, MinLy, MaxLy);

    /// <summary>Pochven: Triglavian space, no cynosural fields.</summary>
    private const int Pochven = 10000070;

    /// <summary>Zarzakh (in Yasna Zakh): no cynosural fields.</summary>
    private const int Zarzakh = 30100000;

    /// <summary>
    /// A jump drive can land here: a cynosural field can be lit. Not high-sec (shown as 0.5 or more), not
    /// Pochven, not Zarzakh. The same rule as the server's (<c>CynoTargets</c> in Hunters.Domain).
    /// </summary>
    public static bool IsCynoTarget(MapModel model, int index) =>
        model.RegionIds[index] != Pochven && model.Ids[index] != Zarzakh && SecurityStatus.Display(model.Security[index]) < 0.5;

    /// <summary>
    /// The systems a jump drive can reach from <paramref name="origin"/> (by index) within <paramref name="ly"/>
    /// light years: true for each cyno target in range, false for the rest and the origin itself.
    /// Distances are in 3D (<see cref="MapModel.LyBetween"/>).
    /// </summary>
    public static bool[] InRange(MapModel model, int origin, double ly)
    {
        var inRange = new bool[model.Count];
        for (var i = 0; i < model.Count; i++)
            inRange[i] = i != origin && IsCynoTarget(model, i) && model.LyBetween(origin, i) <= ly;
        return inRange;
    }

    /// <summary>A range as text: "8 ly", "7.50 ly", "12.3 ly".</summary>
    public static string FormatLy(double ly) =>
        (ly == Math.Floor(ly) ? ly.ToString("0", CultureInfo.InvariantCulture) : ly.ToString(ly < 10 ? "0.00" : "0.0", CultureInfo.InvariantCulture)) + " ly";
}
