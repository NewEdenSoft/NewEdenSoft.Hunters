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
| ESI `universe/system_kills` | Hourly | Server worker stores each hour for trends (below). |
| ESI `universe/system_jumps` | Hourly | As above. |
| zKillboard live feed | Near real time | See [Live kill feed](#live-kill-feed). |

Clients receive an activity snapshot on connect, then live updates.

### Hourly kills and jumps

`SystemActivityService` (server, `Features/Activity`) collects ESI's public `universe/system_kills` (ship, pod and NPC kills) and `universe/system_jumps` for everyone. Each list covers the hour ending at its `Last-Modified` time and lists only systems with activity (about 2,800 systems with kills and 4,800 with jumps an hour). The worker fetches both at startup and again 30 seconds after ESI's `Expires` (never sooner than a minute), retries failures after 1, 2, 4, 8 and then every 15 minutes, and stores each hour once.

Each hour is one row per list (`SystemKills`, `SystemJumps`), keyed by the end of its hour, with the systems and counts as parallel integer arrays: about 720 rows and roughly 20 MB for 30 days, instead of millions of rows with one per system. Hours older than `Hunters:Activity:Retention` (30 days) are deleted after each poll. `Hunters:Activity:Enabled` turns the worker off. An endpoint for clients comes next.

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
