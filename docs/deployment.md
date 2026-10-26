# Deployment

Servers run as containers with Docker Compose. Local development uses the .NET Aspire AppHost (see the [README](../README.md#development)).

## Images

Two images, built by the root [`Dockerfile`](../Dockerfile) and published to GitHub Container Registry by the [release workflow](#releasing):

| Image | Dockerfile target | Contents |
|---|---|---|
| `ghcr.io/newedensoft/hunters-server` | `final` (the default) | `aspnet:10.0`, non-root: the server, with the web build in `wwwroot` |
| `ghcr.io/newedensoft/hunters-intel` | `intel` | `aspnet:10.0` (for the shared service defaults), non-root: the intel service |

Stages: a Node stage builds the React app, a .NET SDK stage publishes `Hunters.Server` and `Hunters.Intel` (stamped with the `VERSION` build argument, `0.0.0-local` by default), and the two runtime stages copy their output.

Tags: every release `X.Y.Z` is published as `X.Y.Z`, `X.Y` and `latest`; a pre-release (`X.Y.Z-rc.1`) only under its own tag. Images are built for `linux/amd64`.

When `Hunters:Web:Enabled` is `false`, the server does not serve static files or the SPA fallback route; it runs API, SignalR and the EVE callback only.

The SPA fallback never applies to `/api`, `/auth`, `/hubs`, `/openapi` or `/.well-known`, so unknown server routes return 404 instead of the web app.

## Compose

[`docker-compose.yml`](../docker-compose.yml) is the production setup; its settings come from a `.env` file next to it ([`.env.example`](../.env.example)).

```mermaid
flowchart LR
    internet((Internet)) -- ":80 / :443" --> traefik
    subgraph proxy [network hunters-proxy]
        traefik --> hunters
    end
    subgraph default [network default]
        hunters --> db[(db)]
        intel --> db
    end
    intel -- "ESI, zKillboard" --> internet
```

| Service | Image | Ports | Volumes |
|---|---|---|---|
| `traefik` | `traefik:v3` | 80, 443 | `letsencrypt` → `/letsencrypt`; the Docker socket, read-only |
| `hunters` | `ghcr.io/newedensoft/hunters-server` | none (through Traefik) | `dpkeys` → `/app/keys`, `sde` → `/app/sde` |
| `intel` | `ghcr.io/newedensoft/hunters-intel` | none | `intel-sde` → `/app/sde` |
| `db` | `postgres:17` | none | `pgdata` |

`HUNTERS_TAG` picks the image version for both services (`latest` by default; pin a release such as `0.1.0` to upgrade on your own schedule). To build the images from a checkout instead of pulling them, add the build override: `docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build`.

The intel service ([D22](decisions.md#d22--a-separate-intel-service-with-a-derived-store)) collects public activity data (ESI's hourly kills and jumps, zKillboard's kill feed) into the database's `intel` schema and applies that schema's migrations at startup; the server only reads it. It needs the same connection string and nothing else; `https://${HUNTERS_DOMAIN}` is sent to ESI and zKillboard as the operator contact. After changing the activity rules, recompute the derived tables from the raw hours with `docker compose run --rm intel rebuild-activity` (a few minutes for 90 days; safe while the service runs).

**Memory:** the intel service's live managed heap is about 40 MB (the SDE catalogue is most of it), and it runs within a 64 MB GC heap: about 85 MB private and 180 MB working set, about half of which is the runtime's own mapped files. Without a limit, .NET's GC collects lazily on a machine with free memory, so the same process can sit at 300+ MB without needing it. Under a container memory limit (`mem_limit`, or `deploy.resources.limits.memory`), .NET caps its heap at 75% of the limit and collects accordingly; 256 MB leaves ample room, and the Compose file sets it.

The `sde` volume caches CCP's SDE archive (about 100 MB), from which the server builds the map. Losing it only means downloading it again at the next start.

**The `dpkeys` volume must persist.** Refresh tokens are encrypted with the Data Protection keys stored there; losing the keys makes every stored token unreadable and forces all users to log in again.

## TLS and Traefik

Traefik terminates TLS and is the only service with published ports. It gets a Let's Encrypt certificate for `HUNTERS_DOMAIN` through the HTTP challenge on port 80 (so the domain's DNS record must point at the server, and ports 80 and 443 must be open), keeps it in the `letsencrypt` volume, and redirects HTTP to HTTPS. WebSockets (SignalR) pass through without extra configuration.

Traefik only routes containers labelled `traefik.enable=true` (the server). It reads those labels through the Docker socket, mounted read-only. The server sits on the `hunters-proxy` network with Traefik and on the default network with the database; the database and the intel service are never reachable from Traefik.

The server trusts Traefik's `X-Forwarded-*` headers (`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`) to build correct callback and redirect URLs. That is safe only because the server publishes no ports of its own: don't add a `ports:` entry to the `hunters` service.

**An existing Traefik:** drop the `traefik` service, put `hunters` on that Traefik's network (replace `hunters-proxy` with its name, marked `external: true`) and change the `certresolver` label to its resolver's name. Another reverse proxy (Caddy, nginx) works the same way: route `https://<domain>` to `hunters:8080` and forward the `X-Forwarded-*` headers.

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
| `Hunters:Activity:Enabled` | `true` | Serve the intel service's activity tables |
| `Hunters:Activity:PollInterval` | `00:00:30` | How often to check for a new activity or hunting version (`Hunters:Activity:Enabled` covers both) |
| `Hunters:Admins:{n}` | — | Admin character IDs |
| `Hunters:Esi:Contact` | repository URL | Contact sent to ESI in the User-Agent |
| `ConnectionStrings:hunters` | — | PostgreSQL connection string |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `false` | Trust forwarded headers from a reverse proxy |

Intel service (`Hunters.Intel`, same `Hunters` section and `ConnectionStrings__hunters`):

| Key | Default | Purpose |
|---|---|---|
| `Hunters:Database:MigrateOnStartup` | `true` | Apply the `intel` schema's migrations at startup |
| `Hunters:Activity:Enabled` | `true` | Collect ESI's hourly kills and jumps per system |
| `Hunters:Activity:Retention` | `90.00:00:00` | How long raw hourly snapshots are kept (13 samples per hour of the week); older hours leave the hour-of-week sums |
| `Hunters:Activity:PeakThreshold` | `0.8` | "In its peak" when the hour now averages at least this share of the busiest hour of the week |
| `Hunters:Activity:PeakMinWeeks` | `3` | Peak fields only once every hour of the week has this many samples |
| `Hunters:Activity:PeakMinAverage` | `1` | Peak fields only when the busiest hour averages at least this much (quiet systems get none) |
| `Hunters:Kills:Enabled` | `true` | Read zKillboard's R2Z2 feed and store kills |
| `Hunters:Kills:Retention` | `90.00:00:00` | How long kills are kept |
| `Hunters:Kills:ResumeWithin` | `20:00:00` | A saved feed position older than this starts again at the newest file (zKillboard keeps files about a day) |
| `Hunters:Hunting:FleetAttackers` | `25` | A kill with more attackers is a fleet fight: its sightings aren't counted |
| `Hunters:Hunting:BattleKills` | `20` | This many player kills in a system within the battle window of a sighting is a battle |
| `Hunters:Hunting:BattleWindow` | `00:15:00` | The battle window, either side of a sighting |
| `Hunters:Hunting:ScoreHalfLife` | `14.00:00:00` | A sighting's weight in the hunting score halves every this long |
| `Hunters:Hunting:UpdateInterval` | `00:05:00` | The hunting aggregates are recomputed at most this often while sightings arrive (and at least hourly) |
| `Hunters:Esi:Contact` | the project URL (Compose: `https://${HUNTERS_DOMAIN}`) | Operator contact in the ESI, SDE and zKillboard `User-Agent` |
| `Hunters:Sde:CachePath` | `/app/sde` in the image; local app data otherwise | Its own SDE cache (map data and item types). Not shared with the server's volume, which could be mid-download at the same moment |
| `Hunters:Sde:RefreshInterval` | `06:00:00` | How often to check CCP for a new SDE build |

## Local development with EVE SSO

1. Register an application at developers.eveonline.com with callback `http://localhost:5173/auth/eve/callback` (the Vite dev server, which Aspire pins to port 5173 and which forwards `/auth` to the server) and the scopes above.
2. Set the AppHost parameters in `src/Hunters.AppHost/appsettings.Development.json` (git-ignored) or as user secrets of the AppHost; `appsettings.json` only holds defaults. The AppHost passes them to the server as `Hunters__Sso__ClientId`, `Hunters__Sso__ClientSecret`, `Hunters__Sso__CallbackUrl`, `Hunters__Name` and `Hunters__Admins__0`. See the [README](../README.md#2a-local-development-aspire).
3. `dotnet run --project src/Hunters.AppHost`.

Running the server on its own (without Aspire) reads the same `Hunters:Sso:*` keys from its own configuration, for example `src/Hunters.Server/appsettings.Development.json` (git-ignored) or its user secrets.

## Deploying a server

On a Linux host with Docker Engine and the Compose plugin:

1. Point a DNS record (`hunters.example.com`) at the host, and open ports 80 and 443.
2. Register an application at developers.eveonline.com: callback `https://<domain>/auth/eve/callback`, scopes from [Authentication](authentication.md#scopes).
3. Copy [`docker-compose.yml`](../docker-compose.yml) and [`.env.example`](../.env.example) into a directory on the host, rename the example to `.env` and fill in `DB_PASSWORD`, `HUNTERS_DOMAIN`, `ACME_EMAIL`, `EVE_CLIENT_ID` and `EVE_CLIENT_SECRET`, and optionally `HUNTERS_ADMIN_CHARACTER_ID`. No source checkout is needed.
4. `docker compose up -d`. The first start downloads the SDE (about 100 MB) before the map is served; `docker compose logs -f hunters intel` shows progress.
5. Open `https://<domain>` and log in.

**Upgrading:** set `HUNTERS_TAG` to the new version (or keep `latest`), then `docker compose pull && docker compose up -d`. Both services apply their database migrations at startup. Read the release notes first.

**Backups:** the `pgdata` volume (`docker compose exec db pg_dump -U hunters hunters > hunters.sql`) and the `dpkeys` volume. Losing `dpkeys` logs everyone out; the other volumes are caches that are rebuilt.

## Releasing

Releases are made by hand from `main`: *Actions → Release → Run workflow*, branch `main`, and a version such as `0.1.0` (or `0.1.0-rc.1` for a pre-release). From the command line: `gh workflow run release.yml --ref main -f version=0.1.0`. The [release workflow](../.github/workflows/release.yml):

1. Refuses to run from any other branch, or when the tag `vX.Y.Z` already exists.
2. Runs the CI checks (the same jobs as [`ci.yml`](../.github/workflows/ci.yml)).
3. Builds both images with `VERSION=X.Y.Z` and pushes them to GitHub Container Registry (`X.Y.Z`, `X.Y` and `latest`; only `X.Y.Z` for a pre-release).
4. Tags the commit `vX.Y.Z` and creates a GitHub Release with generated notes, marked as a pre-release when the version has a suffix. Edit the notes afterwards if needed.

Don't push version tags or draft releases on GitHub yourself: neither starts a build.

New packages on GitHub Container Registry are private. After the first release, make `hunters-server` and `hunters-intel` public under the organisation's *Packages* settings, so servers can pull them without logging in.
