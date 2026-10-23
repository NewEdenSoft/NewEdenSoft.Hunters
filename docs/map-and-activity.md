# Map & Activity

## Map pack

The server loads the SDE with NewEdenKit.Sde and builds a compact **map pack**, versioned by SDE build number. Clients download it once and cache it by build number (not by server), so switching servers does not re-download it.

- **When:** in the background at startup (`MapPackService`), then every `Hunters:Sde:RefreshInterval` it checks CCP for a new build. The archive (about 100 MB) is downloaded only for a new build and cached in `Hunters:Sde:CachePath`. Until the first load finishes, the discovery document has no `mapPack` and the endpoint answers 503.
- **Where:** `GET /api/map/packs/{sdeBuild}` (session required). The discovery document's `mapPack.sdeBuild` names the current build; other builds are 404. The response is immutable and served precompressed (Brotli or gzip).
- **What:** known space, meaning every k-space system (id below 31,000,000) with at least one stargate. That leaves out wormhole space, Abyssal deadspace and the unreachable Jove systems, and keeps Pochven and Zarzakh.

| Contents | Fields |
|---|---|
| Regions | id, name |
| Systems | id, name, region, constellation, true security, `topDown` and `flat` positions |
| Gates | `from`, `to` (each connection once, `from` < `to`) |

Positions are in light years, oriented for screens (x east, y south). Each system has two layouts:

