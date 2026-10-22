# Web Client

React + TypeScript (Vite), served by its own server (see [Deployment](deployment.md)). Not server agnostic.

## Stack and structure

- **TanStack Router** with file-based routes in `src/routes`. The Vite plugin generates `src/routeTree.gen.ts`, which is committed so type checking works on a fresh checkout. Do not edit it.
- **Tailwind CSS v4** (Vite plugin) for styling, with the HUD colours from `design/themes.json` as theme colours. Shared pieces (`Panel`, `Button`, `Label`, `Status`, `PageHeader`) are in `src/components/ui`. See [Design](design.md#components).
- **TanStack Query** for all server state. Query options live next to the API types in `src/api` (`accountQuery`, `discoveryQuery`); routes and components use them through the router context's `queryClient` or the query hooks.
- `src/api/http.ts`: `api()` calls the server on the same origin with the session cookie and throws `ApiError` (status, problem `code`, `title`) on failure.
- **Vitest** for unit tests (`npm test`, also run in CI), next to the code as `*.test.ts`.

| Route | File | Access |
|---|---|---|
| `/login` | `routes/login.tsx` | Public. Form post to `/auth/web/login`; shows `?error` from a failed EVE login; skips to `?redirect` when already logged in. |
| layout | `routes/_authed.tsx` | Loads the account before every page below; a 401 redirects to `/login?redirect=…`. The [rail](#layout-rail-and-panels) on the left of every page. |
| `/` | `routes/_authed/index.tsx` | The map, filling the window beside the rail (`staticData.fullBleed`), with panels over it. |
| `/map` | `routes/_authed/map.tsx` | Redirects to `/`, keeping the colour filter, for old links. |
| `/account` | `routes/_authed/account.tsx` | Account and characters. |
| `/settings` | `routes/_authed/settings.tsx` | Theme and map layout. |

When a query gets a 401 while the app is open (session revoked or ended elsewhere), the query cache is cleared and the app returns to `/login`.

## Layout: rail and panels

The map is the app. Every signed-in page has a slim rail on the left: the map at the top and, while on the map, a toggle per panel and *Reset panels*; Account (the main character's portrait), Settings and *Log out* at the bottom. Account and Settings are ordinary pages that scroll in the centred column.

Panels float over the map, the web version of the desktop's widgets (`src/panels`):

| Piece | Role |
|---|---|
| `definitions.ts` | Each panel: id, title, rail icon, starting place and width (a negative x counts from the right edge), `resizable` (default on), minimum size, open by default. |
| `panelStore.ts` | Position, size, open, collapsed and stacking order per panel, kept in local storage (`hunters.panels`) like the theme and map layout, and synced across tabs. Moves while dragging are saved once at the end. |
| `geometry.ts` | Pure layout rules, unit tested: keep in view (`clampRect`), snap a moved panel (`snapMove`) or a resized one (`snapResize`). |
| `PanelLayer.tsx` | The layer over the map. It measures itself, so panels are kept in view when the window changes size, and passes clicks between panels through to the map. |
| `FloatingPanel.tsx` | One panel: drag by the title bar, resize from the bottom-right corner, collapse to the title bar, close. Clicking brings it to the front. With the title bar focused, the arrow keys move it (Shift for one pixel) and Escape collapses it. |

Panels snap to the map's edges (12 px in) and to the panels beside them (lined up, or 8 px apart) when dropped within 12 px. A panel fits its content until the user resizes it. When the window shrinks, panels are drawn inside it without changing what is saved, so they go back to their places when it grows again. Panels so far: **Map** (layout, colour, legend) and **Characters** (below). Nearby activity and Fleet follow; on phone-sized screens, panels will become a bottom sheet.

**Characters panel.** The same as the desktop's Characters widget: each character's marker, portrait, an online dot, name, system (with *Docked*) and ship, live from the tracker. Untracked characters say so, characters that need a new login are flagged in amber, and the tooltip has the full station name and the last tracking error. Main first, then by name. Clicking a row shows the character on the map; the target button at the end of the row follows it.

**Markers, focus and follow on the map.** As on the [desktop](desktop-client.md#map): each tracked character with a known location is drawn with its marker, a leader line from its system up and to the right ending at a row of badges (main first), offline characters dimmed, rows stepped up when they would overlap, at every zoom. Clicking a character in the panel, or its marker on the map, focuses the map on it with a 350 ms animation (zooming in to at least 60 px/ly). A chip at the top of the map shows the focused character with **Follow** and dismiss; while following, the map pans along each time the character changes system and keeps the zoom. Dragging the map stops following; a press that moves less than 4 px is a click. The hover panel lists the account's characters in the system. `MapRenderer` draws the markers as frame data (`setMarkers`), with one layout for drawing and clicking (`markerAt`), and `focusOn` animates the view; `map/mapFocus.ts` holds the focused and followed character, and `useCharacterMarkers` / `useMapFocusTarget` connect them to the tracker.

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

The layout is a user setting, kept in local storage like the theme (`settings/mapSettings.ts`, key `hunters.map.layout`) and chosen in the Map panel or on the Settings page. The colour filter is in the URL (`/?colour=…`, default left out), so a view can be linked.

The map pack is fetched by the SDE build named in the discovery document and kept in the TanStack Query cache for the session; IndexedDB persistence comes next. The page offers both layouts in the pack: CCP's 2D map, the default, and top-down.

## Account page

The same actions as the [desktop Account page](desktop-client.md#account-page), with portraits from EVE's image server:

- **Add character** and **Log in again** are form posts to `/auth/web/login` with `returnUrl=/account`. With the session cookie, the server links the character to the signed-in account. A failed login comes back as `/login?error=…`; while signed in, `/login` forwards it to `/account?error=…`, which shows the message.
- **Make main**, **Remove** and **Delete account** are TanStack Query mutations that refresh the account query when they finish. Remove and Delete account ask for confirmation. After deleting the account, the cache is cleared and the app goes to `/login`.
- Characters with status `NeedsReauth` or `MissingScopes` get the warning icon and *Log in again*.
- Each row also has the character's map marker (click it to choose another), a **Track** switch and a location line such as *Online · Jita · Docked at Jita IV - Moon 4 - Caldari Navy Assembly Plant · Ishtar “Hunter One”*.

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

### Character tracking

`src/tracking` follows the same rules as the desktop's [character tracking](desktop-client.md#character-tracking), so both clients behave alike:

| File | Role |
|---|---|
| `tracker.ts` | `CharacterTracker`: `track(ids)` sets which characters are polled; `get`, `characters` and `subscribe` give the latest state and the changes (only real ones, not timestamps). Online characters every 5 s, offline ones only checked for coming online every 60 s (their last place read once), failures back off 5, 10, 20, 40, 80 s then 2 min, `needsLogin` stops a character until it is tracked again. `setBackground(true)` follows online characters every 30 s in a hidden tab; becoming visible polls straight away. |
| `esi.ts` | The three ESI calls and `POST /universe/names`, with `fetch`. Sends `X-Compatibility-Date: 2026-08-18`, the same as the desktop's NewEdenKit.ESI. 401, 403 and other failures are results, not exceptions. |
| `tokens.ts` | `ServerTokenProvider`: tokens from the server, cached in memory until a minute before expiry, one request at a time per character. The server's `needs_reauth` becomes `NeedsLoginError`. |
| `names.ts` | `EveNames`: names of systems, stations and ship types, cached for the page's lifetime. Player structures are never looked up (they are not public). |
| `types.ts` | `TrackedCharacter` and friends, mirroring the desktop's types. |

Everything is tested with Vitest (fake timers for the cadences, a stubbed `fetch` for the clients).

`TrackingProvider` (in the signed-in layout) runs one tracker for the session: it tracks the account's characters that are usable and switched on, follows changes to the account and the switches (only the characters that changed start or stop), switches to the background pace while the tab is hidden, and stops when the user logs out. Components read it with `useTrackedCharacter` and `useWhereabouts` (`trackingContext.ts`), and `useCharacterStatus` turns that into the text the panel and the Account page show.

**Markers and tracking switches** (`src/characters`) are kept per browser in local storage (`hunters.characters`), like the desktop keeps them per computer: the same letters, numbers and icons as the desktop (`markers.ts`, the same SVG paths), defaulting to the first letter of the name, and a Track switch that is on by default. The Account page shows each character's marker (click it for the picker), its Track switch and its location line. `whereabouts.ts` mirrors the desktop's `CharacterWhereabouts`.

## Features

Map, activity, filters, path finding, fleet sessions and account management, matching the desktop app. Overlays, click-through and the log watcher are desktop only.
