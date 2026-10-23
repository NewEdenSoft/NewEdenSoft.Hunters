# Architecture

## Components

```
                         EVE SSO          zKillboard live feed      (EVE-Scout, later)
                            ▲                     │
                            │                     ▼
┌──────────────────────────── Hunters.Server (ASP.NET Core) ────────────────────────────┐
│ EVE SSO (confidential, client secret) · character registration · token rotation      │
│ Accounts · admission policy · discovery document                                      │
│ REST API · SignalR hub (fleet, relevant live kills)                                   │
│ Workers: SDE → map pack · admission recheck                                           │
│ Optional: static hosting of the web app                                               │
│ PostgreSQL · cache / presence / message bus interfaces (in-memory first)              │
└───────────────────────────────────────────────────────────────────────────────────────┘
        ▲ reads the intel schema (D22)
┌───────┴───────────────── Hunters.Intel (worker, no HTTP) ─────────────────────────────┐
│ SDE catalogue · ESI hourly kills/jumps · zKill ingest (planned) · derived tables       │
└───────────────────────────────────────────────────────────────────────────────────────┘
        ▲ REST + SignalR                                   ▲ REST + SignalR (same origin)
┌───────┴──────────────────────────┐            ┌──────────┴───────────────┐
│ Hunters.Desktop (Avalonia)       │            │ hunters-web (React)      │
│ any server · overlays · log watch│            │ cookie session           │
└───────────────┬──────────────────┘            └──────────┬───────────────┘
                └──────────────► ESI (character calls) ◄───┘
```

## Solution layout

```
src/
  Hunters.Domain            Map graph, path finding, filters. No I/O.
  Hunters.Contracts         DTOs, discovery document, SignalR hub interfaces. Versioned public API.
  Hunters.Server            ASP.NET Core host, EF Core (Postgres), auth, accounts, workers, optional web hosting.
  Hunters.Intel             Worker service: collects public activity data and derives the intel tables from it (D22).
  Hunters.Intel.Data        The intel schema: IntelDbContext, entities, migrations. Read by the server, written by intel.
  Hunters.Sde               CCP's SDE through NewEdenKit.Sde: map data (with 3D positions) and item types. Used by both.
  Hunters.Client            .NET client SDK: server profiles, login, token provider, REST + SignalR, ESI setup.
  Hunters.Desktop           Avalonia app (CommunityToolkit.Mvvm), Skia map control, overlay windows.
  Hunters.AppHost           .NET Aspire host for local development.
  Hunters.ServiceDefaults   Aspire service defaults (telemetry, health checks).
web/
  hunters-web               React + TypeScript + Vite. Types generated from the server's OpenAPI document.
tests/
  Hunters.Domain.Tests
  Hunters.Server.Tests      Integration tests, including a fake EVE SSO.
  Hunters.Intel.Tests       Intel service tests against PostgreSQL (Testcontainers), with a fake ESI.
```

Build settings: `net10.0`, nullable enabled, warnings as errors, central package management (`Directory.Packages.props`).

## Server layout

The server is organised in vertical feature slices on top of shared infrastructure. The test project mirrors the same folders.

```
src/Hunters.Server/
  Program.cs                composes infrastructure and features
  Configuration/            HuntersOptions (the "Hunters" config section)
  Features/                 one folder per slice: services, endpoints, and a *Feature.cs with its registrations
    Accounts/               accounts, characters, access tokens for client ESI calls
    Admin/                  access keys and account administration
    Admission/              admission policy and periodic recheck
    Auth/                   logins (desktop and web), sessions, authentication schemes
    Discovery/              /.well-known/hunters
  Infrastructure/           shared, feature-independent building blocks
    Caching/ Messaging/ Presence/   hot-state interfaces with in-memory implementations
    Eve/                    EVE SSO (OAuth handler, token client, signing keys, token encryption) and ESI
    Persistence/            EF Core DbContext, entities, migrations
    WebHosting/             serving the bundled web app
```

A feature may use infrastructure and, where needed, another feature's services (for example Auth uses Accounts and Admission). Infrastructure never depends on features.

## Responsibility split for character data

The server handles **character registration and token rotation only**. All character-scoped ESI calls (location, online, ship, waypoints, …) are made by the clients, desktop and web alike, using access tokens issued by the server. Clients report what other users need to see (for example fleet positions) to the server.

When no client is open for a character, nothing tracks it.

## Data ownership

| Data | Owner | Notes |
|---|---|---|
| SDE / map pack | Server | Clients download the map pack, never the SDE. |
| Accounts, characters, sessions | Server | See [Accounts](accounts.md). |
| Refresh tokens | Server only | Consumed on use; a single owner avoids invalidating each other. See [Authentication](authentication.md). |
| Access tokens | Issued by server | Desktop and web request them and call ESI themselves. |
| Character location, ship, online | Clients | Polled from ESI by the client; reported to the server only for sharing (fleet). |
| System kills/jumps history | Intel service | Public ESI data, polled hourly, stored for trends in the `intel` schema (D22). |
| Live kills | Server | Ingested once from zKillboard, forwarded only to interested clients. See [Map & Activity](map-and-activity.md#live-kill-feed). |
| Fleet sessions | Server | Stored in Postgres; hot state in the presence store. See [Fleet Sessions](fleet-sessions.md). |

## Cache, presence and messaging abstractions

Hot state goes through interfaces so the server can run as one instance with in-memory implementations now, and switch to Redis or another distributed store later without changing feature code.

| Interface | Purpose | First implementation | Later |
|---|---|---|---|
| `ICacheStore` | Key/value with TTL (map pack bytes, affiliation lookups, token cache) | In-memory | Redis / `IDistributedCache` |
| `IPresenceStore` | Live per-session and per-connection state: fleet member positions, live-kill subscriptions | In-memory | Redis |
| `IMessageBus` | Publish/subscribe between server instances (fleet events, kill fan-out) | In-process | Redis pub/sub |

Running more than one instance also needs a SignalR backplane (for example the Redis backplane) and a single elected zKill ingest worker. Not needed for single-instance deployments.

## Shared logic between .NET and the web

Path finding, filters and the ESI polling behaviour run on both clients, so they exist in `Hunters.Domain` / `Hunters.Client` (C#) and in the web app (TypeScript). Path finding and filters are verified against the same golden test cases.
