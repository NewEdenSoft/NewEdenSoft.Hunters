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
| `Mode` | How the affiliation and allow-list rules combine. `All`: every enabled rule must pass. `Any`: at least one must pass. |
| `AccessKey` | Creating a **new account** requires a key; existing accounts log in without one. Keys are stored hashed and created by admins, each with an optional expiry and use limit, and can be revoked. A use is counted when an account is created with the key. |
| `Affiliation` | Character must belong to one of the listed corporations or alliances. `Scope` is `FirstCharacter` (the account's main) or `AllCharacters`. |
| `Characters` | Explicit allow and deny lists. Deny always wins. |
| `RecheckInterval` | How often existing accounts are rechecked. |

### When rules are checked

| Rule | Checked at |
|---|---|
| Access key | A key that is supplied is validated at `begin` (desktop) or login start (web), so a wrong key fails before the browser goes to EVE. Whether one is *required* is known only after the code exchange, when it is clear the character has no account yet. |
| Affiliation, character lists | After the code exchange, once the character is known. Uses ESI's public `POST /characters/affiliation`. If ESI cannot be reached the login is refused (fail closed). |
| Main character change | With `Scope: FirstCharacter`, the new main must pass the rules. |
| Recheck | Background job every `RecheckInterval`. If an account no longer qualifies, its sessions are revoked. If ESI cannot be reached the run is skipped, so an ESI outage never locks everyone out. |

A rejected login revokes the freshly issued EVE token and stores nothing.

## Admins

Admin character IDs are set in configuration (`Hunters__Admins__0=…`; blank entries are ignored). An account is an admin while any of its characters is listed.

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/admin/access-keys` | List access keys |
| `POST` | `/api/admin/access-keys` | Create a key (label, optional expiry and use limit). The key is returned once. |
| `DELETE` | `/api/admin/access-keys/{id}` | Revoke a key |
| `GET` | `/api/admin/accounts` | List accounts |
| `POST` | `/api/admin/accounts/{id}/disable`, `/enable` | Disable an account (also ends its sessions and blocks login) or re-enable it |
| `DELETE` | `/api/admin/accounts/{id}/sessions` | End all sessions of an account |
| `POST` | `/api/admin/hunting/rebuild` | Ask the intel service to rebuild hunting profiles from the stored kills: `{ "profiles": ["smartbombers"] }`, or no profiles for all. 202 with the request; 400 (`hunting_profile_unknown`) for a profile it doesn't list. See [Hunting profiles](map-and-activity.md#hunting-profiles). |
| `GET` | `/api/admin/hunting/rebuild` | The latest 20 rebuild requests, newest first: when each was asked, taken up and completed, and the last error while it is retried |

An admin page in the web app is planned for later.
