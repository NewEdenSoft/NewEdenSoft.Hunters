# Roadmap

Character registration and account management come first; every other feature builds on them.

## Phase 0 — Scaffold ✅
1. ✅ Repo layout, `Directory.Build.props` (`net10.0`, nullable, warnings as errors), `Directory.Packages.props`.
2. ✅ Projects: `Hunters.Domain`, `Hunters.Contracts`, `Hunters.Server`, `Hunters.Client`, `Hunters.Desktop`, `Hunters.AppHost`, `Hunters.ServiceDefaults`, tests, `web/hunters-web` (Vite + React + TS).
3. ✅ Server basics: discovery endpoint, EF Core + Postgres, Data Protection persisted to file system, `Web:Enabled` switch, `ICacheStore` / `IPresenceStore` / `IMessageBus` with in-memory implementations.
4. ✅ Dockerfile, `docker-compose.yml`, `.env.example`, GitHub Actions build and test.
5. ✅ Desktop app on the .NET Generic Host with dependency injection.
6. ✅ Design system: HUD styles, faction themes from `design/themes.json`, bundled fonts, theme picker in both apps.
7. ✅ Desktop widget framework: docked pages or floating overlays, per-widget opacity / lock / click-through (Windows), system tray, saved layout. Placeholder widgets only.

## Phase 1 — Character registration and account management
1. ✅ Server: confidential EVE flow, shared callback routed by `state`, web login, desktop `begin` / loopback redirect / `redeem`.
2. ✅ Accounts: account creation, add character, main character, ownership-change handling, character status, remove character, sessions list and revoke, delete account. See [Accounts](accounts.md).
3. ✅ Tokens: access-token endpoint, single-flight refresh, persist-before-return, encryption at rest.
4. ✅ Admission (optional): access keys, affiliation, character lists, periodic recheck. Admin endpoints for keys and accounts.
5. `Hunters.Client`: ✅ loopback login. To do: server profiles, credential storage, `ICharacterTokenProvider`, NewEdenKit.ESI with file cache.
6. Desktop: ✅ login from the Account page (session kept in memory). To do: saved servers, trust prompt, account screen, character list with live location from its own ESI calls.
7. Web: login, account screen, character list with live location from its own ESI calls.
8. ✅ Integration tests with a fake EVE SSO against PostgreSQL (Testcontainers). Self-hosting README still to write.

## Phase 2 — Static map
Map pack builder (SDE → 2D projection), map rendering in desktop and web, "you are here".

## Phase 3 — Activity
ESI kills/jumps worker with history, zKillboard ingest pipeline with subscriptions, activity colouring, filters.

## Phase 4 — Path finding
Route on hover, avoid lists, jump-range highlight.

## Phase 5 — Fleet sessions
Invites, shared locations, destination broadcast, waypoint setting.

## Phase 6 — Desktop extras
Real widget content for each feature, local log watcher, macOS overlay support (click-through via `NSWindow.ignoresMouseEvents`), app icon.

## Later
- Thera / Turnur routes via EVE-Scout
- Intel channel parsing
- Alerts (kills within N jumps)
- Admin page in the web app
- Schematic (Dotlan-style) map layout
- Account merging
- Redis implementations of the cache, presence and message-bus interfaces

## Open questions
- Wormhole systems on the map: separate layer or excluded?
- Fleet session lifetime and roles.

## Feature backlog
_Add new feature ideas here before they are designed._
