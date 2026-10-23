using Microsoft.EntityFrameworkCore;
using NewEdenSoft.Hunters.Intel.Data;
using Npgsql;
using NpgsqlTypes;

namespace NewEdenSoft.Hunters.Intel.Hunting;

/// <summary>Finds hunting profiles' targets on a kill.</summary>
public static class SightingMatcher
{
    /// <summary>
    /// The sightings on one kill: per profile, each pilot whose ship or weapon matches (victim or
    /// attacker), the victim's fitted modules, and pilotless victims such as CRAB beacons. A pilot or
    /// victim matched by several rules is seen once, with the strongest evidence.
    /// </summary>
    /// <param name="fleetAttackers">More attackers than this marks the kill's sightings as a fleet fight.</param>
    public static List<Sighting> Match(Kill kill, IEnumerable<ResolvedProfile> profiles, int fleetAttackers)
    {
        var found = new Dictionary<(string Profile, long Pilot), Sighting>();

        void See(ResolvedProfile profile, ResolvedRule rule, long pilot, SightingRole role, int type)
        {
            var key = (profile.Id, pilot);
            if (found.TryGetValue(key, out var seen) && (seen.Weight > rule.Weight || seen.Weight == rule.Weight && !seen.Indicator))
                return;
            found[key] = new Sighting
            {
                Profile = profile.Id,
                KillmailId = kill.KillmailId,
                SystemId = kill.SystemId,
                Time = kill.Time,
                Hour = new DateTimeOffset(kill.Time.UtcTicks / TimeSpan.TicksPerHour * TimeSpan.TicksPerHour, TimeSpan.Zero),
                PilotId = pilot,
                Role = role,
                TypeId = type,
                Weight = rule.Weight,
                Indicator = rule.Indicator,
                IsFleet = kill.AttackerCount > fleetAttackers,
                AttackerCount = kill.AttackerCount,
            };
        }

        foreach (var profile in profiles)
        {
            foreach (var rule in profile.Rules)
            {
                switch (rule.Kind)
                {
                    case RuleKind.Ship:
                        if (rule.Types.Contains(kill.VictimShipTypeId))
                            See(profile, rule, kill.VictimCharacterId ?? 0, SightingRole.Victim, kill.VictimShipTypeId);
                        for (var i = 0; i < kill.AttackerCharacterIds.Length; i++)
                        {
                            if (kill.AttackerCharacterIds[i] != 0 && rule.Types.Contains(kill.AttackerShipTypeIds[i]))
                                See(profile, rule, kill.AttackerCharacterIds[i], SightingRole.Attacker, kill.AttackerShipTypeIds[i]);
                        }

                        break;

                    case RuleKind.Module:
                        if (kill.VictimFittedTypeIds.FirstOrDefault(rule.Types.Contains) is var fitted and not 0)
                            See(profile, rule, kill.VictimCharacterId ?? 0, SightingRole.Victim, fitted);
                        for (var i = 0; i < kill.AttackerCharacterIds.Length; i++)
                        {
                            if (kill.AttackerCharacterIds[i] != 0 && rule.Types.Contains(kill.AttackerWeaponTypeIds[i]))
                                See(profile, rule, kill.AttackerCharacterIds[i], SightingRole.Attacker, kill.AttackerWeaponTypeIds[i]);
                        }

                        break;

                    case RuleKind.Weapon:
                        if (rule.Victims is { } victims && !victims.Contains(kill.VictimShipTypeId))
                            break;
                        for (var i = 0; i < kill.AttackerCharacterIds.Length; i++)
                        {
                            if (kill.AttackerCharacterIds[i] != 0 && rule.Types.Contains(kill.AttackerWeaponTypeIds[i]))
                                See(profile, rule, kill.AttackerCharacterIds[i], SightingRole.Attacker, kill.AttackerWeaponTypeIds[i]);
                        }

                        break;

                    case RuleKind.Victim:
                        // The beacon's owner is not the pilot seen: the evidence is the beacon itself.
                        if (rule.Types.Contains(kill.VictimShipTypeId))
                            See(profile, rule, 0, SightingRole.Victim, kill.VictimShipTypeId);
                        break;
                }
            }
        }

        return [.. found.Values];
    }
}

