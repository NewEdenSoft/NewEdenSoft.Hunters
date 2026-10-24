# NewEdenSoft.Hunters — Design Documentation

Hunters helps EVE Online players find targets. It tracks a user's registered characters, shows nearby systems with recent activity on an interactive map, and lets members of a fleet session share locations and broadcast destinations.

## Documents

| Document | Contents |
|---|---|
| [Overview](overview.md) | Goals, applications, technology choices |
| [Architecture](architecture.md) | Components, solution layout, data ownership |
| [Authentication](authentication.md) | EVE SSO flows for web and desktop, token handling, app sessions |
| [Accounts & Characters](accounts.md) | Account model, character registration, ownership changes, sessions |
| [Discovery & Admission](discovery-and-admission.md) | Server-agnostic desktop client, discovery document, optional admission policy |
| [Map & Activity](map-and-activity.md) | Map pack, 2D projection, path finding, activity sources, live kill feed, filters |
| [Fleet Sessions](fleet-sessions.md) | Shared locations, destination broadcast, storage |
| [Desktop Client](desktop-client.md) | Server profiles, ESI calls, overlays, log watcher, platform support |
| [Web Client](web-client.md) | Cookie session, browser ESI calls |
| [Desktop To-Do](desktop-todo.md) | Web features the desktop still has to mirror |
| [Design](design.md) | HUD visual direction, faction themes, fonts, components |
| [Deployment](deployment.md) | Docker Compose, configuration reference |
| [Decisions](decisions.md) | Decision log with rationale |
| [Roadmap](roadmap.md) | Phases, open questions, feature backlog |

## Status

Design phase. Nothing is implemented yet. Sections marked **TBD** are open.
