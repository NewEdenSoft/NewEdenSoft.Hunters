using NewEdenSoft.Hunters.Server.Features.Map;
using NewEdenSoft.Hunters.Server.Infrastructure.Sde;

namespace NewEdenSoft.Hunters.Server.Tests.Fakes;

/// <summary>
/// A tiny SDE: three connected known-space systems in two regions, plus systems the map pack must
/// leave out. Tests never download the real SDE.
/// </summary>
public sealed class FakeSdeMap : ISdeMapSource
{
    public const long Build = 3_000_001;
    public const double Ly = MapPackBuilder.MetresPerLightYear;

    /// <summary>Hold the next load until set, to observe the server before the map is ready.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public SdeMapData Data { get; set; } = new(
        Build,
        [new SdeRegion(10000002, "The Forge"), new SdeRegion(10000016, "Lonetrek"), new SdeRegion(10000004, "UUA-F4"), new SdeRegion(11000031, "G-R00031")],
        [
            new SdeSolarSystem(30000142, "Jita", 10000002, 20000020, 0.945913, -13 * Ly, 12 * Ly, -4 * Ly, 18 * Ly),
            new SdeSolarSystem(30000144, "Perimeter", 10000002, 20000020, 0.954, -12 * Ly, 12 * Ly, null, null),
            new SdeSolarSystem(30001363, "Sobaseki", 10000016, 20000199, 0.8, -12 * Ly, 15 * Ly, -3 * Ly, 19 * Ly),
            // Unreachable Jove system: known space, but no stargates.
            new SdeSolarSystem(30000380, "Polaris", 10000004, 20000056, -1, 0, 0, 0, 0),
            // Wormhole space.
            new SdeSolarSystem(31000005, "Thera", 11000031, 21000324, -0.99, 700 * Ly, 900 * Ly, null, null),
        ],
        [
            new SdeStargate(30000142, 30000144), new SdeStargate(30000144, 30000142),
            new SdeStargate(30000142, 30001363), new SdeStargate(30001363, 30000142),
        ]);

    public async Task<SdeMapData> LoadAsync(CancellationToken cancellationToken)
    {
        if (Gate is { } gate)
            await gate.Task.WaitAsync(cancellationToken);
        return Data;
    }
}
