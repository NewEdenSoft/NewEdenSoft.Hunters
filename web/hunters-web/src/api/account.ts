import { queryOptions } from '@tanstack/react-query'
import { api } from './http'

// Mirror NewEdenSoft.Hunters.Contracts.Account.
// Will be replaced by types generated from the server's OpenAPI document.
export type CharacterStatus = 'Active' | 'NeedsReauth' | 'MissingScopes'

export interface CharacterInfo {
  id: number
  name: string
  isMain: boolean
  status: CharacterStatus
  missingScopes: string[]
  addedAt: string
}

export interface AccountInfo {
  id: string
  mainCharacterId: number | null
  isAdmin: boolean
  characters: CharacterInfo[]
}

/**
 * The signed-in account. Also the session check: it fails with a 401 `ApiError` when there is
 * no valid session cookie, which the `_authed` layout route turns into a redirect to /login.
 */
export const accountQuery = queryOptions({
  queryKey: ['account'],
  queryFn: ({ signal }) => api<AccountInfo>('/api/account', { signal }),
  staleTime: 30_000,
})

export function setMainCharacter(characterId: number): Promise<void> {
  return api('/api/account/main', { method: 'PUT', body: JSON.stringify({ characterId }) })
}

/** Removes the character from the account; the server revokes its token at EVE. */
export function removeCharacter(characterId: number): Promise<void> {
  return api(`/api/characters/${characterId}`, { method: 'DELETE' })
}

/** Deletes the account with all characters and sessions, including this one. */
export function deleteAccount(): Promise<void> {
  return api('/api/account', { method: 'DELETE' })
}

/** True when the server cannot get tokens for the character until the user logs in with it again. */
export function needsReauth(character: CharacterInfo): boolean {
  return character.status !== 'Active'
}
