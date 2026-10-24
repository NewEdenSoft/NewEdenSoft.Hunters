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
  /** CCP's flattened 2D map (SDE `position2D`): for display only, not to scale. Never measure distances on it. */
  flat: MapPoint
  /** The third axis of the 3D position, in light years (format 2; 0 in older packs). */
  height?: number
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

/**
 * The map pack of one SDE build, in a format (both from the discovery document). Immutable, so it never
 * goes stale; a new format is a new URL, so a cached pack of an older one is not used.
 */
export const mapPackQuery = (sdeBuild: number, format = 1) =>
  queryOptions({
    queryKey: ['map-pack', sdeBuild, format],
    queryFn: ({ signal }) => api<MapPack>(`/api/map/packs/${sdeBuild}?format=${format}`, { signal }),
    staleTime: Infinity,
    gcTime: Infinity,
  })
