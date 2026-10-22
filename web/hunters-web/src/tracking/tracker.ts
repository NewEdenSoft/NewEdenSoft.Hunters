import type { EsiCall, LocationEsi } from './esi'
import { NeedsLoginError, type TokenProvider } from './tokens'
import { sameState, type TrackedCharacter, type TrackedCharacterChange } from './types'

/** Online characters: location and ship this often, ESI's cache time for them. */
export const LOCATION_INTERVAL_MS = 5_000
/** Online status this often, ESI's cache time for it; offline characters are only checked for coming online. */
export const ONLINE_INTERVAL_MS = 60_000
/** In a hidden tab, online characters are followed this often instead. */
export const BACKGROUND_INTERVAL_MS = 30_000
export const MAX_BACKOFF_MS = 120_000

/** 5, 10, 20, 40, 80 seconds, then {@link MAX_BACKOFF_MS}. */
export function backoff(failures: number): number {
  return Math.min(LOCATION_INTERVAL_MS * 2 ** (failures - 1), MAX_BACKOFF_MS)
}

class TrackingError extends Error {}

interface Loop {
  abort: AbortController
  done: Promise<void>
  /** Ends the current wait early (the tab became visible again). */
  wake: (() => void) | null
}

/**
 * Polls ESI for the online status, location and ship of the characters it is told to track, keeps
 * the latest state of each, and tells subscribers when something changes. The browser makes these
 * calls itself with tokens from the server (docs/decisions.md, D11). The same rules as the
 * desktop's CharacterTracker (docs/desktop-client.md, "Character tracking"), plus a slower pace in
 * a hidden tab.
 */
export class CharacterTracker {
  private readonly esi: LocationEsi
  private readonly tokens: TokenProvider
  private readonly states = new Map<number, TrackedCharacter>()
  private readonly loops = new Map<number, Loop>()
  private readonly listeners = new Set<(change: TrackedCharacterChange) => void>()
  private background = false
  private disposed = false

  constructor(esi: LocationEsi, tokens: TokenProvider) {
    this.esi = esi
    this.tokens = tokens
  }

  /** The latest state of every tracked character. */
  get characters(): TrackedCharacter[] {
    return [...this.states.values()]
  }

  get(characterId: number): TrackedCharacter | undefined {
    return this.states.get(characterId)
  }

  /** Calls the listener on every change; returns the function that unsubscribes it. */
  subscribe(listener: (change: TrackedCharacterChange) => void): () => void {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }

  /**
   * Tracks exactly these characters: starts the new ones and stops the rest. Call it again when the
   * account or the user's choice changes; a character that needed a login starts again.
   */
  track(characterIds: Iterable<number>): void {
    if (this.disposed) throw new Error('The tracker has been disposed.')
    const wanted = new Set(characterIds)

    for (const [id, loop] of this.loops) {
      if (wanted.has(id)) continue
      this.loops.delete(id)
      loop.abort.abort()
      this.publish(id, null)
    }

    for (const id of wanted) {
      if (this.loops.has(id)) continue
      const loop: Loop = { abort: new AbortController(), done: Promise.resolve(), wake: null }
      this.loops.set(id, loop)
      loop.done = this.run(id, loop).finally(() => {
        // A loop that ended by itself (needs login) can be started again by the next track().
        if (this.loops.get(id) === loop && !loop.abort.signal.aborted) this.loops.delete(id)
      })
    }
  }

  /** A hidden tab polls online characters less often; becoming visible again polls straight away. */
  setBackground(background: boolean): void {
    if (background === this.background) return
    this.background = background
    if (!background) for (const loop of this.loops.values()) loop.wake?.()
  }

  async dispose(): Promise<void> {
    this.disposed = true
    const loops = [...this.loops.values()]
    this.loops.clear()
    for (const loop of loops) loop.abort.abort()
    await Promise.all(loops.map((l) => l.done))
  }

  private async run(characterId: number, loop: Loop): Promise<void> {
    const { signal } = loop.abort
    // Started again after a login: keep the last known location and ship until ESI answers.
    const previous = this.states.get(characterId)
    let state: TrackedCharacter = {
      characterId,
      isOnline: previous?.isOnline ?? null,
      location: previous?.location ?? null,
      ship: previous?.ship ?? null,
      updatedAt: previous?.updatedAt ?? null,
      status: 'starting',
      error: null,
    }
    this.publish(characterId, state)

    let nextOnlineCheck = 0
    let failures = 0
    while (!signal.aborted) {
      let delay: number
      try {
        const token = await this.tokens.getAccessToken(characterId, signal)
        const now = Date.now()

        let isOnline = state.isOnline
        if (now >= nextOnlineCheck) {
          isOnline = this.require(await this.esi.getOnline(characterId, token, signal), characterId)
          nextOnlineCheck = now + ONLINE_INTERVAL_MS
        }

        let { location, ship } = state
        // Online: follow them. Offline: read where they logged off, once.
        if (isOnline || !location) {
          location = this.require(await this.esi.getLocation(characterId, token, signal), characterId)
          ship = this.require(await this.esi.getShip(characterId, token, signal), characterId)
        }

        state = { characterId, status: 'tracking', isOnline, location, ship, error: null, updatedAt: now }
        failures = 0
        delay = isOnline ? (this.background ? BACKGROUND_INTERVAL_MS : LOCATION_INTERVAL_MS) : nextOnlineCheck - now
      } catch (error) {
        if (signal.aborted) return
        if (error instanceof NeedsLoginError) {
          // The server already flags the character; nothing to poll until it logs in again.
          this.publish(characterId, { ...state, status: 'needsLogin', error: error.message })
          return
        }
        failures++
        state = { ...state, status: 'failing', error: (error as Error).message }
        delay = backoff(failures)
      }

      this.publish(characterId, state)
      await this.sleep(delay, loop)
    }
  }

  private require<T>(call: EsiCall<T>, characterId: number): T {
    switch (call.status) {
      case 'ok':
        return call.value
      case 'unauthorized':
        // Ask the server for a fresh token on the next attempt.
        this.tokens.invalidate(characterId)
        throw new TrackingError(`ESI rejected the access token: ${call.error}`)
      case 'forbidden':
        throw new TrackingError(`ESI refused to share the location; the character may be missing a permission. ${call.error}`)
      default:
        throw new TrackingError(`ESI did not answer: ${call.error}`)
    }
  }

  private sleep(ms: number, loop: Loop): Promise<void> {
    return new Promise((resolve) => {
      const done = () => {
        clearTimeout(timer)
        loop.abort.signal.removeEventListener('abort', done)
        loop.wake = null
        resolve()
      }
      const timer = setTimeout(done, ms)
      loop.abort.signal.addEventListener('abort', done)
      loop.wake = done
    })
  }

  private publish(characterId: number, state: TrackedCharacter | null): void {
    const previous = this.states.get(characterId) ?? null
    // A stopped loop may still finish an attempt; it no longer owns the character.
    if (state && (this.disposed || !this.loops.has(characterId))) return

    if (state) this.states.set(characterId, state)
    else this.states.delete(characterId)

    if (sameState(previous, state)) return
    const change: TrackedCharacterChange = { characterId, previous, current: state }
    for (const listener of this.listeners) listener(change)
  }
}
