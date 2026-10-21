import { queryOptions } from '@tanstack/react-query'
import { api } from './http'

// Mirror NewEdenSoft.Hunters.Contracts.Map. Positions are light years, x east, y south.
export interface MapPoint {
  x: number
  y: number
}

export interface MapRegion {
  id: number
  name: string
}

export interface MapSystem {
  id: number
  name: string
  regionId: number
  constellationId: number
  security: number
  topDown: MapPoint
  flat: MapPoint
}

export interface MapGate {
  from: number
  to: number
}

export interface MapPack {
  sdeBuild: number
  regions: MapRegion[]
  systems: MapSystem[]
  gates: MapGate[]
}

/** The map pack of one SDE build. Immutable, so it never goes stale. */
export const mapPackQuery = (sdeBuild: number) =>
  queryOptions({
    queryKey: ['map-pack', sdeBuild],
    queryFn: ({ signal }) => api<MapPack>(`/api/map/packs/${sdeBuild}`, { signal }),
    staleTime: Infinity,
    gcTime: Infinity,
  })
