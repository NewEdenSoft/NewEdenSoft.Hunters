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
