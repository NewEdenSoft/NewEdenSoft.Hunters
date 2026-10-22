import { api, ApiError } from '../api/http'

/** Access tokens for the account's characters, for the app's own ESI calls. The server holds the refresh tokens. */
export interface TokenProvider {
  /** A token valid for at least another minute. Throws {@link NeedsLoginError} when the server can no longer refresh it. */
  getAccessToken(characterId: number, signal?: AbortSignal): Promise<string>
  /** Drops a cached token that ESI rejected, so the next request asks the server again. */
  invalidate(characterId: number): void
}

/** The server can no longer refresh the character's token; the user must log in with it again. */
export class NeedsLoginError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'NeedsLoginError'
  }
}

/** Server error code: the character's refresh token no longer works (AuthErrorCodes.NeedsReauth). */
export const NEEDS_REAUTH = 'needs_reauth'

/** Cached tokens are replaced this long before they expire. */
export const TOKEN_MARGIN_MS = 60_000

interface AccessTokenResponse {
  characterId: number
  accessToken: string
  expiresAt: string
}

/**
 * Tokens from the server (`POST /api/characters/{id}/access-token`, with the session cookie),
 * cached until a minute before they expire. Concurrent requests for one character share a call.
 */
export class ServerTokenProvider implements TokenProvider {
  private readonly tokens = new Map<number, { token: string; expiresAt: number }>()
  private readonly inFlight = new Map<number, Promise<string>>()
  private readonly now: () => number

  constructor(now: () => number = Date.now) {
    this.now = now
  }

  getAccessToken(characterId: number): Promise<string> {
    const cached = this.tokens.get(characterId)
    if (cached && cached.expiresAt - TOKEN_MARGIN_MS > this.now()) return Promise.resolve(cached.token)

    let request = this.inFlight.get(characterId)
    if (!request) {
      request = this.request(characterId).finally(() => this.inFlight.delete(characterId))
      this.inFlight.set(characterId, request)
    }
    return request
  }

  invalidate(characterId: number): void {
    this.tokens.delete(characterId)
  }

  private async request(characterId: number): Promise<string> {
    try {
      const response = await api<AccessTokenResponse>(`/api/characters/${characterId}/access-token`, { method: 'POST' })
      this.tokens.set(characterId, { token: response.accessToken, expiresAt: Date.parse(response.expiresAt) })
      return response.accessToken
    } catch (error) {
      if (error instanceof ApiError && error.code === NEEDS_REAUTH) throw new NeedsLoginError(error.message)
      throw error
    }
  }
}