/// <summary>Writes sightings and marks the ones in fleet fights. Every statement can safely run twice.</summary>
public static class SightingStore
{
    /// <summary>Inserts sightings; one already there (the same kill, or the same pilot that hour) is skipped. Returns how many were new.</summary>
    public static Task<int> InsertAsync(IntelDbContext db, IReadOnlyCollection<Sighting> sightings, CancellationToken cancellationToken)
    {
        if (sightings.Count == 0)
            return Task.FromResult(0);

        return db.Database.ExecuteSqlRawAsync("""
            INSERT INTO intel."Sightings" ("Profile", "KillmailId", "SystemId", "Time", "Hour", "PilotId", "Role", "TypeId", "Weight", "Indicator", "IsFleet", "AttackerCount")
            SELECT * FROM unnest(@profile, @kill, @system, @time, @hour, @pilot, @role, @type, @weight, @indicator, @fleet, @attackers)
            ON CONFLICT DO NOTHING
            """,
            [
                Array("profile", NpgsqlDbType.Text, sightings.Select(s => s.Profile)),
                Array("kill", NpgsqlDbType.Bigint, sightings.Select(s => s.KillmailId)),
                Array("system", NpgsqlDbType.Integer, sightings.Select(s => s.SystemId)),
                Array("time", NpgsqlDbType.TimestampTz, sightings.Select(s => s.Time)),
                Array("hour", NpgsqlDbType.TimestampTz, sightings.Select(s => s.Hour)),
                Array("pilot", NpgsqlDbType.Bigint, sightings.Select(s => s.PilotId)),
                Array("role", NpgsqlDbType.Smallint, sightings.Select(s => (short)s.Role)),
                Array("type", NpgsqlDbType.Integer, sightings.Select(s => s.TypeId)),
                Array("weight", NpgsqlDbType.Double, sightings.Select(s => s.Weight)),
                Array("indicator", NpgsqlDbType.Boolean, sightings.Select(s => s.Indicator)),
                Array("fleet", NpgsqlDbType.Boolean, sightings.Select(s => s.IsFleet)),
                Array("attackers", NpgsqlDbType.Integer, sightings.Select(s => s.AttackerCount)),
            ],
            cancellationToken);
    }

    /// <summary>
    /// Marks sightings in a battle as fleet: at least <see cref="HuntingOptions.BattleKills"/> player kills
    /// in the system within <see cref="HuntingOptions.BattleWindow"/> either side. Live, for the window
    /// around a new kill (earlier sightings of a growing fight get marked too); for a backfill, for whole
    /// profiles. Profiles in <paramref name="keepingFights"/> are left alone: bursts of kills are their
    /// target. Returns how many were marked.
    /// </summary>
    public static Task<int> MarkBattlesAsync(
        IntelDbContext db, HuntingOptions options, int? systemId, DateTimeOffset? around, string[]? profiles, string[] keepingFights, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync("""
            UPDATE intel."Sightings" s SET "IsFleet" = true
            WHERE NOT s."IsFleet"
              AND (@system::int IS NULL OR s."SystemId" = @system)
              AND (@around::timestamptz IS NULL OR s."Time" BETWEEN @around - @window AND @around + @window)
              AND (@profiles::text[] IS NULL OR s."Profile" = ANY(@profiles))
              AND NOT (s."Profile" = ANY(@keeping))
              AND (SELECT count(*) FROM intel."Kills" k
                   WHERE k."SystemId" = s."SystemId" AND NOT k."Npc"
                     AND k."Time" BETWEEN s."Time" - @window AND s."Time" + @window) >= @battle
            """,
            [
                new NpgsqlParameter("system", NpgsqlDbType.Integer) { Value = (object?)systemId ?? DBNull.Value },
                new NpgsqlParameter("around", NpgsqlDbType.TimestampTz) { Value = (object?)around ?? DBNull.Value },
                new NpgsqlParameter("profiles", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = (object?)profiles ?? DBNull.Value },
                new NpgsqlParameter("keeping", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = keepingFights },
                new NpgsqlParameter("window", NpgsqlDbType.Interval) { Value = options.BattleWindow },
                new NpgsqlParameter("battle", NpgsqlDbType.Integer) { Value = options.BattleKills },
            ],
            cancellationToken);

    private static NpgsqlParameter Array<T>(string name, NpgsqlDbType type, IEnumerable<T> values) =>
        new(name, NpgsqlDbType.Array | type) { Value = values.ToArray() };
}
