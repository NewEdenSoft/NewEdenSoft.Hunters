import type { EveNames } from '../tracking/names'
import type { TrackedCharacter } from '../tracking/types'

/**
 * A tracked character's place and ship as text. Mirrors the desktop's CharacterWhereabouts, so both
 * clients describe a character the same way. Resolve {@link namesToResolve} first.
 */
export interface Whereabouts {
  /** The solar system, e.g. "Jita". */
  system: string
  /** "Docked at {station}", "Docked" in a player structure (not public), or null in space. */
  docked: string | null
  /** The ship type, with the ship's own name when it has one: "Ishtar “Hunter One”". */
  ship: string | null
}

/** Null until the tracker knows where the character is. */
export function describeWhereabouts(state: TrackedCharacter | null | undefined, names: EveNames): Whereabouts | null {
  const location = state?.location
  if (!state || !location) return null

  let docked: string | null = null
  if (location.stationId !== null) docked = names.get(location.stationId) ? `Docked at ${names.get(location.stationId)}` : 'Docked'
  else if (location.structureId !== null) docked = 'Docked'

  let ship: string | null = null
  if (state.ship) {
    const type = names.get(state.ship.typeId)
    const own = state.ship.name.trim()
    ship = !type ? state.ship.name : !own || own === type ? type : `${type} “${state.ship.name}”`
  }

  return { system: names.get(location.solarSystemId) ?? 'Unknown system', docked, ship }
}

/** The ids whose names a state needs. Player structures are not public, so they are left out. */
export function namesToResolve(state: TrackedCharacter | null | undefined): number[] {
  if (!state?.location) return []
  const ids = [state.location.solarSystemId]
  if (state.location.stationId !== null) ids.push(state.location.stationId)
  if (state.ship) ids.push(state.ship.typeId)
  return ids
}
