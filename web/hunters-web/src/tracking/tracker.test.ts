import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { EsiCall, EsiCallStatus, LocationEsi } from './esi'
import { NeedsLoginError, type TokenProvider } from './tokens'
import {
  backoff,
  BACKGROUND_INTERVAL_MS,
  CharacterTracker,
  LOCATION_INTERVAL_MS,
  MAX_BACKOFF_MS,
  ONLINE_INTERVAL_MS,
} from './tracker'
import { systemChanged, type CharacterLocation, type CharacterShip, type TrackedCharacterChange } from './types'

const PILOT = 90000001
const JITA: CharacterLocation = { solarSystemId: 30000142, stationId: 60003760, structureId: null }
const PERIMETER: CharacterLocation = { solarSystemId: 30000144, stationId: null, structureId: null }
const ISHTAR: CharacterShip = { typeId: 12005, name: 'Ishtar', itemId: 1000000001 }

class FakeEsi implements LocationEsi {
  readonly calls = { online: 0, location: 0, ship: 0 }
  private character = { online: true, location: JITA, ship: ISHTAR }
  private failNext: Exclude<EsiCallStatus, 'ok'> | null = null

  set(online: boolean, location: CharacterLocation, ship = ISHTAR) {
    this.character = { online, location, ship }
  }

  fail(status: Exclude<EsiCallStatus, 'ok'>) {
    this.failNext = status
  }

  getOnline = () => this.answer('online', (c) => c.online)
  getLocation = () => this.answer('location', (c) => c.location)
  getShip = () => this.answer('ship', (c) => c.ship)

  private answer<T>(call: keyof FakeEsi['calls'], select: (c: FakeEsi['character']) => T): Promise<EsiCall<T>> {
    this.calls[call]++
    if (this.failNext) {
      const status = this.failNext
      this.failNext = null
      return Promise.resolve({ status, error: 'token expired' })
    }
    return Promise.resolve({ status: 'ok', value: select(this.character) })
  }
}

class FakeTokens implements TokenProvider {
  requests = 0
  invalidations = 0
  failure: Error | null = null
  getAccessToken = () => {
    this.requests++
    return this.failure ? Promise.reject(this.failure) : Promise.resolve('token')
  }
  invalidate = () => {
    this.invalidations++
  }
}

describe('CharacterTracker', () => {
  let esi: FakeEsi
  let tokens: FakeTokens
  let tracker: CharacterTracker
  let changes: TrackedCharacterChange[]

  beforeEach(() => {
    vi.useFakeTimers()
    esi = new FakeEsi()
    tokens = new FakeTokens()
    tracker = new CharacterTracker(esi, tokens)
    changes = []
    tracker.subscribe((c) => changes.push(c))
  })

  afterEach(async () => {
    await tracker.dispose()
    vi.useRealTimers()
  })

  const settle = () => vi.advanceTimersByTimeAsync(0)

  it('follows online characters every five seconds', async () => {
    tracker.track([PILOT])
    await settle()

    expect(tracker.get(PILOT)).toMatchObject({ status: 'tracking', isOnline: true, location: JITA, ship: ISHTAR })
    expect(changes.some((c) => systemChanged(c) && c.current?.location === JITA)).toBe(true)

    esi.set(true, PERIMETER)
    await vi.advanceTimersByTimeAsync(LOCATION_INTERVAL_MS)

    expect(tracker.get(PILOT)?.location).toEqual(PERIMETER)
    expect(esi.calls.location).toBe(2)
    expect(changes.some((c) => systemChanged(c) && c.previous?.location === JITA && c.current?.location === PERIMETER)).toBe(true)
  })

  it('raises nothing for polls that change nothing', async () => {
    tracker.track([PILOT])
    await settle()
    const before = changes.length

    await vi.advanceTimersByTimeAsync(LOCATION_INTERVAL_MS * 3)

    expect(esi.calls.location).toBe(4)
    expect(changes.length).toBe(before)
  })

  it('reads offline characters once, then only checks for them coming online', async () => {
    esi.set(false, JITA)
    tracker.track([PILOT])
    await settle()
    expect(tracker.get(PILOT)).toMatchObject({ isOnline: false, location: JITA })

    await vi.advanceTimersByTimeAsync(ONLINE_INTERVAL_MS - 1)
    expect(esi.calls).toEqual({ online: 1, location: 1, ship: 1 })

    esi.set(true, PERIMETER)
    await vi.advanceTimersByTimeAsync(1)
    expect(tracker.get(PILOT)).toMatchObject({ isOnline: true, location: PERIMETER })
    expect(esi.calls.online).toBe(2)
  })

  it('stops a character that needs a login until it is tracked again', async () => {
    tracker.track([PILOT])
    await settle()
    tokens.failure = new NeedsLoginError('Log in again.')
    await vi.advanceTimersByTimeAsync(LOCATION_INTERVAL_MS)
    expect(tracker.get(PILOT)).toMatchObject({ status: 'needsLogin', location: JITA })

    const requests = tokens.requests
    await vi.advanceTimersByTimeAsync(MAX_BACKOFF_MS)
    expect(tokens.requests).toBe(requests)

    tokens.failure = null
    esi.set(true, PERIMETER)
    tracker.track([PILOT])
    expect(tracker.get(PILOT)).toMatchObject({ status: 'starting', location: JITA })
    await settle()
    expect(tracker.get(PILOT)).toMatchObject({ status: 'tracking', location: PERIMETER })
  })

  it('drops a rejected token, backs off, and recovers', async () => {
    esi.fail('unauthorized')
    tracker.track([PILOT])
    await settle()

    expect(tracker.get(PILOT)).toMatchObject({ status: 'failing' })
    expect(tracker.get(PILOT)?.error).toContain('access token')
    expect(tokens.invalidations).toBe(1)

    await vi.advanceTimersByTimeAsync(backoff(1))
    expect(tracker.get(PILOT)).toMatchObject({ status: 'tracking', error: null })
  })

  it('removes characters that are no longer tracked', async () => {
    tracker.track([PILOT])
    await settle()

    tracker.track([])

    expect(tracker.characters).toEqual([])
    expect(changes.at(-1)).toMatchObject({ characterId: PILOT, current: null })
    const calls = esi.calls.location
    await vi.advanceTimersByTimeAsync(LOCATION_INTERVAL_MS * 3)
    expect(esi.calls.location).toBe(calls)
  })

  it('polls less in a hidden tab and straight away when it is shown again', async () => {
    tracker.track([PILOT])
    await settle()
    tracker.setBackground(true)
    await vi.advanceTimersByTimeAsync(LOCATION_INTERVAL_MS) // the wait already started
    expect(esi.calls.location).toBe(2)

    await vi.advanceTimersByTimeAsync(BACKGROUND_INTERVAL_MS - 1)
    expect(esi.calls.location).toBe(2)

    tracker.setBackground(false)
    await settle()
    expect(esi.calls.location).toBe(3)
  })

  it('backs off 5, 10, 20, 40, 80 seconds, then every two minutes', () => {
    expect([1, 2, 3, 4, 5, 6, 20].map(backoff)).toEqual([5_000, 10_000, 20_000, 40_000, 80_000, 120_000, 120_000])
  })
})
