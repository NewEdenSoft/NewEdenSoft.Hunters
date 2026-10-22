# Deployment

Servers run as containers with Docker Compose. Local development uses the .NET Aspire AppHost (see the [README](../README.md#development)).

## Image

One image, built by the root [`Dockerfile`](../Dockerfile):

1. Node stage builds the React app.
2. .NET SDK stage publishes `Hunters.Server`.
3. Runtime stage (`aspnet:10.0`, non-root user) copies the server and the web build into `wwwroot`.

When `Hunters:Web:Enabled` is `false`, the server does not serve static files or the SPA fallback route; it runs API, SignalR and the EVE callback only.

The SPA fallback never applies to `/api`, `/auth`, `/hubs`, `/openapi` or `/.well-known`, so unknown server routes return 404 instead of the web app.

## Compose

See [`docker-compose.yml`](../docker-compose.yml) and [`.env.example`](../.env.example).

```sh
cp .env.example .env   # set DB_PASSWORD at minimum
docker compose up -d
```

Services:

| Service | Image | Volumes |
|---|---|---|
| `hunters` | `ghcr.io/newedensoft/hunters-server` (or built locally) | `dpkeys` → `/app/keys`, `sde` → `/app/sde` |
| `db` | `postgres:17` | `pgdata` |

The `sde` volume caches CCP's SDE archive (about 100 MB), from which the server builds the map. Losing it only means downloading it again at the next start.

**The `dpkeys` volume must persist.** Refresh tokens are encrypted with the Data Protection keys stored there; losing the keys makes every stored token unreadable and forces all users to log in again.

## TLS and reverse proxies

TLS is terminated by a reverse proxy (Caddy, Traefik, nginx). Set `HUNTERS_BEHIND_PROXY=true` so the server honours `X-Forwarded-*` headers (`ASPNETCORE_FORWARDEDHEADERS_ENABLED`) and builds correct callback and redirect URLs. Only enable this when the server is not directly reachable from the internet.

## Configuration reference

Environment variables use `__` for nesting (`Hunters__Web__Enabled`).

| Key | Default | Purpose |
|---|---|---|
| `Hunters:Name` | `Hunters` | Display name in the discovery document |
| `Hunters:Web:Enabled` | `true` (the AppHost sets `false`; Vite serves the web app) | Serve the web app |
| `Hunters:DataProtection:KeysPath` | `/app/keys` in the image; local app data otherwise | Data Protection key storage |
| `Hunters:Database:MigrateOnStartup` | `true` | Apply database migrations at startup |
| `Hunters:Sso:ClientId` | — | EVE application client ID |
| `Hunters:Sso:ClientSecret` | — | EVE application secret |
| `Hunters:Sso:CallbackUrl` | — | Must match the EVE application's single callback URL: `{public url}/auth/eve/callback`. Its origin is also the public address in desktop login links |
| `Hunters:Sso:Scopes` | location, online, ship, waypoint | Requested scopes |
| `Hunters:Admission:*` | all off | See [Admission](discovery-and-admission.md) |
| `Hunters:Sde:CachePath` | `/app/sde` in the image; local app data otherwise | SDE archive and extracted map data |
| `Hunters:Sde:RefreshInterval` | `06:00:00` | How often to check CCP for a new SDE build |
| `Hunters:Activity:Enabled` | `true` | Collect ESI's hourly kills and jumps per system |
| `Hunters:Activity:Retention` | `30.00:00:00` | How long hourly kills and jumps are kept |
| `Hunters:Admins:{n}` | — | Admin character IDs |
| `Hunters:Esi:Contact` | repository URL | Contact sent to ESI in the User-Agent |
| `ConnectionStrings:hunters` | — | PostgreSQL connection string |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `false` | Trust forwarded headers from a reverse proxy |

## Local development with EVE SSO

1. Register an application at developers.eveonline.com with callback `http://localhost:5173/auth/eve/callback` (the Vite dev server, which Aspire pins to port 5173 and which forwards `/auth` to the server) and the scopes above.
2. Set the AppHost parameters in `src/Hunters.AppHost/appsettings.Development.json` (git-ignored) or as user secrets of the AppHost; `appsettings.json` only holds defaults. The AppHost passes them to the server as `Hunters__Sso__ClientId`, `Hunters__Sso__ClientSecret`, `Hunters__Sso__CallbackUrl`, `Hunters__Name` and `Hunters__Admins__0`. See the [README](../README.md#2a-local-development-aspire).
3. `dotnet run --project src/Hunters.AppHost`.

Running the server on its own (without Aspire) reads the same `Hunters:Sso:*` keys from its own configuration, for example `src/Hunters.Server/appsettings.Development.json` (git-ignored) or its user secrets.

## Self-hosting guide (to write)

1. Register an application at developers.eveonline.com: callback `https://<host>/auth/eve/callback`, scopes from [Authentication](authentication.md#scopes).
2. Fill in `.env` from `.env.example` (`EVE_CLIENT_ID`, `EVE_CLIENT_SECRET`, `HUNTERS_PUBLIC_URL`, `DB_PASSWORD`).
3. `docker compose up -d`.
