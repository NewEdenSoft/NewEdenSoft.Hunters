import type { ActivityColumns, ActivityMetrics } from '../api/activity'
import type { HuntingPlaceProfile } from '../api/hunting'
import type { KillParty, RecentKill } from '../api/kills'
import { likelyNow } from './hunting'

/** One line of the System panel's hour stats. */
export interface MetricRow {
  label: string
  lastHour: number
  /** Against the hour before; null without one (downtime, a gap). */
  change: number | null
  /** The last 24 hours. */
  day: number
  /** Usual for this hour of the week; null without data. */
  typical: number | null
  /** "Peak now", "Peak in N h" (up to six hours ahead), or null. */
  peak: string | null
}

const metricLabels: [keyof ActivityMetrics, string][] = [
  ['npcKills', 'NPC kills'],
  ['shipKills', 'Ship kills'],
  ['podKills', 'Pod kills'],
  ['shipJumps', 'Jumps'],
]

/** The four hourly metrics of the system at `index` (map pack order). */
export function metricRows(metrics: ActivityMetrics, index: number): MetricRow[] {
  return metricLabels.map(([key, label]) => row(label, metrics[key], index))
}

function row(label: string, columns: ActivityColumns, index: number): MetricRow {
  const lastHour = columns.lastHour[index]
  return {
    label,
    lastHour,
    change: columns.previousHour ? lastHour - columns.previousHour[index] : null,
    day: columns.day[index],
    typical: columns.typical?.[index] ?? null,
    peak: peakHint(columns.peakRatio[index], columns.hoursToPeak[index]),
  }
}

/** The same rule as the hover panel: in the busiest hours (80% of the peak), or a peak within six hours. */
export function peakHint(ratio: number | null, hoursToPeak: number | null): string | null {
  if (ratio !== null && ratio >= 0.8) return 'Peak now'
  if (hoursToPeak !== null && hoursToPeak > 0 && hoursToPeak <= 6) return `Peak in ${hoursToPeak} h`
  return null
}

export type ProfileStatus = 'peak now' | 'now' | 'seen'

/**
 * A profile seen here, at this hour of the day (UTC): "now" when it is usually seen here at this hour,
 * "peak now" when this is its busiest hour here (80% of it), "seen" otherwise. As on the map.
 */
export function profileStatus(profile: Pick<HuntingPlaceProfile, 'score' | 'hours'>, hour: number): ProfileStatus {
  if (likelyNow(profile.score, profile.hours, hour) <= 0) return 'seen'
  const busiest = Math.max(0, ...profile.hours)
  return busiest > 0 && (profile.hours[hour] ?? 0) / busiest >= 0.8 ? 'peak now' : 'now'
}

/** The profiles likely now first (peak now, then now), then by score, as the server orders them. */
export function sortProfiles<T extends Pick<HuntingPlaceProfile, 'score' | 'hours'>>(profiles: T[], hour: number): T[] {
  const rank: Record<ProfileStatus, number> = { 'peak now': 0, now: 1, seen: 2 }
  return [...profiles].sort((a, b) => rank[profileStatus(a, hour)] - rank[profileStatus(b, hour)] || b.score - a.score)
}

/** ISK as EVE players write it: 950, 12.5k, 3.2M, 1.05B. */
export function formatIsk(isk: number): string {
  const units: [number, string][] = [
    [1e12, 'T'],
    [1e9, 'B'],
    [1e6, 'M'],
    [1e3, 'k'],
  ]
  for (const [size, unit] of units) {
    if (isk >= size) {
      const value = isk / size
      return `${value >= 100 ? Math.round(value) : value >= 10 ? value.toFixed(1).replace(/\.0$/, '') : value.toFixed(2).replace(/\.?0+$/, '')}${unit}`
    }
  }
  return String(Math.round(isk))
}

/** How long ago, briefly: "now", "12m", "3h", "5d". */
export function timeAgo(time: string | number, now: number): string {
  const minutes = Math.floor((now - new Date(time).getTime()) / 60_000)
  if (minutes < 1) return 'now'
  if (minutes < 60) return `${minutes}m`
  const hours = Math.floor(minutes / 60)
  if (hours < 48) return `${hours}h`
  return `${Math.floor(hours / 24)}d`
}

/** The ids the recent kills need names for: pilots, corporations, alliances and ships. */
export function killNameIds(kills: RecentKill[]): number[] {
  const ids = new Set<number>()
  const add = (party: KillParty | null) => {
    if (!party) return
    for (const id of [party.characterId, party.corporationId, party.allianceId, party.shipTypeId]) if (id) ids.add(id)
  }
  for (const kill of kills) {
    add(kill.victim)
    add(kill.finalBlow)
  }
  return [...ids]
}

/** Who a party is, in words: the pilot, else its corporation, else its ship (an NPC). */
export function partyName(party: KillParty, nameOf: (id: number) => string | undefined): string | undefined {
  if (party.characterId) return nameOf(party.characterId)
  if (party.corporationId) return nameOf(party.corporationId)
  return party.shipTypeId ? nameOf(party.shipTypeId) : undefined
}

export const zkillboardSystemUrl = (systemId: number) => `https://zkillboard.com/system/${systemId}/`
export const zkillboardKillUrl = (killmailId: number) => `https://zkillboard.com/kill/${killmailId}/`
/** Dotlan names systems in its paths, spaces as underscores. */
export const dotlanSystemUrl = (name: string) => `https://evemaps.dotlan.net/system/${encodeURIComponent(name.replaceAll(' ', '_'))}`
