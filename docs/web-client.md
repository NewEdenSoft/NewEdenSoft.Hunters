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
| `/map` | `routes/_authed/map.tsx` | The map (full window below the app bar, `staticData.fullBleed`). |
| `/account` | `routes/_authed/account.tsx` | Account and characters. |
| `/settings` | `routes/_authed/settings.tsx` | Theme and map layout. |

When a query gets a 401 while the app is open (session revoked or ended elsewhere), the query cache is cleared and the app returns to `/login`.

## Map

`src/map` holds the map, independent of React except for `MapView` ([D21](decisions.md#d21--the-map-is-2d-drawn-with-canvas-2d-web-and-skia-desktop)):

| File | Role |
|---|---|
| `model.ts` | The map pack as typed arrays per system index: positions per layout, security buckets, gate index pairs, bounds. |
| `security.ts` | Security as EVE shows it (above 0.0 and below 0.05 shows as 0.1) and its colour bucket. |
| `filters.ts` | Colour filters: how each system is coloured, or hidden. Security is the first. |
| `MapLegend.tsx` | The active filter's legend. |
| `gridIndex.ts` | Uniform grid for the nearest system to the cursor. |
| `MapRenderer.ts` | Canvas 2D renderer: view transform, pan (drag), zoom (wheel, pinch), hover; draws only when something changes. |
| `MapView.tsx` | Hosts the renderer; shows the hover panel. |

Sizes follow the zoom: systems grow from 0.9 to 6.5 px radius and gates from 0.4 to 2 px wide, so the zoomed-out map stays calm and the zoomed-in map is easy to read. Zoomed in (from 12 px/ly), a clear margin of up to 5 px opens between each system and its gates: the gate layer is cut out around the systems in one batch before the systems are drawn.

Each frame: gates are stroked under the view transform: solid within a constellation, short dashes (6 px on, 4 px off) between constellations, long dashes (18 px on, 8 px off) between regions, with widths and dashes divided by the zoom so they look the same at every zoom. A gate is coloured with a gradient from its first system's filter colour to the other's, at 55% opacity; gates whose ends share a colour (about three quarters with the security filter) are batched into one `Path2D` per colour and style, and the others each get a gradient. Paths and gradients are built in world coordinates once per layout, filter and theme, and reused every frame; gates out of view are skipped. A gate to a hidden system keeps the neutral gate colour. Then systems in view are filled in one batch per colour group of the active filter; from zoom 28 px/ly, names are drawn to the bottom right of each system, skipping any that would overlap a name already drawn (the hovered system's name always wins). Below that zoom, region names are drawn instead, centred on the mean position of each region's systems, larger regions first and with clear space between names. A region whose systems spread more than twice as wide as the median region gets no label in that layout (Pochven in the top-down layout), since its centre would sit inside other regions. Theme colours are CSS `color-mix()` expressions, which Canvas cannot read, so they are resolved to `rgba()` once and again when the theme changes.

**Colour filters.** A filter turns the map into a colour group per system (or `HIDDEN`), the group colours (CSS, theme variables allowed) and a legend. The renderer only draws groups, so new ways of colouring the map (activity, a route, fleet members) are new filters, not renderer changes. Hidden systems are not drawn, labelled or hoverable; their gates still are. The hover panel always shows the system's real security in its security colour, whatever the filter.

The layout is a user setting, kept in local storage like the theme (`settings/mapSettings.ts`, key `hunters.map.layout`) and chosen on the map or on the Settings page. The colour filter is in the URL (`/map?colour=…`, default left out), so a view can be linked.

The map pack is fetched by the SDE build named in the discovery document and kept in the TanStack Query cache for the session; IndexedDB persistence comes next. The page offers both layouts in the pack: CCP's 2D map, the default, and top-down.

## Account page

The same actions as the [desktop Account page](desktop-client.md#account-page), with portraits from EVE's image server:

- **Add character** and **Log in again** are form posts to `/auth/web/login` with `returnUrl=/account`. With the session cookie, the server links the character to the signed-in account. A failed login comes back as `/login?error=…`; while signed in, `/login` forwards it to `/account?error=…`, which shows the message.
- **Make main**, **Remove** and **Delete account** are TanStack Query mutations that refresh the account query when they finish. Remove and Delete account ask for confirmation. After deleting the account, the cache is cleared and the app goes to `/login`.
- Characters with status `NeedsReauth` or `MissingScopes` get the warning icon and *Log in again*. The web app does not request access tokens yet, so for now only the server's status flags a character.

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
