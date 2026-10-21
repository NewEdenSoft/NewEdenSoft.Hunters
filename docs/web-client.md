# Web Client

React + TypeScript (Vite), served by its own server (see [Deployment](deployment.md)). Not server agnostic.

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
