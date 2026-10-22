using NewEdenSoft.Hunters.Sde;

namespace NewEdenSoft.Hunters.Intel.Tests;

/// <summary>A tiny SDE: three systems in two regions, and a few types. Tests never download the real SDE.</summary>
public sealed class FakeSde : ISdeMapSource, ISdeTypeSource
{
    public const double Ly = SdeUnits.MetresPerLightYear;
    public const int TheForge = 10000002, Lonetrek = 10000016;
    public const int Jita = 30000142, Perimeter = 30000144, Sobaseki = 30001363;
    public const int CarrierGroup = 547, Thanatos = 23911, CrabBeacon = 60244, OldCrabBeacon = 99999, ShipCategory = 6;

    public long Build { get; set; } = 3_000_001;

    public Exception? Failure { get; set; }

    public int Loads { get; private set; }

    public Task<SdeMapData> LoadAsync(CancellationToken cancellationToken)
    {
        Loads++;
        if (Failure is { } e)
            return Task.FromException<SdeMapData>(e);
        return Task.FromResult(new SdeMapData(
            Build,
            [new SdeRegion(TheForge, "The Forge"), new SdeRegion(Lonetrek, "Lonetrek")],
            [
                // Perimeter is 3 ly east and 4 ly up from Jita: 5 ly away in 3D.
                new SdeSolarSystem(Jita, "Jita", TheForge, 20000020, 0.945913, 0, 0, 0, null, null),
                new SdeSolarSystem(Perimeter, "Perimeter", TheForge, 20000020, 0.954, 3 * Ly, 4 * Ly, 0, null, null),
                new SdeSolarSystem(Sobaseki, "Sobaseki", Lonetrek, 20000199, 0.8, 0, 0, 12 * Ly, null, null),
            ],
            [new SdeStargate(Jita, Perimeter), new SdeStargate(Perimeter, Jita)]));
    }

    Task<SdeTypeData> ISdeTypeSource.LoadAsync(CancellationToken cancellationToken)
    {
        if (Failure is { } e)
            return Task.FromException<SdeTypeData>(e);
        return Task.FromResult(new SdeTypeData(
            Build,
            [
                new SdeType(Thanatos, "Thanatos", CarrierGroup, true),
                new SdeType(CrabBeacon, "CONCORD Rogue Analysis Beacon", 4810, true),
                // An unpublished copy with the same name: the published one must win.
                new SdeType(OldCrabBeacon, "CONCORD Rogue Analysis Beacon", 4810, false),
            ],
            [new SdeGroup(CarrierGroup, "Carrier", ShipCategory), new SdeGroup(4810, "Analysis Beacon", 22)],
            [new SdeCategory(ShipCategory, "Ship"), new SdeCategory(22, "Deployable")]));
    }
}
