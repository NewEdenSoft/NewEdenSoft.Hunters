import { createContext, useContext, useEffect, useState, useSyncExternalStore } from 'react'
import { describeWhereabouts, namesToResolve, type Whereabouts } from '../characters/whereabouts'
import type { EveNames } from './names'
import type { TokenProvider } from './tokens'
import type { CharacterTracker } from './tracker'
import type { TrackedCharacter } from './types'

/** The signed-in session's tracker, name cache and character tokens; null outside the signed-in pages. */
export interface Tracking {
  tracker: CharacterTracker
  names: EveNames
  /** The same tokens the tracker uses, for the app's other ESI calls (setting a destination). */
  tokens: TokenProvider
}

export const TrackingContext = createContext<Tracking | null>(null)

export function useTracking(): Tracking | null {
  return useContext(TrackingContext)
}

const noop = () => () => {}

/** What the tracker knows about a character, kept in step with it; null while not tracked. */
export function useTrackedCharacter(characterId: number): TrackedCharacter | null {
  const tracking = useTracking()
  return useSyncExternalStore(
    tracking ? (onChange) => tracking.tracker.subscribe((c) => c.characterId === characterId && onChange()) : noop,
    () => tracking?.tracker.get(characterId) ?? null,
  )
}

/** A tracked state's system, docked and ship text, once their names are looked up. */
export function useWhereabouts(state: TrackedCharacter | null): Whereabouts | null {
  const tracking = useTracking()
  const [, setResolved] = useState(0)

  useEffect(() => {
    if (!tracking) return
    const ids = namesToResolve(state)
    if (ids.every((id) => tracking.names.get(id) !== undefined)) return
    let current = true
    void tracking.names.resolve(ids).then(() => current && setResolved((n) => n + 1))
    return () => {
      current = false
    }
  }, [tracking, state])

  return tracking ? describeWhereabouts(state, tracking.names) : null
}
