import type { HuntingOverview } from '../api/hunting'
import type { ProfileStyle } from '../theme/themes'
import type { MapModel } from './model'

/** A shown profile's badge at a system. */
export interface HuntingBadge {
  profile: string
  code: string
  colour: string
  /** Seen there, but not usually at this hour: drawn faint. */
  faint: boolean
}

/** The badges of one system, drawn in a row below its name. */
export interface HuntingBadgeGroup {
  systemIndex: number
  badges: HuntingBadge[]
}

/**
 * The hunting profiles on the map: a glow per system (0–1, the strongest shown profile's "likely now"
 * there, relative to that profile's strongest system) and badges for the shown profiles seen there.
 */
export interface HuntingLayer {
  glow: Float32Array
  badges: HuntingBadgeGroup[]
}

/**
 * "Likely now": the score times this hour's share of the profile's sightings in the system. High where
 * the habit is strong and this is its usual hour.
 */
export function likelyNow(score: number, hours: number[], hour: number): number {
  let total = 0
  for (const h of hours) total += h
  return total > 0 ? (score * (hours[hour] ?? 0)) / total : 0
}

/**
 * Builds the layer for the shown profiles (in the given order) at the given hour of the day (UTC).
 * Systems not on this map are skipped. Each profile's glow is relative to its own strongest system now,
 * so a rare target still lights up where it is most likely; the square root lifts the middle values.
 */
export function buildHuntingLayer(
  model: MapModel,
  overview: HuntingOverview,
  shown: string[],
  hour: number,
  styleOf: (profile: string) => ProfileStyle,
): HuntingLayer {
  const glow = new Float32Array(model.count)
  const badges = new Map<number, HuntingBadge[]>()

  for (const profile of shown) {
    const systems = overview.profiles.find((p) => p.profile === profile)
    if (!systems) continue
    const style = styleOf(profile)
    // Only systems on this map count, also for the strongest.
    const indexes = systems.systemIds.map((id) => model.indexOf.get(id))
    const likely = systems.systemIds.map((_, i) => (indexes[i] === undefined ? 0 : likelyNow(systems.score[i], systems.hours[i], hour)))
    const strongest = Math.max(0, ...likely)

    indexes.forEach((index, i) => {
      if (index === undefined) return
      if (strongest > 0 && likely[i] > 0) glow[index] = Math.max(glow[index], Math.sqrt(likely[i] / strongest))
      let row = badges.get(index)
      if (!row) badges.set(index, (row = []))
      row.push({ profile, code: style.code, colour: style.colour, faint: likely[i] <= 0 })
    })
  }

  return { glow, badges: [...badges].map(([systemIndex, row]) => ({ systemIndex, badges: row })) }
}
