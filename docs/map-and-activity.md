# Map & Activity

## Map pack

The server loads the SDE with NewEdenKit.Sde and builds a compact **map pack**, versioned by SDE build number. Clients download it once and cache it by build number (not by server), so switching servers does not re-download it.

- **When:** in the background at startup (`MapPackService`), then every `Hunters:Sde:RefreshInterval` it checks CCP for a new build. The archive (about 100 MB) is downloaded only for a new build and cached in `Hunters:Sde:CachePath`. Until the first load finishes, the discovery document has no `mapPack` and the endpoint answers 503.
- **Where:** `GET /api/map/packs/{sdeBuild}?format={format}` (session required). The discovery document's `mapPack` names the current `sdeBuild` and `format`; other builds are 404. The response is immutable and served precompressed (Brotli or gzip). The format only makes the URL new, so a client never uses a cached pack of an older format (`MapPack.Format`; 2 added `height`).
- **What:** known space, meaning every k-space system (id below 31,000,000) with at least one stargate. That leaves out wormhole space, Abyssal deadspace and the unreachable Jove systems, and keeps Pochven and Zarzakh.

| Contents | Fields |
|---|---|
| Regions | id, name |
| Systems | id, name, region, constellation, true security, `topDown` and `flat` positions, `height` |
| Gates | `from`, `to` (each connection once, `from` < `to`) |

Positions are in light years, oriented for screens (x east, y south). The SDE gives each system **two sets of coordinates**, and they are not interchangeable:

| SDE field | What it is | In the map pack | Use it for |
|---|---|---|---|
| `position` (`x`, `y`, `z`) | Where the system **is** in the world, in metres | `topDown` (`x`, `z`) and `height` (`y`) | Drawing the top-down layout, **and every distance** |
| `position2D` (`x`, `y`) | CCP's hand-made flattened layout of the in-game 2D map | `flat` | **Display only**: drawing the 2D layout. Never distances, ranges or anything else measured |