- `topDown`: the 3D position seen from above (SDE `x`, `z`), like the in-game star map. See [2D projection](#2d-projection).
- `flat`: CCP's own flattened layout (SDE `position2D`), like the in-game 2D map.

SDE build 3632936: 5,268 systems, 6,989 gates, 68 regions; 112 KB with Brotli (1 MB uncompressed).

## 2D projection

The first map style is a top-down projection of the SDE's 3D coordinates: systems are placed by their `x` and `z`, and `y` is dropped. This matches the in-game star map seen from above.

Region bounding boxes are precomputed so clients can zoom to a region directly.

Wormhole space is not in the map pack (no stargates). The SDE now also has CCP's flattened layout (`position2D`), which the map pack carries as `flat`; which layout the clients use by default is decided by trying both.

## Path finding

Runs on the client so hover previews are instant. The gate graph (~8k systems, ~14k connections) is small enough for a breadth-first or Dijkstra search in well under a millisecond.

- Implemented in `Hunters.Domain` (C#) and in the web app (TypeScript), checked against shared golden test cases.
- Preferences: shortest, prefer safer, avoid listed systems.
- Hover a system → route from the active character's system is highlighted.
- Later: dynamic edges for Thera/Turnur connections (EVE-Scout).

## Activity sources

| Source | Freshness | Handling |
|---|---|---|
| ESI `universe/system_kills` | Hourly | The intel service stores each hour for trends (below). |
| ESI `universe/system_jumps` | Hourly | As above. |
| zKillboard R2Z2 feed | Near real time | The intel service stores every kill ([Kills](#kills)); live forwarding to clients is [Live kill feed](#live-kill-feed). |

Clients fetch the hourly activity over HTTP ([Serving activity](#serving-activity)) and revalidate it with its ETag; live kills come later.

### Hourly kills and jumps

The intel service ([D22](decisions.md#d22--a-separate-intel-service-with-a-derived-store), `src/Hunters.Intel/Activity`) collects ESI's public `universe/system_kills` (ship, pod and NPC kills) and `universe/system_jumps` for everyone. Each list covers the hour ending at its `Last-Modified` time and lists only systems with activity (about 3,000 systems with kills and 5,000 with jumps an hour). `SystemActivityService` fetches both at startup and again 30 seconds after ESI's `Expires` (never sooner than a minute), retries failures after 1, 2, 4, 8 and then every 15 minutes, and stores each hour once.

Each hour is one raw row per list (`intel.SystemKills`, `intel.SystemJumps`), keyed by the end of its hour, with the systems and counts as parallel integer arrays: about 2,200 rows for 90 days instead of tens of millions with one per system. The raw rows are the source of truth; everything below is derived from them and can be rebuilt.

### Derived activity tables

After storing a new hour the collector signals `ActivityAggregator`, which also runs at startup and at least hourly. In one transaction (with a Postgres advisory lock, so a rebuild and the service take turns) it:

1. **Expires** hours older than `Hunters:Activity:Retention` (90 days: about 13 samples for each hour of the week): subtracts them from the sums below, then deletes them.
2. **Applies** each new hour (a raw row's `Applied` flag) to its **hour of week**: 0 = Monday 00:00 UTC … 167 = Sunday 23:00. An hour ending at `Last-Modified` 20:00:48 mostly covers 19:00, so its bucket is that of `PeriodEnd − 30 min`.
   - `SystemActivityWeek` and `RegionActivityWeek`: per system or region and hour of week, the **sums** of NPC, ship and pod kills and jumps (one `INSERT … ON CONFLICT` per hour; regions summed from the SDE catalogue). Only rows with activity exist; a missing row is zero.
   - `ActivityWeekSamples`: how many kill and jump hours each hour of week holds. The **average** for an hour of week is sum ÷ samples, so quiet hours count as zeros without being stored.
3. **Replaces** `SystemActivityCurrent` and `RegionActivityCurrent` (about 8,000 rows), with per metric:
   - `LastHour`, `PreviousHour` (null unless the second-newest hour ended about an hour earlier, so a delta across downtime or a gap isn't shown) and `Day` (the hours ending in the last 24 hours; `KillsDayHours`/`JumpsDayHours` say how many there were)
   - `Typical`: the average for the latest hour's hour of week ("usual for this hour")
   - **peak now**, for the hour after the latest data: `PeakRatio` (its average ÷ the busiest hour's, 0–1), `HoursToPeak` (hours until the next hour at `Hunters:Activity:PeakThreshold` or more, 0 = now, looking 24 hours ahead) and `PeakHourOfWeek`. These stay null until every hour of the week has `PeakMinWeeks` samples, and for systems whose busiest hour averages under `PeakMinAverage`, so a single kill in a quiet system doesn't light up as a peak.
4. Bumps the `activity` row of `intel.Versions`, which tells readers (the API server) to reload.

A new hour takes about 3 seconds at full size. `rebuild-activity` (see [Deployment](deployment.md)) clears the derived tables and applies every raw hour again, for when these rules change; incremental updates and a rebuild give the same tables.

### Serving activity

The API server only reads the `intel` schema (`src/Hunters.Server/Features/Activity`). `ActivityService` checks the `activity` version every `Hunters:Activity:PollInterval` (30 seconds) and, when it moved, loads the current tables and the sample counts into memory in one repeatable-read snapshot. Until the intel service has created its schema and stored an hour, there is no activity: the endpoints answer 503 (`activity_unavailable`) and the discovery document doesn't list the `activity` feature. All endpoints need a session (D19).

| Endpoint | Returns |
|---|---|
| `GET /api/activity/current` | Every system and region, **columnar** and in the order of the current map pack's `systems` and `regions` (`sdeBuild` names it), per metric: `lastHour`, `previousHour` (null for all when there is no previous hour), `day`, `typical`, `peakRatio`, `hoursToPeak`, `peakHourOfWeek`. Systems without activity are zeros. Built once per (version, map build) and served precompressed, like the map pack. |
| `GET /api/activity/systems/{id}` | One system: the current values and `week`, 168 averages per metric (sum ÷ samples) from Monday 00:00 UTC, null for hours without samples yet; `nowHourOfWeek` marks "now". 404 for systems not on the map. |
| `GET /api/activity/regions/{id}` | The same for a region. |

Every response carries an ETag from the version (304 when it still matches) and `Cache-Control: private, max-age` up to when the next hour is expected (the latest `Last-Modified` plus 62 minutes).

### Kills

The intel service's `KillFeedService` (`src/Hunters.Intel/Kills`) reads zKillboard's **R2Z2** feed: one file per kill at `r2z2.zkillboard.com/ephemeral/{sequence}.json`, with the full ESI killmail and zKillboard's notes, kept by zKillboard for about a day.

- **Reading:** one file at a time, 100 ms apart while catching up (zKillboard allows 15 requests a second), with a `User-Agent` naming the operator contact. A 404 is ambiguous, so it is compared with `sequence.json` (which lags a little behind the files): above it, the file isn't written yet and the reader waits 6 seconds, as zKillboard asks; at or below it, the file is gone and is skipped.
- **Position:** saved in `intel.FeedPositions` in the same save as each kill, so a restart resumes at the next file. A position older than `Hunters:Kills:ResumeWithin` (20 hours, before zKillboard drops the files) starts again at the newest file. The first start also begins at the newest.
- **Slim kills** (`intel.Kills`, about 35,000 a day, kept for `Hunters:Kills:Retention`, 90 days): id and hash, sequence, time, system, zKillboard's `npc` and `solo`, total value; the victim's character, corporation, alliance, ship (or structure, or deployable such as a CRAB beacon) and **fitted** module types (slots, rigs and subsystems, not cargo); and **per attacker** the character (0 for NPCs), ship and weapon, as parallel arrays. Hunting profiles count pilots per ship and smartbombers by weapon, so the attackers are kept whole. A kill announced twice is stored once.
- Failures back off like the ESI collector (1, 2, 4, 8, then every 15 minutes) and keep the position.

### Hunting profiles

A hunting profile is a kind of target: where it is seen, and when, is built up from the kills (`src/Hunters.Intel/Hunting`). The built-in profiles (`HuntingProfiles.All`) are defined in code:

| Profile | Seen when |
|---|---|
| `carriers` | a pilot flies a Carrier (group 547); **indicator:** a CRAB beacon killed, 0.75 |
| `supers` | a pilot flies a Supercarrier (659) or Titan (30); **indicator:** a CRAB beacon killed, 0.75 |
| `dreads` | a pilot flies a Dreadnought (485) or Lancer Dreadnought (4594) |
| `fax` | a pilot flies a Force Auxiliary (1538) |
| `marauders` | a pilot flies a Marauder (900) |
| `ishtars` | a pilot flies an Ishtar (type 12005) |
| `smartbombers` | the victim has a Smart Bomb (group 72) fitted, or an attacker's last hit was one |
| `crab` | a CRAB beacon is killed: every type in the group of "CONCORD Rogue Analysis Beacon" (the CONCORD and the CONCORD-Carrier beacon) |

- **Pilots** are seen as the victim or as an attacker; attackers without a character (NPCs) are not.
- **Indicators** are indirect evidence with a weight below 1. Anchoring a CRAB beacon is mostly a capital ratting habit, so a beacon killed shows a capital ratted there even when no capital is on any killmail. Indicator sightings have no pilot, are flagged as indicators, and add their weight, so direct and indirect evidence combine but can still be told apart.
- **Resolving:** each rule's groups, types and type names are resolved against the SDE catalogue to a set of type ids, which is all matching looks at. What can't be found is logged as a warning and left out.

Every kill the feed stores is matched as it arrives, into **`intel.Sightings`**: profile, kill, system, time, pilot (0 for none), victim or attacker, the type that matched, weight, indicator, and `IsFleet`. They are inserted with `ON CONFLICT DO NOTHING`, and unique indexes are the rules:

- **One per pilot an hour:** one sighting per (profile, pilot, system, hour), so a pilot ratting all evening counts once an hour, not once a kill. Pilotless sightings count once per kill.
- **The same kill twice** (a repost, a retry, a backfill) adds nothing.

**Fleet fights** are kept out of the counts, but the sightings stay, so the thresholds can be tuned:

- a kill with more than `Hunters:Hunting:FleetAttackers` (25) attackers marks its sightings `IsFleet`
- `Hunters:Hunting:BattleKills` (20) player kills in the system within `Hunters:Hunting:BattleWindow` (15 minutes) either side of a sighting marks it `IsFleet`. Checked around each new player kill, so the earlier sightings of a growing fight are marked too.

**Backfill:** `intel.HuntingProfileStates` keeps the resolved definition each profile's sightings were built with. At startup and on each new SDE catalogue, a profile whose definition changed (a rule edited, or a new SDE build adding a carrier hull) or that is new has its sightings deleted and rebuilt from the stored kills; a profile that was removed loses its sightings. Sightings expire with their kills. The states also hold each profile's display name, which readers list the profiles from.

### Hunting aggregates

`HuntingAggregator` turns the sightings into **where** and **when** per profile, in `intel.HuntingSystemStats` (per profile and system) and `intel.HuntingRegionStats` (per profile and region, through the SDE catalogue; systems it doesn't know count only for themselves). Fleet sightings don't count; indicators count at their weight.

| Column | Meaning |
|---|---|
| `Week`, `Month` | weighted sightings in the last 7 and 30 days |
| `MonthIndicator` | the part of `Month` from indicators (CRAB beacons for Carriers and Supers) |
| `Score` | every kept sighting, its weight halving every `Hunters:Hunting:ScoreHalfLife` (14 days): recent habits count most |
| `LastSeen` | the latest sighting that counts |
| `Pilots` | different pilots in the last 30 days |
| `Hours` | weighted sightings per hour of the day (UTC), 24 entries, over all kept sightings |

- **How:** sightings are few (about one kill in five has one), so the tables are recomputed from them in SQL, one statement per table, rather than kept up incrementally. That's always right, including after a backfill or when a battle marks earlier sightings as fleet. At 90 days (about 650,000 sightings) a recompute takes about 1.5 seconds, all in Postgres.
- **When:** at startup, then whenever new sightings arrive (the kill reader and the profile service signal it), at most every `Hunters:Hunting:UpdateInterval` (5 minutes), and at least hourly so the windows and the decay move on.
- **Readers:** each recompute bumps the `hunting` row of `intel.Versions`, in the same transaction.

## Live kill feed

Two goals: clients only receive kills that are relevant to them, and the server is not overwhelmed by the feed, including during large fights.

### Pipeline

```
zKill feed ──► Ingest worker ──► bounded channel ──► Dispatcher ──┬──► rolling per-system counters
 (one consumer)  parse to slim      (drop oldest     dedupe by id  │
                 KillSummary         when full)                    └──► subscriber index lookup
                                                                          │
                                                                          ▼
                                                              per-connection batch buffers
                                                              (flush every ~1–2 s) ──► SignalR
```

1. **One consumer per deployment.** A single ingest worker reads the zKillboard feed. If the server ever runs multiple instances, one is elected (Postgres advisory lock) and fans out over `IMessageBus`.
2. **Parse to a slim summary immediately.** Only what the map needs is kept, and the raw payload is thrown away:
   ```
   KillSummary { killmailId, hash, systemId, time, victimShipTypeId, attackerCount, iskValue, isNpc, isSolo }
   ```
   Clients that want full details fetch the killmail themselves from ESI using `killmailId` + `hash`.
3. **Bounded channel.** The ingest worker writes to a bounded `Channel<KillSummary>`. If the dispatcher falls behind, the oldest items are dropped and a metric is recorded, so memory use cannot grow without limit.
4. **Counters always, pushes only when relevant.** Every kill updates rolling per-system counters (last 5 / 15 / 60 minutes), which are cheap. A kill is only forwarded if at least one connection subscribes to its system.

### Subscriptions

- Each client sends the set of system IDs it cares about. The client computes this set from the map graph: systems within N jumps of its characters, fleet members' areas, and pinned systems. It is updated when characters move.
- The server caps the set size (configurable, e.g. 1,000 systems) and keeps an index `systemId → connections` in `IPresenceStore`.
- Systems nobody subscribes to cost one counter update and nothing else.

### Burst protection

- Kills are batched per connection and flushed every 1–2 seconds instead of one message per kill.
- Each batch has a per-connection cap. Above the cap, kills are collapsed into a per-system summary ("+47 kills in X"), which the client shows as a fight indicator.
- The feed can be turned off per server (`features` in the discovery document).

**To verify in the activity phase:** the current zKillboard live-feed API and its usage rules.

## Filters

Filter rules live in `Hunters.Domain` (and their TypeScript mirror) and are evaluated on the client against the activity snapshot and live updates. Examples:

- Hide systems with no recorded activity
- Kills in the last N minutes above a threshold
- NPC / ship kills, jumps above a threshold
- Security band (high / low / null)
- Within N jumps of the active character
