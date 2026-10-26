import type { MapModel } from './model'
import { neighboursOf } from './route'

/** When the focused system may change by itself, and how the map's clicks work (Settings, "Map behaviour"). */
export interface MapBehaviour {
  /** Focus a character's system when it comes from unknown space (wormhole space, or anywhere not on the map) into known space. */
  enterKnownSpace: boolean
  /** Focus a character's system after a move of at least {@link minJumps} gate jumps (a filament, a jump bridge, a cyno). */
  longMove: boolean
  minJumps: number
  /** Double click a system to focus it. A single click selects it either way. */
  doubleClickFocus: boolean
}

export const defaultMapBehaviour: MapBehaviour = { enterKnownSpace: true, longMove: true, minJumps: 5, doubleClickFocus: true }

/** The minimum jumps offered for a long move. */
export const minJumpChoices = [2, 3, 5, 10, 20]

/** Why a move focuses its new system, or null when it doesn't. */
export type AutoFocusReason = 'known-space' | 'long-move'

/**
 * Whether a character's move from one system to another should focus the new one. Coming from a system
 * that isn't on the map (unknown space) into one that is: "known-space". Between two systems on the
 * map, at least `minJumps` gate jumps apart (or with no gate route at all): "long-move". A first known
 * location (no previous system) never focuses.
 */
export function autoFocusReason(model: MapModel, from: number | null, to: number, behaviour: MapBehaviour): AutoFocusReason | null {
  const target = model.indexOf.get(to)
  if (from === null || from === to || target === undefined) return null
  const origin = model.indexOf.get(from)
  if (origin === undefined) return behaviour.enterKnownSpace ? 'known-space' : null
  if (!behaviour.longMove) return null
  return jumpsWithin(model, origin, target, behaviour.minJumps - 1) ? null : 'long-move'
}

/** Whether `to` is at most `limit` gate jumps from `from` (a breadth-first search that stops at the limit). */
export function jumpsWithin(model: MapModel, from: number, to: number, limit: number): boolean {
  if (from === to) return true
  const adjacent = neighboursOf(model)
  const seen = new Set([from])
  let frontier = [from]
  for (let depth = 1; depth <= limit && frontier.length > 0; depth++) {
    const next: number[] = []
    for (const system of frontier) {
      for (const n of adjacent[system]) {
        if (n === to) return true
        if (!seen.has(n)) {
          seen.add(n)
          next.push(n)
        }
      }
    }
    frontier = next
  }
  return false
}
