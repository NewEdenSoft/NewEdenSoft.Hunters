# Accounts & Characters

Character registration and account management come first: every other feature depends on knowing who the user is and which characters they own.

## Model

| Entity | Key fields |
|---|---|
| `Account` | Id, created, main character, status (active / disabled) |
| `Character` | EVE character id, account id, name, owner hash, granted scopes, encrypted refresh token, cached access token + expiry, status, row version |
| `Session` | Id, account id, kind (web / desktop), device name, created, last seen, revoked |
| `AccessKey` | Hash, label, expiry, use limit, uses, revoked (see [Admission](discovery-and-admission.md)) |

## Lifecycle

### First login
Creates the account. The first character becomes the **main** character.

### Adding characters
Same login flow with the current session attached ([Authentication](authentication.md)). The character is linked to the current account.

### Logging in with a character already linked to an account
Signs in to that account. A character belongs to exactly one account; adding a character that is already linked to a different account is rejected. Account merging may come later.

### Character ownership changes
The EVE SSO token carries an `owner` hash that changes when a character is transferred to another player. If a login or refresh returns a different owner hash than stored, the character is unlinked from its account and its token revoked. The new owner can register it normally.

### Character status

| Status | Meaning | User action |
|---|---|---|
| `Active` | Token valid, all required scopes granted | — |
| `NeedsReauth` | Refresh failed or the token was lost | Log in with the character again |
| `MissingScopes` | Server's scope configuration now requires scopes the character has not granted | Log in with the character again |

### Changing main character
Allowed. If admission uses `Affiliation.Scope = FirstCharacter`, the new main must pass admission.

### Removing a character
Revokes the refresh token at EVE and deletes the character. The main character cannot be removed while other characters exist; change main first.

### Sessions and devices
Users can list their sessions (device name, kind, last seen) and revoke any of them. Revoking closes that session's SignalR connections.

### Deleting the account
Revokes all tokens at EVE, deletes all account data, closes all connections.

## API (sketch)

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/account` | Account, main character, characters with status |
| `PUT` | `/api/account/main` | Change main character |
| `DELETE` | `/api/account` | Delete account |
| `DELETE` | `/api/characters/{id}` | Remove character |
| `POST` | `/api/characters/{id}/access-token` | Get an access token for client ESI calls |
| `GET` | `/api/sessions` | List sessions |
| `DELETE` | `/api/sessions/{id}` | Revoke session |

Admin endpoints (accounts, access keys, disabling accounts) live under `/api/admin/*`.

## UI

Both clients get an account screen: characters with portrait, status and re-auth button, add character, set main, remove; sessions list with revoke; delete account.
