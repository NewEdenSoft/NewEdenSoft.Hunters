# Authentication

## Principles

1. **The server is a confidential EVE SSO client.** It holds the client ID and client secret. The secret never leaves the server.
2. **A flow ends the way it started.** The server starts the EVE authorization code flow and completes it with `client_id:secret`. PKCE is not used against EVE.
3. **One callback URL.** EVE allows a single callback URL per application. The server's callback serves both web and desktop logins; what each login is for travels in the protected OAuth `state`.
4. **Standard OAuth handling for the browser leg.** The redirect to EVE, `state` protection, the correlation cookie, the callback and error handling are ASP.NET Core's OAuth handler (`AddOAuth`), not custom code. See [Implementation](#implementation).
5. **The server is the only party that refreshes tokens.** EVE refresh tokens are consumed on use and a new one is returned with each new access token. Any second party refreshing would invalidate the server's token.
6. **Clients call ESI themselves.** For character data the server only handles registration and token rotation. Desktop and web clients make all character ESI calls using access tokens they obtain from the server.

## Web login

1. Browser → `POST /auth/web/login` (form fields `accessKey` and `returnUrl`, both optional; `GET` also works without a key). If already signed in, the login adds a character to the account.
2. Server issues an OAuth challenge: the login details go into the protected `state`, a correlation cookie is set, and the browser is redirected to EVE `authorize`.
3. EVE → `GET /auth/eve/callback?code&state`.
4. The handler checks `state` and the correlation cookie, then the server exchanges the code with `client_id:secret`, validates the JWT, runs admission, stores the refresh token, and sets the session cookie.

## Desktop login

The desktop app listens on a loopback address, opens the browser to the server, and receives a single-use login code when the server finishes the EVE flow.

```
Desktop                                        Server                       EVE SSO
  │ 1. start listener http://127.0.0.1:{port}/
  │    appVerifier = random; appChallenge = SHA256(appVerifier)
  │ 2. POST /auth/desktop/begin {port, appChallenge, accessKey?} ─►│ validate key,
  │◄── {loginId} ────────────────────────────────────────────────│ store login (5 min)
  │ 3. open browser: {server}/auth/desktop/{loginId}
  │                                                │ 4. OAuth challenge: login details in
  │                                                │    protected state, correlation cookie,
  │                                                │    302 authorize?client_id&state ─────►│
  │                                                │◄── 5. callback?code&state ─────────│
  │                                                │ 6. exchange with client_id:secret,
  │                                                │    admission, store refresh token,
  │                                                │    create single-use loginCode
  │◄── 7. 302 http://127.0.0.1:{port}/callback?loginId&code ───────│
  │    app serves "You can close this tab"
  │ 8. POST /auth/desktop/redeem {loginId, loginCode, appVerifier} ─►│
  │◄── {sessionToken, sessionId, characterId, account} ─────────────│
```

### Rules

| Item | Rule |
|---|---|
| `loginId` | Public (appears in browser URL). Expires after 5 minutes. The browser link works once. |
| Correlation | Opening the browser link sets the OAuth correlation cookie, so only that browser can complete the callback. |
| Browser link | Built from the public origin of `Hunters:Sso:CallbackUrl`, not from the address the app used. The link and the EVE callback are then always on the same origin, even when the app reaches the server directly while browsers go through the Vite dev server or a reverse proxy. |
| `port` | Registered in step 2. The server only redirects to `127.0.0.1:{registered port}`, never to a URL supplied in the browser. |
| `appChallenge` / `appVerifier` | Binds the login to the app instance that started it. This is between the app and the server only; it is not EVE PKCE. A local process that intercepts the `loginCode` cannot redeem it. |
| `loginCode` | Single use, expires after 60 seconds, stored hashed. |
| `accessKey` | Sent in the `begin` body, never in a URL. An invalid key fails before the browser opens. Only needed to create a new account. |
| Errors | After EVE, a failed desktop login redirects to the loopback with `error` and `error_description` instead of `code`. |

