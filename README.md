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

Postgres runs as a persistent container with a data volume (`newedensoft.hunters.apphost-…-postgres-data`): it keeps running after the AppHost stops, and the data (accounts, collected intel) carries over to the next run. Its generated password is in the AppHost's user secrets (`Parameters:postgres-password`); keep it, or the volume can't be opened. To start over, remove the container and the volume with `docker rm -f` and `docker volume rm`.

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

## Configuration

Logins use EVE SSO, so every server needs its own EVE application.

### 1. Register an EVE application

At [developers.eveonline.com](https://developers.eveonline.com/), create an application with:

| Field | Value |
|---|---|
| Connection type | Authentication & API Access |
| Scopes | `esi-location.read_location.v1`, `esi-location.read_online.v1`, `esi-location.read_ship_type.v1`, `esi-ui.write_waypoint.v1` |
| Callback URL | Local development: `http://localhost:5173/auth/eve/callback`. Self-hosted: `https://<your host>/auth/eve/callback` |

EVE allows one callback URL per application, so use separate applications for development and for a hosted server.

### 2a. Local development (Aspire)

The AppHost passes these to the server. It reads them from the `Parameters` section of its configuration: [`src/Hunters.AppHost/appsettings.json`](src/Hunters.AppHost/appsettings.json) holds the defaults, and you put your own values in `src/Hunters.AppHost/appsettings.Development.json`.

| Parameter | Required | Purpose |
|---|---|---|
| `eve-client-id` | Yes | Client ID of your EVE application |
| `eve-client-secret` | Yes | Secret key of your EVE application |
| `eve-callback-url` | Yes | Defaults to `http://localhost:5173/auth/eve/callback`; must match the application |
| `server-name` | No | Name shown to clients |
| `admin-character-id` | No | EVE character ID of the server admin |

`appsettings.Development.json` files are git-ignored throughout the repository, so this is the place for your keys. Create `src/Hunters.AppHost/appsettings.Development.json`:

```json
{
  "Parameters": {
    "eve-client-id": "<client id>",
    "eve-client-secret": "<secret key>",
    "admin-character-id": "<your character id>"
  }
}
```

User secrets work too and take precedence (`dotnet user-secrets --project src/Hunters.AppHost set "Parameters:eve-client-secret" "<secret key>"`). Never put keys in `appsettings.json`, which is committed.

Restart the AppHost after changing parameters. The web dev server always runs on port 5173 so the callback URL stays valid.

### 2b. Self-hosting (Docker Compose)

Copy [`.env.example`](.env.example) to `.env` and fill in:

| Variable | Required | Purpose |
|---|---|---|
| `DB_PASSWORD` | Yes | PostgreSQL password |
| `EVE_CLIENT_ID` | Yes | Client ID of your EVE application |
| `EVE_CLIENT_SECRET` | Yes | Secret key of your EVE application |
| `HUNTERS_PUBLIC_URL` | Yes | Public base URL, e.g. `https://hunters.example.com`; the callback is this plus `/auth/eve/callback` |
| `HUNTERS_NAME` | No | Name shown to clients |
| `HUNTERS_ADMIN_CHARACTER_ID` | No | EVE character ID of the server admin |
| `HUNTERS_ACCESS_KEY_ENABLED` | No | `true` to require an access key for new accounts |
| `HUNTERS_BEHIND_PROXY` | No | `true` behind a reverse proxy that terminates TLS |
| `HUNTERS_WEB_ENABLED`, `HUNTERS_PORT`, `HUNTERS_TAG` | No | Web app hosting, published port, image tag |

Then `docker compose up -d`. The full server configuration reference, including admission rules, is in [Deployment](docs/deployment.md#configuration-reference).

## Self-hosting

See [Deployment](docs/deployment.md).

## License

[GNU General Public License v3.0](LICENSE)
