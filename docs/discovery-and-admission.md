# Discovery & Admission

## Server-agnostic desktop client

The desktop client can connect to any Hunters server, so groups can run their own. Each server has its own EVE application registration. The client knows nothing about a server until it reads the discovery document.

### Discovery document

`GET {server}/.well-known/hunters` (anonymous)

```json
{
  "name": "Example Hunters",
  "apiVersion": "1.0",
  "minClientVersion": "0.1.0",
  "admission": { "requiresAccessKey": true },
  "mapPack": { "sdeBuild": 3012345 },
  "features": ["fleet", "zkill-live"]
}
```

- No SSO details are exposed; the server runs the EVE flow itself.
- The client refuses to connect if its version is below `minClientVersion` or it does not support `apiVersion`. `Hunters.Contracts` is therefore a versioned public API.
- Admission details (allowed corporations etc.) are never exposed.

### Trust

Connecting to a server hands it the user's refresh tokens. When adding a new server, the desktop shows the server name, URL and requested scopes and asks the user to confirm.

## Admission policy

Server owners **can** restrict who may log in. Admission is optional: with no rules enabled (the default), anyone with an EVE character can log in. Applies to both web and desktop logins.

```json
"Admission": {
  "Mode": "All",
  "AccessKey":   { "Enabled": true },
  "Affiliation": { "Enabled": true, "CorporationIds": [98000001], "AllianceIds": [], "Scope": "FirstCharacter" },
  "Characters":  { "Allow": [], "Deny": [] },
  "RecheckInterval": "01:00:00"
}
```

| Setting | Meaning |
|---|---|
| `Mode` | `All`: every enabled rule must pass. `Any`: at least one must pass. |
| `AccessKey` | Login requires a key. Keys are stored hashed. Either one shared server key or per-user invite keys with optional expiry, use limit and revocation. |
| `Affiliation` | Character must belong to one of the listed corporations or alliances. `Scope` is `FirstCharacter` (the account's main) or `AllCharacters`. |
| `Characters` | Explicit allow and deny lists. Deny always wins. |
| `RecheckInterval` | How often existing accounts are rechecked. |

### When rules are checked

| Rule | Checked at |
|---|---|
| Access key | `begin` (desktop) or login start (web), before the browser goes to EVE. |
| Affiliation, character lists | After the code exchange, once the character is known. Uses ESI's public `POST /characters/affiliation`. |
| Recheck | Background job every `RecheckInterval`. If an account no longer qualifies, its sessions are revoked and its SignalR connections closed. |

## Admins

Admin character IDs are set in configuration (`Hunters__Admins__0=…`). Admins can manage access keys, view and remove accounts, and revoke sessions. An admin page in the web app is planned for later.
