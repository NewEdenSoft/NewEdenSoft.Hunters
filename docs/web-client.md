# Web Client

React + TypeScript (Vite), served by its own server (see [Deployment](deployment.md)). Not server agnostic.

## Stack and structure

- **TanStack Router** with file-based routes in `src/routes`. The Vite plugin generates `src/routeTree.gen.ts`, which is committed so type checking works on a fresh checkout. Do not edit it.
- **Tailwind CSS v4** (Vite plugin) for styling, with the HUD colours from `design/themes.json` as theme colours. Shared pieces (`Panel`, `Button`, `Label`, `Status`, `PageHeader`) are in `src/components/ui`. See [Design](design.md#components).
- **TanStack Query** for all server state. Query options live next to the API types in `src/api` (`accountQuery`, `discoveryQuery`); routes and components use them through the router context's `queryClient` or the query hooks.
- `src/api/http.ts`: `api()` calls the server on the same origin with the session cookie and throws `ApiError` (status, problem `code`, `title`) on failure.

| Route | File | Access |
|---|---|---|
| `/login` | `routes/login.tsx` | Public. Form post to `/auth/web/login`; shows `?error` from a failed EVE login; skips to `?redirect` when already logged in. |
| layout | `routes/_authed.tsx` | Loads the account before every page below; a 401 redirects to `/login?redirect=…`. App bar with navigation and log out. |
| `/` | `routes/_authed/index.tsx` | Home. |
| `/account` | `routes/_authed/account.tsx` | Account and characters. |
| `/settings` | `routes/_authed/settings.tsx` | Theme picker. |

When a query gets a 401 while the app is open (session revoked or ended elsewhere), the query cache is cleared and the app returns to `/login`.

## Session

Cookie session on the server's origin. Login and adding characters use the web flow in [Authentication](authentication.md#web-login).

The session cookie persists, so users stay logged in across browser restarts until they log out or the session is revoked. Unlike the desktop, which keeps its session in memory only ([D20](decisions.md#d20--desktop-sessions-are-memory-only)).

Every page except the login page requires a session ([D19](decisions.md#d19--both-apps-are-behind-login)). Without one, the app shows only the login page; when the session ends, it returns there.

## ESI calls

The web client makes the same character ESI calls as the desktop client, directly from the browser (ESI supports CORS):

- Location, online status and ship for the user's characters
- Setting in-game waypoints

Access tokens come from `POST /api/characters/{id}/access-token`. They are kept in memory only (never in `localStorage`) and refreshed shortly before expiry. The browser never sees refresh tokens.

Polling only happens while the page is open. Polling slows down when the tab is hidden.

## Features

Map, activity, filters, path finding, fleet sessions and account management, matching the desktop app. Overlays, click-through and the log watcher are desktop only.
