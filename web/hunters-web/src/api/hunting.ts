import { queryOptions } from '@tanstack/react-query'
import { api } from './http'

// Mirrors NewEdenSoft.Hunters.Contracts.Hunting.

/** A hunting profile: a kind of target, such as Carriers. */
export interface HuntingProfileInfo {
  id: string
  name: string
}

/** One profile's systems with sightings, highest score first, as columns: entry `i` belongs to `systemIds[i]`. */
export interface HuntingProfileSystems {
  profile: string
  systemIds: number[]
  /** Weighted sightings, each halving every 14 days: how established the habit is. */
  score: number[]
  week: number[]
  month: number[]
  lastSeen: string[]
  /** Per system, 24 weighted sightings by hour of the day (UTC). */
  hours: number[][]
}

export interface HuntingOverview {
  version: number
  updatedAt: string
  profiles: HuntingProfileSystems[]
}

export const huntingProfilesQuery = queryOptions({
  queryKey: ['hunting', 'profiles'],
  queryFn: ({ signal }) => api<HuntingProfileInfo[]>('/api/hunting/profiles', { signal }),
  staleTime: 60 * 60_000,
})

/** Where each profile is seen. The aggregates move every few minutes; the browser revalidates with the ETag. */
export const huntingOverviewQuery = queryOptions({
  queryKey: ['hunting', 'overview'],
  queryFn: ({ signal }) => api<HuntingOverview>('/api/hunting/overview', { signal }),
  staleTime: 5 * 60_000,
  refetchInterval: 5 * 60_000,
})
