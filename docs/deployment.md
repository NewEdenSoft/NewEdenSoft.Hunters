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
| `hunters` | `ghcr.io/newedensoft/hunters-server` (or built locally) | `dpkeys` → `/app/keys` |
| `db` | `postgres:17` | `pgdata` |

**The `dpkeys` volume must persist.** Refresh tokens are encrypted with the Data Protection keys stored there; losing the keys makes every stored token unreadable and forces all users to log in again.

## TLS and reverse proxies

TLS is terminated by a reverse proxy (Caddy, Traefik, nginx). Set `HUNTERS_BEHIND_PROXY=true` so the server honours `X-Forwarded-*` headers (`ASPNETCORE_FORWARDEDHEADERS_ENABLED`) and builds correct callback and redirect URLs. Only enable this when the server is not directly reachable from the internet.

## Configuration reference

Environment variables use `__` for nesting (`Hunters__Web__Enabled`).

| Key | Default | Purpose |
|---|---|---|
| `Hunters:Name` | `Hunters` | Display name in the discovery document |
| `Hunters:Web:Enabled` | `true` (`false` in Development) | Serve the web app |
| `Hunters:DataProtection:KeysPath` | `/app/keys` in the image; local app data otherwise | Data Protection key storage |
| `Hunters:Admission:AccessKey:Enabled` | `false` | Require an access key to log in |
| `ConnectionStrings:hunters` | — | PostgreSQL connection string |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `false` | Trust forwarded headers from a reverse proxy |

Added in Phase 1:

| Key | Purpose |
|---|---|
| `Hunters:Sso:ClientId` | EVE application client ID |
| `Hunters:Sso:ClientSecret` | EVE application secret |
| `Hunters:Sso:CallbackUrl` | Must match the EVE application's single callback URL |
| `Hunters:Sso:Scopes` | Requested scopes |
| `Hunters:Admission:*` | See [Admission](discovery-and-admission.md) |
| `Hunters:Admins` | Admin character IDs |

## Self-hosting guide (to write)

1. Register an application at developers.eveonline.com: callback `https://<host>/auth/eve/callback`, scopes from [Authentication](authentication.md#scopes).
2. Fill in `.env` from `.env.example`.
3. `docker compose up -d`.
