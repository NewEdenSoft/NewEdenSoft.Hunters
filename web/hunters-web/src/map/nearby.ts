import type { ReachRequest, ReachSort, ReachSystem } from '../api/reach'

/** What the Nearby panel looks for around the focused system (Nearby panel, kept in this browser). */
export interface NearbySetting {
  /** Systems within this many gate jumps; null for none by gates. */
  jumps: number | null
  /** Cyno targets within the Jump range panel's light years too. */
  byJumpRange: boolean
  /** "activity" (the rise in NPC kills), "npc-kills" (the most), or a hunting profile's id (its "likely now"). */
  sort: string
  /** Show the systems without any activity too (hidden by default). */
  showQuiet: boolean
}

export const defaultNearby: NearbySetting = { jumps: 5, byJumpRange: false, sort: 'activity', showQuiet: false }

/** The gate jumps offered. */
export const nearbyJumpChoices = [3, 5, 10, 15]

/** Cards shown: more is a long list to read in a panel. */
export const NEARBY_LIMIT = 50

/** Rows asked for: enough that 50 active systems are left once the quiet ones are hidden. */
export const NEARBY_FETCH = 200

/** A saved setting, or the defaults for anything missing or out of range. */
export function parseNearby(value: unknown): NearbySetting {
  const saved = typeof value === 'object' && value !== null ? (value as Partial<Record<keyof NearbySetting, unknown>>) : {}
  const jumps = saved.jumps === null ? null : typeof saved.jumps === 'number' && nearbyJumpChoices.includes(saved.jumps) ? saved.jumps : defaultNearby.jumps
  return {
    jumps,
    byJumpRange: saved.byJumpRange === true,
    sort: typeof saved.sort === 'string' && saved.sort !== '' ? saved.sort : defaultNearby.sort,
    showQuiet: saved.showQuiet === true,
  }
}

/**
 * The reach query for the setting, from the focused system: null without a focus, or with neither gate
 * jumps nor jump range chosen. Gate jumps limit the list, so the light years are only asked for without
 * them (the server's union would spend the rows on systems the list leaves out). A sort by a profile the
 * server doesn't list falls back to activity.
 */
export function nearbyRequest(setting: NearbySetting, from: number | null, rangeLy: number, profiles: string[]): ReachRequest | null {
  if (from === null || (setting.jumps === null && !setting.byJumpRange)) return null
  return {
    from,
    jumps: setting.jumps,
    ly: setting.jumps === null ? rangeLy : null,
    profile: profiles.includes(setting.sort) ? setting.sort : null,
    sort: npcSort(setting.sort),
    limit: NEARBY_FETCH,
  }
}

/** The server's order when not sorting by a profile: the most NPC kills, or else their rise. */
export function npcSort(sort: string): ReachSort {
  return sort === 'npc-kills' ? 'npc-kills' : 'npc-rising'
}

/**
 * Something happens here: NPC, ship or pod kills in the last hour or the last 24 hours, or a hunting
 * profile seen. Jumps alone don't count: traffic passing through isn't a target. Without activity data
 * (the server has none yet) a system can't be judged, so it counts as active.
 */
export function isActive(row: Pick<ReachSystem, 'activity' | 'profiles'>): boolean {
  if (row.profiles.length > 0 || row.activity === null) return true
  const { npcKills, shipKills, podKills } = row.activity
  return [npcKills, shipKills, podKills].some((m) => m.lastHour > 0 || m.day > 0)
}

/**
 * Within the gate jumps chosen (any row without them). Checked here as well as by the server, so the
 * previous answer, shown while the next loads, doesn't list systems beyond a smaller choice.
 */
export function withinJumps(row: Pick<ReachSystem, 'jumps'>, jumps: number | null): boolean {
  return jumps === null || (row.jumps !== null && row.jumps <= jumps)
}

/** How far a row is: "3 j", "4.2 ly", or both when both were asked for. Null jumps: no gate route. */
export function distanceText(row: Pick<ReachSystem, 'jumps' | 'ly'>, byJumps: boolean, byLy: boolean): string {
  const jumps = row.jumps === null ? 'no gates' : `${row.jumps} j`
  const ly = `${row.ly < 10 ? row.ly.toFixed(1) : Math.round(row.ly)} ly`
  if (byJumps && byLy) return `${jumps} · ${ly}`
  return byLy ? ly : jumps
}

/** Within the jump drive's reach: a cyno target within the light years (when they were asked for). */
export function inJumpRange(row: Pick<ReachSystem, 'cynoTarget' | 'ly'>, ly: number | null): boolean {
  return ly !== null && row.cynoTarget && row.ly <= ly
}
