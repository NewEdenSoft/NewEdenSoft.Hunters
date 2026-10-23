# Decision Log

## D1 — Server processes the SDE; clients get a map pack
Clients only need map data. Shipping a small versioned map pack avoids every client downloading and processing the full SDE.

## D2 — Path finding and filters run on the client
Hover previews must be instant. The graph is small. Cost: the logic exists in C# and TypeScript, kept consistent with shared golden tests.

## D3 — Desktop client is server agnostic
Groups can self-host. The client learns everything from `/.well-known/hunters`. Consequence: `Hunters.Contracts` is a versioned public API.

## D4 — Server is a confidential EVE client; no PKCE against EVE
A flow must end the way it started: a code obtained with PKCE cannot be exchanged with the client secret. Having the server start and finish the flow with the secret means a leaked refresh token is useless without the secret, and web and desktop share one exchange path.

*Rejected:* desktop-started PKCE with the server completing the exchange (no secret protection on refresh tokens; more client complexity).

## D5 — Desktop login via loopback redirect and single-use code
EVE allows one callback URL, which the server owns. After completing the EVE flow, the server redirects to the desktop's registered loopback port with a single-use `loginCode`, redeemed with an app-side verifier.

*Rejected:* polling for the result (needs a confirmation code to resist phishing); custom URI scheme (OS registration, single-instance forwarding).

## D6 — Server is the only refresher
EVE refresh tokens are consumed on use and replaced. Two parties refreshing would invalidate each other, so only the server refreshes.

## D7 — Optional admission policy on the server
Server owners can control access with access keys, corporation/alliance affiliation and character lists, rechecked periodically. All rules are off by default.

## D8 — Initial map style is a 2D projection of SDE coordinates
Available directly from the SDE. Schematic layouts may follow.

## D9 — Server hosts its own web app, optionally
The web app is not server agnostic; it is served by and talks to its own server. Hosting can be turned off.

## D10 — .NET 10, Docker Compose deployment

## D11 — Clients make all character ESI calls
For character data, the server only handles registration and token rotation. Desktop and web make location, ship, online and waypoint calls themselves with server-issued access tokens. This keeps server load low and spreads ESI error-limit usage across users' own IPs. Consequence: nothing tracks a character while no client is open.

*Rejected:* server-side location polling (load grows with every registered character).

