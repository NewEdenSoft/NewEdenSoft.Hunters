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

## D13 — Hot state behind cache, presence and message-bus interfaces
`ICacheStore`, `IPresenceStore` and `IMessageBus` start with in-memory implementations. Redis or another distributed store can be added later without changing feature code. Durable data (fleet sessions included) lives in Postgres.

## D14 — Refresh tokens encrypted at rest despite the client secret
The client secret protects tokens used outside the server. Encryption covers the common case where a database dump or backup leaks without the server itself being compromised. Cost is low (built-in Data Protection, one persistent volume); losing the keys only forces users to log in again.

## D16 — Sci-fi HUD design with user-selectable faction themes from one shared token file
Users pick a theme based on EVE's race, faction and corporation colours. Each theme is three input colours; the rest is derived with OKLab mixing rules stored as data in `design/themes.json`, which both apps read, so web and desktop always match and adding a theme needs no code. Security, activity and alert colours are fixed across themes. Fonts are bundled, not loaded from Google, which suits self-hosting. See [Design](design.md).

*Rejected:* a generated CSS/XAML build step (more tooling; runtime evaluation also enables custom themes later); a third-party Avalonia theme library (lock-in, harder to match the web).

## D15 — Desktop is Windows first; platform features behind interfaces
Overlay, hotkey, credential and log-location code sits behind per-OS interfaces. Windows first, macOS second, Linux X11 best effort, Wayland limited. See [Desktop Client](desktop-client.md#platform-support).
