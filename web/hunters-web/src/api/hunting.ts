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

/** A profile seen in one system. */
export interface HuntingPlaceProfile {
  profile: string
  name: string
  score: number
  week: number
  month: number
  /** The part of `month` from indirect evidence (CRAB beacons for Carriers). */
  monthIndicator: number
  /** Different pilots seen in the last 30 days. */
  pilots: number
  lastSeen: string
  /** 24 weighted sightings by hour of the day (UTC). */
  hours: number[]
  /** The part of `month` from locals: seen in their own alliance's sovereignty (counting 1.5 times). */
  monthLocal: number
  /** The part of `pilots` seen as locals. */
  localPilots: number
}

/** The profiles seen in one system, highest score first; empty when none were. */
export interface HuntingPlace {
  id: number
  version: number
  updatedAt: string
  profiles: HuntingPlaceProfile[]
}

export const systemHuntingQuery = (systemId: number) =>
  queryOptions({
    queryKey: ['hunting', 'system', systemId],
    queryFn: ({ signal }) => api<HuntingPlace>(`/api/hunting/systems/${systemId}`, { signal }),
    staleTime: 5 * 60_000,
  })

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