### Desktop side

`DesktopLoginClient` in `Hunters.Client` runs the whole flow and works with any server. Its `LoopbackListener` is a plain socket on `127.0.0.1` with a port chosen by the OS, so no URL reservation or admin rights are needed. It answers only `GET /callback`, shows a "you can close this tab" or error page, and ignores other requests and idle connections that browsers open. The app waits up to 10 minutes for the browser, and the user can cancel.

A callback link forwarded from another browser fails the correlation check. Even without it, the `loginCode` would go to the victim's own loopback and could not be redeemed without the app's verifier.

## Implementation

| Piece | Role |
|---|---|
| `AddOAuth<OAuthOptions, EveOAuthHandler>("Eve")` | The standard handler: challenge, `state` protection (Data Protection), correlation cookie (`SameSite=Lax`), callback at `/auth/eve/callback`. |
| `EveOAuthHandler` | Two overrides: the `redirect_uri` is always the configured `Hunters:Sso:CallbackUrl` (not derived from the request host, which proxies change); the code exchange goes through `IEveSsoClient` (HTTP Basic client authentication, JWT signature validation). PKCE is off. |
| `LoginRequest` | What the login is for (web or desktop, account to add to, access key, loopback port, app challenge, return URL), stored in `AuthenticationProperties.Items` and therefore inside the protected `state`. |
| `LoginEvents` | `OnTicketReceived` applies the login (sign in, link, create, admission) and sends the browser to the loopback or the web app. `OnAccessDenied` and `OnRemoteFailure` send it to the matching error. |
| `IEveSsoClient` | Code exchange, refresh, revoke and token validation. Refresh and revoke are not part of the OAuth handler. |

### Adding characters

Same flow, with the existing app session attached to `begin`. The new character is linked to the same account.

## App sessions

- The desktop receives an app session (long-lived, revocable per device) on redeem. It is stored per server in the OS credential store (Windows Credential Manager / DPAPI).
- The web app uses a cookie session.
- Admins can revoke sessions; admission rechecks also revoke them. See [Admission](discovery-and-admission.md).

## Access tokens for clients

`POST /api/characters/{characterId}/access-token` (requires app session or web cookie)

- Returns the cached access token if it has more than ~60 seconds left, otherwise refreshes.
- Desktop: `Hunters.Client` provides an `ICharacterTokenProvider` that caches tokens until shortly before expiry and supplies them to NewEdenKit.ESI calls (via middleware).
- Web: tokens are kept in memory only. See [Web Client](web-client.md).

## Refresh handling

Because refresh tokens are consumed on use:

- **Single flight per character.** A keyed lock in process; a Postgres row lock (`SELECT … FOR UPDATE`) or row version check if multiple server instances run.
- **Persist before returning.** The new refresh token is committed before the access token is returned to anyone.
- **Lost token handling.** If EVE consumed the old token but the new one could not be stored, the character is marked *needs re-auth* and the user is prompted in the UI.
- **Encryption at rest.** Refresh tokens are encrypted with ASP.NET Data Protection. Keys are stored on a persistent volume (see [Deployment](deployment.md)). The client secret already makes a stolen token useless on its own, but a database dump or backup is often stored alongside the `.env` file that holds the secret. Encryption keeps a database-only leak harmless. Losing the keys only forces users to log in again. See [D14](decisions.md#d14--refresh-tokens-encrypted-at-rest-despite-the-client-secret).
- **Ownership changes.** If a login or refresh returns a different `owner` hash, the character was transferred. See [Accounts](accounts.md#character-ownership-changes).
- **Rejected logins.** If admission fails after the exchange, the server revokes the token at EVE and stores nothing.

## Scopes

Configured on the server, matching its EVE application registration. Initial set:

- `esi-location.read_location.v1`
- `esi-location.read_online.v1`
- `esi-location.read_ship_type.v1`
- `esi-ui.write_waypoint.v1`
