# Deployment

Servers run as containers with Docker Compose. Local development uses the .NET Aspire AppHost.

## Image

One image. A multi-stage Dockerfile builds the React app with Node and copies the output into the server's `wwwroot`. When `Hunters:Web:Enabled` is `false`, the server does not serve static files or the SPA fallback route; it runs API, SignalR and the EVE callback only.

## Compose

```yaml
services:
  hunters:
    image: ghcr.io/newedensoft/hunters-server:latest
    environment:
      Hunters__Sso__ClientId: ${EVE_CLIENT_ID}
      Hunters__Sso__ClientSecret: ${EVE_CLIENT_SECRET}
      Hunters__Sso__CallbackUrl: https://hunters.example.com/auth/eve/callback
      Hunters__Web__Enabled: "true"
      Hunters__Admission__AccessKey__Enabled: "true"
      ConnectionStrings__Hunters: Host=db;Database=hunters;Username=hunters;Password=${DB_PASSWORD}
    volumes:
      - dpkeys:/app/keys      # Data Protection keys — must persist
      - sde:/app/sde          # NewEdenKit.SDE cache
    depends_on: [db]
    ports: ["8080:8080"]

  db:
    image: postgres:17
    environment:
      POSTGRES_DB: hunters
      POSTGRES_USER: hunters
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    volumes: [pgdata:/var/lib/postgresql/data]

volumes: { dpkeys: {}, sde: {}, pgdata: {} }
```

**Data Protection keys must be on a persistent volume.** Refresh tokens are encrypted with them; losing the keys makes every stored token unreadable and forces all users to log in again.

## TLS

Terminated by a reverse proxy (Caddy or Traefik), documented as an optional compose service. The server enables forwarded headers so it builds correct callback and redirect URLs.

## Configuration reference

| Key | Purpose |
|---|---|
| `Hunters:Sso:ClientId` | EVE application client ID |
| `Hunters:Sso:ClientSecret` | EVE application secret (use `.env` or a compose secret) |
| `Hunters:Sso:CallbackUrl` | Must match the EVE application's single callback URL |
| `Hunters:Sso:Scopes` | Requested scopes |
| `Hunters:Web:Enabled` | Serve the web app |
| `Hunters:Admission:*` | See [Admission](discovery-and-admission.md) |
| `Hunters:Admins` | Admin character IDs |
| `ConnectionStrings:Hunters` | PostgreSQL |

## Self-hosting guide (to write)

1. Register an application at developers.eveonline.com: callback `https://<host>/auth/eve/callback`, scopes from the list above.
2. Fill in `.env` from `.env.example`.
3. `docker compose up -d`.