## D12 — Live kills are ingested once and forwarded only where relevant
One zKill consumer per deployment, slim summaries, a bounded channel, per-system subscriptions sent by clients, and batched, capped pushes. Clients fetch full killmails from ESI themselves. See [Map & Activity](map-and-activity.md#live-kill-feed).

*Extended:* the intel service (D22) is that one consumer. It reads zKillboard's R2Z2 feed around the clock and **stores** every kill as a slim row for 90 days, as the input for hunting profiles. See [Kills](map-and-activity.md#kills).

## D13 — Hot state behind cache, presence and message-bus interfaces
`ICacheStore`, `IPresenceStore` and `IMessageBus` start with in-memory implementations. Redis or another distributed store can be added later without changing feature code. Durable data (fleet sessions included) lives in Postgres.

## D14 — Refresh tokens encrypted at rest despite the client secret
The client secret protects tokens used outside the server. Encryption covers the common case where a database dump or backup leaks without the server itself being compromised. Cost is low (built-in Data Protection, one persistent volume); losing the keys only forces users to log in again.

## D16 — Sci-fi HUD design with user-selectable faction themes from one shared token file
Users pick a theme based on EVE's race, faction and corporation colours. Each theme is three input colours; the rest is derived with OKLab mixing rules stored as data in `design/themes.json`, which both apps read, so web and desktop always match and adding a theme needs no code. Security, activity and alert colours are fixed across themes. Fonts are bundled, not loaded from Google, which suits self-hosting. See [Design](design.md).

*Rejected:* a generated CSS/XAML build step (more tooling; runtime evaluation also enables custom themes later); a third-party Avalonia theme library (lock-in, harder to match the web).

## D17 — Desktop features are widgets that dock or float
Each desktop feature is a widget that is either a page in the main window or an overlay over the game. The app lives in the system tray and the main window is mostly closed. A floating widget's page shows its overlay settings, and locking and click-through are per widget.

*Rejected:* a global unlock hotkey for all overlays (per-widget settings on the widget's own page are clearer); showing a widget both docked and floating at once (two live views of the same state).

## D18 — EVE login uses the standard ASP.NET Core OAuth handler
The browser leg (challenge, `state` protection, correlation cookie, callback, error handling) is `AddOAuth`, with login details carried in the protected `state` and two small overrides for EVE (fixed redirect URI, code exchange with Basic authentication and JWT validation). Account logic runs in the handler's events. This replaced a hand-written flow that kept `state` server-side but did not bind it to the browser, leaving the web login open to login CSRF.

*Rejected:* the hand-written flow plus a custom correlation cookie (more security-sensitive code to own); the third-party `AspNet.Security.OAuth.EVEOnline` provider (extra dependency; token validation still needed for refresh).

## D19 — Both apps are behind login
Every feature of the desktop and web apps requires a signed-in session. Signed out, each app shows only its login screen. The desktop also restores no overlays and offers only *Open* and *Quit* in the tray. When a session ends, the app returns to the login screen. All features depend on the user's characters and the server (locations, activity near them, fleets), so there is nothing useful to show without a session, and one gate is simpler than per-feature checks.

*Rejected:* an anonymous map or activity view (needs public server endpoints and load the server owner did not opt into).

## D20 — Desktop sessions are memory only
The desktop app keeps its session token in memory and logs in again at every start. Only the web app keeps a session across restarts (its cookie). No long-lived bearer token sits on the user's disk, in backups or in the OS credential store, and there is no per-OS credential code to maintain. Logging in is quick because the EVE login page usually remembers the browser. The app revokes its session when it quits.

*Rejected:* storing the session in the OS credential store (Credential Manager, Keychain, Secret Service): convenient, but a stolen token gives full access to the account until revoked, and it needs a credential implementation per OS.

## D21 — The map is 2D, drawn with Canvas 2D (web) and Skia (desktop)
Both clients draw the map in 2D with an immediate-mode renderer: Canvas 2D in the browser, SkiaSharp on the desktop. Gates are one cached path stroked under the view transform, systems are batched by colour, and labels are placed with level of detail and collision avoidance. The two renderers follow the same model, so layout, hit-testing and label rules can be shared and tested in both languages.

*Rejected:* three.js / React Three Fiber with a 2D/3D camera switch (tried in an earlier version of this project; text labels were painful); SVG or one DOM element per system (too many elements for smooth pan and zoom); graph libraries such as Sigma.js (hard to style to the HUD and to layer our own overlays).

## D22 — A separate intel service with a derived store
Public activity data (ESI's hourly kills and jumps, and later the zKillboard feed) is collected by **`Hunters.Intel`**, a worker process next to the API server: its own Docker Compose service, sharing the database but writing only its own `intel` schema, whose migrations it owns. The intel service keeps the raw inputs and builds **derived tables when new data arrives** (current activity, hour-of-week curves, hunting profiles). The API server only reads them, so requests are lookups, never aggregations. The collectors run around the clock whatever the API server is doing, and can be scaled, restarted or turned off on their own.

*Rejected:* workers inside the API server (one process doing two jobs; restarts and load mixed); computing profiles per request (repeated work, slow for hour-of-week curves over 90 days); one row per system per hour for the raw data (millions of rows; arrays per hour instead).

## D23 — Range queries run on the server, with the user's own limits
"What's in reach from here" is `GET /api/intel/reach`: the server searches its in-memory map (the gate graph and 3D positions from the SDE, built with the map pack) and joins the activity and hunting intel, so clients get one ranked answer per origin. The user's limits are the query: **gate jumps** (every system within N, whatever its distance) and/or **light years** (every cyno target within X, however many gates away), as a union. Every row carries both distances and whether a jump drive can land there, so the client decides what "in jump range" means for its ship. The server knows nothing about hulls, skills or fuel; it only applies distance and where cynos can be lit (not high-sec, wormhole space, Pochven or Zarzakh). A search of the whole map takes about 0.2 ms. See [Map & Activity](map-and-activity.md#reach).

This narrows D2: routes, hover previews and filters stay on the client; ranking systems by intel within a range is the server's.

*Rejected:* the client joining a whole-map intel download with its own range search (every client repeats the work and needs the 3D positions and the cyno rules); the server applying ship ranges (the user knows their own hull and skills); gate jumps only (after a filament a Black Ops reaches systems 15+ gates away).

## D15 — Desktop is Windows first; platform features behind interfaces
Overlay, hotkey and log-location code sits behind per-OS interfaces. Windows first, macOS second, Linux X11 best effort, Wayland limited. See [Desktop Client](desktop-client.md#platform-support).
