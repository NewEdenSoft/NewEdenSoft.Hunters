using NewEdenSoft.Hunters.Sde;

namespace NewEdenSoft.Hunters.Intel.Tests;

/// <summary>A tiny SDE: three systems in two regions, and a few types. Tests never download the real SDE.</summary>
public sealed class FakeSde : ISdeMapSource, ISdeTypeSource
{
    public const double Ly = SdeUnits.MetresPerLightYear;
    public const int TheForge = 10000002, Lonetrek = 10000016;
    public const int Jita = 30000142, Perimeter = 30000144, Sobaseki = 30001363;
    public const int CarrierGroup = 547, Thanatos = 23911, CrabBeacon = 60244, OldCrabBeacon = 99999, ShipCategory = 6;

    /// <summary>The real ids of what the real R2Z2 samples hold (a CRAB beacon killed by a Wyvern and a Hel).</summary>
    public const int SupercarrierGroup = 659, Wyvern = 23917, Hel = 22852, CarrierCrabBeacon = 92183, CrabGroup = 4137;

    public const int SmartBombGroup = 72, Smartbomb = 3993, Ishtar = 12005, Rifter = 587, Capsule = 670;

    public const int VortonGroup = 4060, Vorton = 54748, Thunderchild = 54733;

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
                new SdeType(Wyvern, "Wyvern", SupercarrierGroup, true),
                new SdeType(Hel, "Hel", SupercarrierGroup, true),
                new SdeType(Ishtar, "Ishtar", 358, true),
                new SdeType(Rifter, "Rifter", 25, true),
                new SdeType(Capsule, "Capsule", 29, true),
                new SdeType(Smartbomb, "Large Graviton Smartbomb II", SmartBombGroup, true),
                new SdeType(Vorton, "Large Vorton Projector II", VortonGroup, true),
                new SdeType(Thunderchild, "Thunderchild", 27, true),
                new SdeType(CrabBeacon, "CONCORD Rogue Analysis Beacon", CrabGroup, true),
                new SdeType(CarrierCrabBeacon, "CONCORD-Carrier Rogue Analysis Beacon", CrabGroup, true),
                // An unpublished copy with the same name in another group: the published one must win.
                new SdeType(OldCrabBeacon, "CONCORD Rogue Analysis Beacon", 4810, false),
            ],
            [
                new SdeGroup(CarrierGroup, "Carrier", ShipCategory), new SdeGroup(SupercarrierGroup, "Supercarrier", ShipCategory),
                new SdeGroup(358, "Heavy Assault Cruiser", ShipCategory), new SdeGroup(25, "Frigate", ShipCategory), new SdeGroup(29, "Capsule", ShipCategory),
                new SdeGroup(SmartBombGroup, "Smart Bomb", 7), new SdeGroup(VortonGroup, "Vorton Projector", 7),
                new SdeGroup(27, "Battleship", ShipCategory), new SdeGroup(CrabGroup, "Rogue Analysis Beacon", 22),
                new SdeGroup(4810, "Analysis Beacon", 22),
            ],
            [new SdeCategory(ShipCategory, "Ship"), new SdeCategory(7, "Module"), new SdeCategory(22, "Deployable")]));
    }
}
