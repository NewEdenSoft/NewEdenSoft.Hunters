# NewEdenSoft.Hunters

Hunters helps EVE Online players find targets. It tracks your registered characters, shows nearby systems with recent activity on an interactive map, and lets fleet members share locations and broadcast destinations.

- **Server**: ASP.NET Core (.NET 10), self-hostable with Docker Compose
- **Web app**: React + TypeScript, optionally hosted by the server
- **Desktop app**: Avalonia, connects to any Hunters server, with overlay widgets

## Status

Early development. See the [design documentation](docs/README.md) and [roadmap](docs/roadmap.md).

## Repository layout

```
src/
  Hunters.Domain            Map graph, path finding, filters (no I/O)
  Hunters.Contracts         Public API types shared by server and clients
  Hunters.Server            ASP.NET Core server
  Hunters.Client            .NET client SDK used by the desktop app
  Hunters.Desktop           Avalonia desktop app
  Hunters.AppHost           .NET Aspire host for local development
  Hunters.ServiceDefaults   Telemetry and health check defaults
web/hunters-web             React web app
tests/                      xUnit tests
```

## Development

Requirements: .NET 10 SDK, Node 22, Docker (for Postgres).

Run everything (Postgres, server, web dev server) with Aspire:

```sh
dotnet run --project src/Hunters.AppHost
```

The Aspire dashboard link is printed on startup. The web dev server proxies `/api`, `/auth`, `/hubs`, `/openapi` and `/.well-known` to the server.

Run the desktop app:

```sh
dotnet run --project src/Hunters.Desktop
```

Build and test:

```sh
dotnet build
dotnet test
cd web/hunters-web && npm ci && npm run lint && npm run build
```

## Self-hosting

See [Deployment](docs/deployment.md).

## License

[GNU General Public License v3.0](LICENSE)
