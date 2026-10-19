# Overview

## Goals

- Track a user's registered EVE characters and show where they are.
- Show systems near those characters that have had recent activity (kills, jumps).
- Let users in a fleet session share their locations and broadcast destinations, and show fleet members on the map.
- Provide interacting map features: path finding when hovering a system, filters that hide systems without activity, and more to come.

## Applications

| Application | Technology | Notes |
|---|---|---|
| Server | ASP.NET Core (.NET 10) | Character registration, token rotation, accounts, SignalR, public activity data. Optionally hosts the web app. Self-hostable with Docker Compose. |
| Web app | React + TypeScript (Vite) | For users who don't want to install anything. Bound to the server that hosts it. Makes its own character ESI calls. |
| Desktop app | Avalonia (.NET 10) | Server agnostic. Makes its own character ESI calls. Adds overlay widgets with per-widget click-through so it can sit on top of EVE clients instead of needing a dedicated screen. |

Everything except the web app is .NET.

## External libraries

| Library | Use |
|---|---|
| [NewEdenKit.SDE](https://github.com/NewEdenKit/NewEdenKit.SDE) | Downloading, caching and loading versions of the EVE Static Data Export. |
| [NewEdenKit.ESI](https://github.com/NewEdenKit/NewEdenKit.ESI) | Typed ESI client with ETag caching. Takes an access token per call; does not do SSO. |

Both libraries are owned by the project author.

## External data sources

| Source | Data |
|---|---|
| EVE SSO | Character login, access/refresh tokens |
| ESI | Character location/online/ship, system kills & jumps, affiliations, waypoints |
| zKillboard live feed | Near real-time kills |
| EVE-Scout (later) | Thera / Turnur connections |

## License

Open source. License to be chosen to stay compatible with NewEdenKit (currently GPL-3.0-or-later; the author can relicense NewEdenKit if needed).
