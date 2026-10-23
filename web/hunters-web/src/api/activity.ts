import { queryOptions } from '@tanstack/react-query'
import { api } from './http'

// Mirrors NewEdenSoft.Hunters.Contracts.Activity.

/** One metric for every system (or region), as columns in map pack order. */
export interface ActivityColumns {
  lastHour: number[]
  /** Null when the hour before is missing (downtime, a gap): no change can be shown. */
  previousHour: number[] | null
  day: number[]
  typical: number[] | null
  peakRatio: (number | null)[]
  hoursToPeak: (number | null)[]
  peakHourOfWeek: (number | null)[]
}

export interface ActivityMetrics {
  npcKills: ActivityColumns
  shipKills: ActivityColumns
  podKills: ActivityColumns
  shipJumps: ActivityColumns
}

/**
 * Activity now for the whole map: entry `i` of each system column is `MapPack.systems[i]` of the map
 * pack built from `sdeBuild` (and of each region column, `MapPack.regions[i]`).
 */
export interface ActivityCurrent {
  version: number
  sdeBuild: number
  killsPeriodEnd: string
  jumpsPeriodEnd: string
  killsDayHours: number
  jumpsDayHours: number
  systems: ActivityMetrics
  regions: ActivityMetrics
}

/**
 * The server's latest hour of activity. It changes once an hour; asking every minute is cheap, since the
 * browser revalidates with the ETag and the server answers 304 until there is a new hour.
 */
export const activityQuery = queryOptions({
  queryKey: ['activity', 'current'],
  queryFn: ({ signal }) => api<ActivityCurrent>('/api/activity/current', { signal }),
  staleTime: 60_000,
  refetchInterval: 60_000,
})
