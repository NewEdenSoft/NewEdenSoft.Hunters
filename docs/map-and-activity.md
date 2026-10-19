# Map & Activity

## Map pack

The server loads the SDE with NewEdenKit.SDE and builds a compact **map pack**, versioned by SDE build number. Clients download it once and cache it by build number (not by server), so switching servers does not re-download it.

Contents:

- Systems: id, name, security status, constellation, region, 2D position
- Regions and constellations: id, name, 2D bounding box
- Stargate edges (system ↔ system)

The full SDE is hundreds of MB; the map pack is expected to be a few hundred KB.

## 2D projection

The first map style is a top-down projection of the SDE's 3D coordinates: systems are placed by their `x` and `z`, and `y` is dropped. This matches the in-game star map seen from above.

Region bounding boxes are precomputed so clients can zoom to a region directly.

**TBD:** whether wormhole systems (no stargates) are a separate layer or excluded. A Dotlan-style schematic layout may be added later; the SDE has no schematic layout data.

## Path finding

Runs on the client so hover previews are instant. The gate graph (~8k systems, ~14k connections) is small enough for a breadth-first or Dijkstra search in well under a millisecond.

- Implemented in `Hunters.Domain` (C#) and in the web app (TypeScript), checked against shared golden test cases.
- Preferences: shortest, prefer safer, avoid listed systems.
- Hover a system → route from the active character's system is highlighted.
- Later: dynamic edges for Thera/Turnur connections (EVE-Scout).

## Activity sources

| Source | Freshness | Handling |
|---|---|---|
| ESI `universe/system_kills` | Hourly | Server worker polls with ETags and stores history for trends. |
| ESI `universe/system_jumps` | Hourly | As above. |
| zKillboard live feed | Near real time | See [Live kill feed](#live-kill-feed). |

Clients receive an activity snapshot on connect, then live updates.

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
