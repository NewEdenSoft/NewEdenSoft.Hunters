# Fleet Sessions

A fleet session is an app-level group, not an in-game fleet. Importing in-game fleet membership may be added later.

## Behaviour

- A user creates a session and receives an invite code.
- Members who join share the location of their characters with the session.
- Members can broadcast a destination, with an optional route.
- Fleet members and broadcast destinations are shown on the map.
- A destination broadcast can optionally set the in-game waypoint. The receiving client makes that ESI call itself (`esi-ui.write_waypoint.v1`).

Sessions live on one server, so only accounts on that server can join.

## Storage

| Data | Store | Notes |
|---|---|---|
| Sessions, members, invites, roles | Postgres | Source of truth. Survives restarts. |
| Current destination broadcasts | Postgres | Written on change. |
| Live member positions, online state, connection map | `IPresenceStore` | High churn, short TTL. |
| Last-known member positions | Postgres | Written through from presence, throttled (e.g. at most every 30 s per character), so state can be restored after a restart. |
| Events to other server instances | `IMessageBus` | Only needed when scaled out. |

`IPresenceStore` and `IMessageBus` start with in-memory implementations. A Redis or other distributed implementation can be added later without changing fleet code. See [Architecture](architecture.md#cache-presence-and-messaging-abstractions).

## Real-time

- Each session is a SignalR group.
- Location updates come from clients (their own ESI polling, and the desktop log watcher). The server does not poll character locations.

**TBD:** session lifetime and expiry, roles (owner / member / moderator).
