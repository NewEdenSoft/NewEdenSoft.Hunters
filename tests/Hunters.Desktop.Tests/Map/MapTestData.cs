using NewEdenSoft.Hunters.Contracts.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

/// <summary>A small map: two compact regions and one scattered across the map.</summary>
internal static class MapTestData
{
    public const int Forge = 1, Lonetrek = 2, Pochven = 3;

    public static MapPack Pack() => new(
        42,
        [new MapRegion(Forge, "The Forge"), new MapRegion(Lonetrek, "Lonetrek"), new MapRegion(Pochven, "Pochven")],
        [
            System(10, "Jita", Forge, 100, 0.946, 0, 0),
            System(11, "Perimeter", Forge, 100, 0.95, 1, 0),
            System(12, "Niyabainen", Forge, 101, 0.03, 0, 1),
            System(20, "Otela", Lonetrek, 200, -0.2, 10, 0),
            System(21, "Ahynada", Lonetrek, 200, 0.5, 11, 0),
            System(30, "Kino", Pochven, 300, -1, -40, -40),
            System(31, "Ignebaener", Pochven, 300, -1, 40, 40),
        ],
        [new MapGate(10, 11), new MapGate(10, 12), new MapGate(11, 20), new MapGate(20, 21), new MapGate(21, 99)]);

    private static MapSystem System(int id, string name, int region, int constellation, double security, double x, double y) =>
        new(id, name, region, constellation, security, new MapPoint(x, y), new MapPoint(x * 2, y * 2));
}