- `topDown`: the world position seen from above (SDE `x`, `z`), like the in-game star map. See [2D projection](#2d-projection).
- `flat`: CCP's flattened layout (SDE `position2D`), like the in-game 2D map. **For display only.** It is drawn to be readable, not to scale: neighbouring systems are spread apart and regions are moved, so distances on it are meaningless (Jita to Perimeter is 0.78 "ly" on it, 2.10 ly in the world; Jita to Amarr 28.35, against 20.28).
- `height`: the third axis of the world position (SDE `y`), in light years. With `topDown` it gives the straight-line distance between two systems, as jump drives measure it (`lyBetween` on the web, `MapModel.LyBetween` on the desktop), for jump ranges on the map. Positions and heights are rounded to 0.00001 ly, so distances are within about 0.00002 ly of the SDE's (checked over 200,000 random pairs), and a system at a range's edge lands on the right side.

SDE build 3632936: 5,268 systems, 6,989 gates, 68 regions; 112 KB with Brotli (1 MB uncompressed).

## 2D projection

The first map style is a top-down projection of the SDE's 3D coordinates: systems are placed by their `x` and `z`, and `y` is dropped. This matches the in-game star map seen from above.

Region bounding boxes are precomputed so clients can zoom to a region directly.

Wormhole space is not in the map pack (no stargates). The SDE now also has CCP's flattened layout (`position2D`), which the map pack carries as `flat` for display only (see above); CCP's 2D map is the clients' default layout.

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

- **Reading:** one file at a time, with a `User-Agent` naming the operator contact. A 404 is ambiguous, so it is compared with `sequence.json` (which lags a little behind the files): above it, the file isn't written yet and the reader waits 6 seconds, as zKillboard asks; at or below it, the file is gone and is skipped. `sequence.json` is asked again only for a file past its last answer, so a run of gaps costs one request per file.
- **Rate limit:** zKillboard blocks an address for an hour above 15 requests a second. Every request to R2Z2 (files and `sequence.json`) waits its turn at one gate for the process (`KillFeedGate`), at most `Hunters:Kills:MaxRequestsPerSecond` (5, so a second reader on the same address still stays under the limit; a day of kills catches up in about two hours). A block (429 or 403) pauses the feed for its `Retry-After`, or `Hunters:Kills:BlockedPause` (an hour) without one, instead of retrying into a longer block.
- **Position:** saved in `intel.FeedPositions` in the same save as each kill, so a restart resumes at the next file. A position older than `Hunters:Kills:ResumeWithin` (20 hours, before zKillboard drops the files) starts again at the newest file. The first start also begins at the newest.
- **Slim kills** (`intel.Kills`, about 35,000 a day, kept for `Hunters:Kills:Retention`, 90 days): id and hash, sequence, time, system, zKillboard's `npc` and `solo`, total value; the victim's character, corporation, alliance, ship (or structure, or deployable such as a CRAB beacon) and **fitted** module types (slots, rigs and subsystems, not cargo) with **how many of each** (one per slot; charges loaded in a module share its slot and count too); and **per attacker** the character (0 for NPCs), corporation, alliance, ship and weapon, as parallel arrays, and which attacker dealt the **final blow** (an index into them, from ESI's `final_blow`; -1 for kills stored before it was kept). The smartbomber and camp rules compare an attacker's corporation and alliance with the victim's, so the attackers are kept whole. Kills stored before the corporations were kept get them from ESI's `POST /characters/affiliation/` (up to 1,000 pilots a call; the corporation a pilot is in then, not at the kill) before a profile that needs them is rebuilt (`KillAffiliations`). A kill announced twice is stored once.
- Failures back off like the ESI collector (1, 2, 4, 8, then every 15 minutes) and keep the position.
- **Fetching stored kills again** (`KillRefetchService`): kills keep their `id` and `hash`, and ESI killmails never change, so what a slim kill didn't keep can be fetched again from ESI's `GET /killmails/{id}/{hash}/` rather than storing the raw files.
  - **Today:** the fitted counts of kills stored before they were kept. Only kills a rule needs counts for are fetched: with a smart bomb fitted, but fewer than five distinct smart bomb types (five distinct ones already prove five).
  - **Order and pace:** newest first, at `Hunters:Kills:Refetch:PerSecond` (1).
  - **Matching:** each kill fetched is matched again for the profiles that need the counts, so smartbombers fills back up gradually after the upgrade.
  - **Gone kills:** a killmail ESI no longer has gets zero counts, so it is not asked again.
  - **ESI limits:** a 420 or 429, or ESI's error limit dropping below 20, pauses the refetch until ESI says. Other failures back off.
  - **Resuming:** the work list is just "counts not known", so a restart resumes it. Once it is empty, it is looked at hourly (`Hunters:Kills:Refetch:IdleInterval`).
  - **One worker:** a database lock means only one worker refetches at a time.
  - **Logs:** Information logs give the kills left and an estimate of when it finishes.
  - **Switch:** `Hunters:Kills:Refetch:Enabled` turns it off.

### Hunting profiles

A hunting profile is a kind of target: where it is seen, and when, is built up from the kills (`src/Hunters.Intel/Hunting`). **What dies in a system is what lives there:** a capital that only helped kill something may have come for that one kill and left, so ships count when they die, not when they kill. The built-in profiles (`HuntingProfiles.All`) are defined in code. Each also has a **description** and its **evidence** (its rules in players' words, one line per way) written next to the rules, so a change to one is a change to the other. The profile service stores them with the name in `intel.HuntingProfileStates` on every sync, and the API serves them for the clients' info page.

| Profile | Seen when |
|---|---|
| `carriers` | a Carrier (group 547) dies; **indicator:** a CRAB beacon killed, 0.75 |
| `supers` | a Supercarrier (659) or Titan (30) dies; **indicator:** a CRAB beacon killed, 0.75 |
| `dreads` | a Dreadnought (485) or Lancer Dreadnought (4594) dies |
| `fax` | a Force Auxiliary (1538) dies |
| `marauders` | a Marauder (900) dies |
| `ishtars` | an Ishtar (type 12005) dies |
| `miners` | a Mining Barge (463), Exhumer (543), Industrial Command Ship (941: Orca, Porpoise) or Capital Industrial Ship (883: Rorqual) dies |
| `smartbombers` | a ship with **at least five smart bombs** fitted dies (Smart Bomb, group 72, any mix of sizes and damage types; one is a utility slot, not a smartbomber), or an attacker's area weapon (Smart Bomb, or Vorton Projector, 4060) kills a victim in **its own corporation or alliance**: friendly fire in a group that bombs together (smartbomb battleships catching a corp-mate's ship and pod, or their own Mobile Tractor Units; Thunderchild groups). **Appearances:** an area weapon on anyone else's loss (an alt in another corporation) shows the smartbomber there too, towards its [regulars](#regulars) |
| `crab` | a CRAB beacon is killed: every type in the group of "CONCORD Rogue Analysis Beacon" (the CONCORD and the CONCORD-Carrier beacon) |
| `smartbomb-camps` | an attacker's last hit was a Smart Bomb (72) **on a capsule** (29) of **another corporation and alliance**: PvP smartbombing, typically a camp on a low-sec pipe gate. A group's own pods caught in its bombs are friendly fire, not a camp. **Keeps fights:** a burst of pods is its target, so the battle rule doesn't apply to it (the 25-attacker rule still does) |

- **Pilots** are seen as the victim, or, for a weapon rule, as the attacker; attackers without a character (NPCs) are not.
- **Losses to NPCs count double** (`HuntingProfiles.NpcLossWeight`): a pilot killed by NPCs only (zKillboard's `npc`) died ratting there, the strongest sign it comes back. Pilotless victims (CRAB beacons) count as they are.
- **Locals count 1.5 times** (`HuntingProfiles.LocalWeight`, stacking with an NPC loss), and their sightings are flagged `Local`: whoever is seen belongs to the alliance that holds the system's sovereignty ([Sovereignty](#sovereignty)).
  - **Whoever is seen:** the victim, a CRAB beacon's owner (the victim on the killmail), or the attacker for a weapon rule.
  - **Held at the time of the kill:** a kill from before the claim's `ClaimedSince` is not local, since who held the system then is not known.
  - **Rebuilds:** a backfill uses the current map with that rule, so older sightings are only local when the same alliance still holds the system.
  - **First start:** the profiles wait for the first map (from the database, or ESI on the very first start) before they are built.
- **Rule kinds:** a dying pilot's **ship**; a **module** fitted on the dying ship, optionally only when at least a number of the rule's types are fitted together (smart bombs: five). Without the counts, each distinct type counts once, the fewest there could have been; a pilotless **victim** type (a deployable); an attacker's **weapon**, optionally only on given victims (smart bombs on capsules) and only on its **own side** (the victim in the attacker's corporation or alliance) or the **other side**. A kill whose attackers' corporations aren't known is not the attacker's own side.
- **Indicators** are indirect evidence with a weight below 1. Anchoring a CRAB beacon is mostly a capital ratting habit, so a beacon killed shows a capital ratted there even when no capital is on any killmail. Indicator sightings have no pilot, are flagged as indicators, and add their weight, so direct and indirect evidence combine but can still be told apart.
- **Resolving:** each rule's groups, types and type names are resolved against the SDE catalogue to a set of type ids, which is all matching looks at. What can't be found is logged as a warning and left out.

Every kill the feed stores is matched as it arrives, into **`intel.Sightings`**: profile, kill, system, time, pilot (0 for none), victim or attacker, the type that matched, weight, indicator, and `IsFleet`. They are inserted with `ON CONFLICT DO NOTHING`, and unique indexes are the rules:

- **One per pilot an hour:** one sighting per (profile, pilot, system, hour), so a pilot ratting all evening counts once an hour, not once a kill. Pilotless sightings count once per kill.
- **The same kill twice** (a repost, a retry, a backfill) adds nothing.

**Fleet fights** are kept out of the counts, but the sightings stay, so the thresholds can be tuned:

- a kill with more than `Hunters:Hunting:FleetAttackers` (25) attackers marks its sightings `IsFleet`
- `Hunters:Hunting:BattleKills` (20) player kills in the system within `Hunters:Hunting:BattleWindow` (15 minutes) either side of a sighting marks it `IsFleet`. Checked around each new player kill, so the earlier sightings of a growing fight are marked too.

**Backfill:** `intel.HuntingProfileStates` keeps the resolved definition each profile's sightings were built with. At startup and on each new SDE catalogue, a profile whose definition changed (a rule edited, or a new SDE build adding a carrier hull) or that is new has its sightings deleted and rebuilt from the stored kills; a profile that was removed loses its sightings.

**Manual rebuild:** an admin can ask for a rebuild without a restart (`POST /api/admin/hunting/rebuild`; see [Admins](discovery-and-admission.md#admins)).
- **The request:** the API server adds a row to `intel.HuntingRebuildRequests`. That is its one write to the intel schema.
- **Pickup:** the profile service looks for open requests every 30 seconds. It clears the stored definition of the profiles they name (all, for a request naming none), so the same backfill rebuilds them. Several open requests are carried out together.
- **Done or retried:** a request is marked completed once the rebuild succeeds. A failure backs off and tries again, with the last error noted on the request. Sightings expire with their kills. The states also hold each profile's display name, which readers list the profiles from.

### Sovereignty

The intel service's `SovereigntyService` (`src/Hunters.Intel/Sovereignty`) reads ESI's `GET /sovereignty/systems` and keeps the systems an **alliance** holds in `intel.SystemSovereignty`: the alliance, the corporation holding the claim, and `ClaimedSince`. Systems held by an NPC faction, or by nobody, have no row.

- **When:** at startup the stored map is published at once, so matching doesn't wait for ESI. ESI's list then replaces it whenever ESI has a new one (its `Expires`, hourly).
- **Failures:** they back off like the other collectors and keep the last map.
- **Use:** it tells a local from a passer-by in the [hunting profiles](#hunting-profiles).

### Hunting aggregates

`HuntingAggregator` turns the sightings into **where** and **when** per profile, in `intel.HuntingSystemStats` (per profile and system) and `intel.HuntingRegionStats` (per profile and region, through the SDE catalogue; systems it doesn't know count only for themselves). Fleet sightings don't count; indicators count at their weight.

| Column | Meaning |
|---|---|
| `Week`, `Month` | weighted sightings in the last 7 and 30 days |
| `MonthIndicator` | the part of `Month` from indicators (CRAB beacons for Carriers and Supers) |
| `Score` | every kept sighting, its weight halving every `Hunters:Hunting:ScoreHalfLife` (14 days): recent habits count most |
| `LastSeen` | the latest sighting that counts |
| `Pilots` | different pilots in the last 30 days |
| `MonthLocal`, `LocalPilots` | the parts of `Month` and `Pilots` from locals |
| `Hours` | weighted sightings per hour of the day (UTC), 24 entries, over all kept sightings |

- **How:** sightings are few (about one kill in five has one), so the tables are recomputed from them in SQL, one statement per table, rather than kept up incrementally. That's always right, including after a backfill or when a battle marks earlier sightings as fleet. At 90 days (about 650,000 sightings) a recompute takes about 1.5 seconds, all in Postgres.
- **When:** at startup, then whenever new sightings arrive (the kill reader and the profile service signal it), at most every `Hunters:Hunting:UpdateInterval` (5 minutes), and at least hourly so the windows and the decay move on.
- **Readers:** each recompute bumps the `hunting` row of `intel.Versions`, in the same transaction.

#### Regulars

A pilot seen in a system on **different days at about the same time of day** has a routine there and is likely back in that window. Regulars are recomputed with the aggregates, in the same transaction, into `intel.HuntingRegulars` (per profile, system and pilot). They come from the sightings of the last 30 days that have a pilot and are not fleet fights.

**Appearances** add to them. A smartbomber seldom dies, but it shows on the attacker list when its own alts die on its grid, and alts are often in other corporations, where the friendly-fire rule can't see them. A rule marked as appearance-only (`AppearanceOnly`) records such kills in `intel.HuntingAppearances`, apart from the sightings so they don't take a place in the one-an-hour rule.
- **Gate:** an appearance counts towards a pilot's regulars only when the profile already counts that pilot in the system in the last 30 days: dying with five smart bombs fitted, or friendly fire. A gate camper who never shows otherwise is not made a regular this way; camps have their own profile.
- **What it changes:** appearances count as days in the window and towards the strength, never towards the scores, windows or pilot counts.
- **Fleets and lifetime:** fleet fights (by attacker count) are kept but not counted. Appearances expire with their kills and are rebuilt with their profile.

- **Windows:** each of the 24 start hours (UTC) is tried for a **session window** of `Hunters:Hunting:RegularWindowHours` (4, a typical play session) and an **extended** one of `RegularExtendedWindowHours` (6). Both may run past midnight.
- **Regular:** a pilot is a regular when a window holds sightings on at least `RegularMinDays` (2) different days. Two sightings on one day are not a routine.
- **Which window:** the session window wins when it holds; otherwise the extended one. Among those, the one with the most days wins, then one starting at an hour the pilot was seen (the window reads "from when they show up"), then the earliest.
- **Columns:** `Days` (different days seen), `DaysInWindow`, `WindowStart`, `WindowHours`, `LastSeen`, and `Local` (seen as a local at least once).
- **Strength:** how reliable the routine is. It is the days in the window, each fading with the score's half-life (14 days) from its latest sighting there. A regular that only the extended window holds counts `RegularExtendedWeight` (0.5), weaker evidence than a session. Six days this week is a strong routine; two days a month ago hardly one.

**Reliable now:** every system row of `intel.HuntingSystemStats` gets a `Reliable` curve: per hour of the day (UTC), the strength of its regulars whose window covers that hour (zeros without regulars). A system is a reliable target at an hour when someone is reliably there then, which is what the map lights up for the current hour.

### Serving hunting

The API server's `HuntingService` (`src/Hunters.Server/Features/Hunting`) follows the `hunting` version like activity (every `Hunters:Activity:PollInterval`) and loads the stats tables and profile names into memory in one repeatable-read snapshot: a few thousand small rows, about 1–2 MB. Until the intel service has computed its first aggregates, the endpoints answer 503 (`hunting_unavailable`) and the discovery document doesn't list the `hunting` feature. All need a session (D19).

| Endpoint | Returns |
|---|---|
| `GET /api/hunting/profiles` | the profiles: `id`, `name`, `description` (what the target is) and `evidence` (what counts as seeing it, one line per way) |
| `GET /api/hunting/overview` | every profile, **sparse**: only systems with sightings, highest score first, as columns: `systemIds`, `score`, `week`, `month`, `lastSeen`, `hours` (24 per system), and `reliable` (24 per system, empty for systems never recomputed with it). Built once per version and served precompressed. For colouring the map by a profile. |
| `GET /api/hunting/systems/{id}` | the profiles seen in a system, highest score first, each with `score`, `week`, `month`, `monthIndicator` (indirect evidence), `pilots`, `monthLocal` and `localPilots` (locals), `lastSeen`, `hours`, `reliable` (24 by hour), and `regulars`: up to 10 per profile, session windows first, then the strongest (`pilotId`, `days`, `daysInWindow`, `windowStart`, `windowHours`, `lastSeen`, `local`, `strength`). Empty when none were; 404 for systems not on the map. |
| `GET /api/hunting/regions/{id}` | the same for a region, without regulars or `reliable` (empty lists) |

Responses carry an ETag from the version (304 when it still matches) and `max-age` of a minute. "Likely now" and "peak now" for a profile are worked out by the client from `hours` and the current hour: the share of the current hour, or the current hour's weight ÷ the busiest hour's.

### Recent kills

`GET /api/kills/systems/{id}?limit=20` (`src/Hunters.Server/Features/Kills`) returns a system's newest kills from `intel.Kills`, newest first: up to `limit` (1–50, 20 by default), over the whole retention. Each has the killmail id and hash, time, `npc`, `solo`, total value, the attacker count, the **victim** and the **final blow**, each as character, corporation, alliance and ship type ids (null where there are none: an NPC has no pilot). The final blow is null for kills stored before it was kept. Ids only: clients look up the names (ESI `POST /universe/names`) and draw the portraits and ship renders from the image server.

- The query uses the `(SystemId, Time)` index.
- **Availability:** the kills come from the same feed as the hunting profiles, so the endpoint answers 503 (`kills_unavailable`) until the hunting aggregates are in, and the discovery document lists `kills` with `hunting`.
- **Errors:** 404 (`kills_not_found`) for a system not on the map, 400 for a limit out of range.
- **Caching:** the ETag is the newest kill and the count; `max-age` is 30 seconds. A session is needed (D19).

## Reach

`GET /api/intel/reach?from={systemId}&jumps={n}&ly={x}&profile={id}&sort={order}&limit={n}` answers "what's in reach from here" ([D23](decisions.md#d23--range-queries-run-on-the-server-with-the-users-own-limits)), for example after a filament drops a Black Ops somewhere random.

- **The user's limits are the query:**
  - `jumps` (1–50): every system within that many gate jumps, whatever its light-year distance; a target 5 jumps away but out of jump range still shows (gate there, or use a midpoint)
  - `ly` (0.1–20): every **cyno target** within that many light years, however many gate jumps away; from Turnur, 8 ly reaches 103 cyno targets, 24 of them more than 10 gate jumps away
  - at least one is needed, and together they give the union
- **Every row carries both distances** (gate jumps, null when no gates lead there, and light years), the security status and `cynoTarget`. The client decides "in jump range" for its own ship: a cyno target within the user's light years. The server knows nothing about hulls or skills.
- **Cyno targets** (`Hunters.Domain`, `CynoTargets`): not high-sec (shown as 0.5 or more, the clients' rounding), not wormhole or other instanced space, not Pochven (region 10000070), not Zarzakh (30100000).
- **Geometry** (`Hunters.Domain`, `MapGeometry`): the map pack's systems and gates as arrays, with 3D positions in light years from the SDE, built with the map pack. A whole-map gate search plus the light-year scan takes about 0.2 ms (1 ms at the 50 jumps + 20 ly extreme).
- **Each row's intel:**
  - activity, per metric: last hour, previous hour, 24 hours, usual for the hour, peak ratio, hours to peak; null until the server has activity data
  - the hunting profiles seen there: score, last seen, **likely now** (the score times the current hour's share of the sightings) and **peak now** (the current hour against the busiest hour of the day)
- **Order:**
  - with a `profile`: its likely now, then its score
  - otherwise by `sort`: `npc-rising` (the default; the rise in NPC kills, last hour minus the hour before) or `npc-kills` (the most NPC kills in the last hour, then in 24 hours)

  Ties go to the nearest. The answer echoes `sort`; an unknown sort is a 400. `limit` caps the rows (default 200, at most 500); `total` says how many were in reach.
- The origin is included (0 jumps, 0 ly). An origin not on the map is 404, bad limits or an unknown profile 400. The ETag combines the map build, the activity and hunting versions and the hour, so an answer is revalidated cheaply until any of them moves.

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
