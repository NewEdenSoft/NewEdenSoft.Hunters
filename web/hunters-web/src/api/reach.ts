import { queryOptions } from '@tanstack/react-query'
import { api } from './http'

// Mirrors NewEdenSoft.Hunters.Contracts.Intel (Reach.cs).

/** One metric: last hour, the hour before (null without one), 24 hours, the usual, and the peak. */
export interface ReachMetric {
  lastHour: number
  previousHour: number | null
  day: number
  typical: number | null
  peakRatio: number | null
  hoursToPeak: number | null
}

export interface ReachActivity {
  npcKills: ReachMetric
  shipKills: ReachMetric
  podKills: ReachMetric
  shipJumps: ReachMetric
}

/** A hunting profile seen in a system: how likely it is there now, and whether this is its busiest hour there. */
export interface ReachProfile {
  profile: string
  score: number
  lastSeen: string
  likelyNow: number
  peakNow: number | null
}

export interface ReachSystem {
  systemId: number
  /** Gate jumps from the origin; null when no gates lead there. */
  jumps: number | null
  /** Straight-line distance from the origin, in light years. */
  ly: number
  security: number
  /** A jump drive can land here: not high-sec, Pochven or Zarzakh. */
  cynoTarget: boolean
  activity: ReachActivity | null
  profiles: ReachProfile[]
}

/** The orders without a profile: the rise in NPC kills (the default), or the most NPC kills. */
export type ReachSort = 'npc-rising' | 'npc-kills'

export interface IntelReach {
  from: number
  jumps: number | null
  ly: number | null
  profile: string | null
  /** How many systems are in reach; `systems` holds at most the limit. */
  total: number
  hourOfDay: number
  activityVersion: number | null
  huntingVersion: number | null
  systems: ReachSystem[]
  sort: ReachSort
}

export interface ReachRequest {
  from: number
  jumps: number | null
  ly: number | null
  /** Sort by this profile's "likely now"; null sorts by `sort`. */
  profile: string | null
  sort: ReachSort
  limit: number
}

/** What is in reach of a system. The server's ETag moves with the hour and the data, so this is cheap to ask again. */
export const reachQuery = (request: ReachRequest) =>
  queryOptions({
    queryKey: ['intel', 'reach', request],
    queryFn: ({ signal }) => {
      const query = new URLSearchParams({ from: String(request.from), limit: String(request.limit), sort: request.sort })
      if (request.jumps !== null) query.set('jumps', String(request.jumps))
      if (request.ly !== null) query.set('ly', String(request.ly))
      if (request.profile !== null) query.set('profile', request.profile)
      return api<IntelReach>(`/api/intel/reach?${query}`, { signal })
    },
    staleTime: 60_000,
    refetchInterval: 2 * 60_000,
    // Keep the old rows while the next answer loads (another origin, another sort).
    placeholderData: (previous) => previous,
  })
