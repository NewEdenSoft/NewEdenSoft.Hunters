// Mirror NewEdenSoft.Hunters.Client.Tracking (TrackedCharacter.cs): the web follows the same rules.

export type TrackingStatus =
  /** Tracking began; nothing has been read from ESI yet. */
  | 'starting'
  /** ESI answers; the state is current. */
  | 'tracking'
  /** The server can no longer refresh the character's token. Tracking stops until the character logs in again. */
  | 'needsLogin'
  /** The last attempt failed (server, ESI or permission); retrying with backoff. The last good state is kept. */
  | 'failing'

/** Where a character is: its solar system, and the station or structure it is docked in. */
export interface CharacterLocation {
  solarSystemId: number
  stationId: number | null
  structureId: number | null
}

/** The ship a character is flying. */
export interface CharacterShip {
  typeId: number
  name: string
  itemId: number
}

/**
 * What the tracker knows about one character. `location` and `ship` stay at their last known
 * values while the character is offline or polling fails.
 */
export interface TrackedCharacter {
  characterId: number
  status: TrackingStatus
  isOnline: boolean | null
  location: CharacterLocation | null
  ship: CharacterShip | null
  error: string | null
  /** When ESI last answered, in milliseconds since the epoch. */
  updatedAt: number | null
}

/** A tracked character changed. `current` is null when it is no longer tracked. */
export interface TrackedCharacterChange {
  characterId: number
  previous: TrackedCharacter | null
  current: TrackedCharacter | null
}

export function isDocked(location: CharacterLocation): boolean {
  return location.stationId !== null || location.structureId !== null
}

/** The character moved to another solar system (or its first known one). */
export function systemChanged(change: TrackedCharacterChange): boolean {
  return change.current?.location?.solarSystemId !== change.previous?.location?.solarSystemId
}

/** The same apart from `updatedAt`: nothing a subscriber needs to hear about. */
export function sameState(a: TrackedCharacter | null, b: TrackedCharacter | null): boolean {
  if (!a || !b) return a === b
  return (
    a.characterId === b.characterId &&
    a.status === b.status &&
    a.isOnline === b.isOnline &&
    a.error === b.error &&
    a.location?.solarSystemId === b.location?.solarSystemId &&
    a.location?.stationId === b.location?.stationId &&
    a.location?.structureId === b.location?.structureId &&
    a.ship?.typeId === b.ship?.typeId &&
    a.ship?.name === b.ship?.name &&
    a.ship?.itemId === b.ship?.itemId
  )
}
