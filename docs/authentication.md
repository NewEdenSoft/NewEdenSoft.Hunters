# Authentication

## Principles

1. **The server is a confidential EVE SSO client.** It holds the client ID and client secret. The secret never leaves the server.
2. **A flow ends the way it started.** The server starts the EVE authorization code flow and completes it with `client_id:secret`. PKCE is not used against EVE.
3. **One callback URL.** EVE allows a single callback URL per application. The server's callback serves both web and desktop logins and routes by `state`.
4. **The server is the only party that refreshes tokens.** EVE refresh tokens are consumed on use and a new one is returned with each new access token. Any second party refreshing would invalidate the server's token.
5. **Clients call ESI themselves.** For character data the server only handles registration and token rotation. Desktop and web clients make all character ESI calls using access tokens they obtain from the server.

## Web login

1. Browser → `GET /auth/web/login` (with access key if required, see [Admission](discovery-and-admission.md)).
2. Server creates a login record with a random `state` and redirects to EVE `authorize`.
3. EVE → `GET /auth/eve/callback?code&state`.
4. Server exchanges the code with `client_id:secret`, validates the JWT, runs admission, stores the refresh token, and sets the session cookie.

## Desktop login

The desktop app listens on a loopback address, opens the browser to the server, and receives a single-use login code when the server finishes the EVE flow.

```
Desktop                                        Server                       EVE SSO
  │ 1. start listener http://127.0.0.1:{port}/
  │    appVerifier = random; appChallenge = SHA256(appVerifier)
  │ 2. POST /auth/desktop/begin {port, appChallenge, accessKey?} ─►│ validate key,
  │◄── {loginId} ────────────────────────────────────────────────│ store login (5 min)
  │ 3. open browser: {server}/auth/desktop/{loginId}
  │                                                │ 4. 302 authorize?client_id&state ─►│
  │                                                │◄── 5. callback?code&state ─────────│
  │                                                │ 6. exchange with client_id:secret,
  │                                                │    admission, store refresh token,
  │                                                │    create single-use loginCode
  │◄── 7. 302 http://127.0.0.1:{port}/done?loginId&loginCode ──────│
  │    app serves "You can close this tab"
  │ 8. POST /auth/desktop/redeem {loginId, loginCode, appVerifier} ─►│
  │◄── {appSession, character, eveAccessToken, expiresAt} ──────────│
```

### Rules

| Item | Rule |
|---|---|
| `loginId` | Public (appears in browser URL). Expires after 5 minutes. |
| `port` | Registered in step 2. The server only redirects to `127.0.0.1:{registered port}`, never to a URL supplied in the browser. |
| `appChallenge` / `appVerifier` | Binds the login to the app instance that started it. This is between the app and the server only; it is not EVE PKCE. A local process that intercepts the `loginCode` cannot redeem it. |
| `loginCode` | Single use, expires after 60 seconds, stored hashed. |
| `accessKey` | Sent in the `begin` body, never in a URL. An invalid key fails before the browser opens. |

A phishing link (attacker-started login sent to a victim) delivers the `loginCode` to the victim's own loopback, so the attacker never receives it.

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
