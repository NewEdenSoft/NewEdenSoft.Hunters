import { securityBucketColor } from '../theme/themes'
import type { MapModel } from './model'
import { BUCKET_COUNT, NULL_BUCKET, securityBucket } from './security'

/**
 * A colour filter decides how each system is drawn: which colour group it belongs to, or whether it
 * is hidden. Security status is the first; activity, routes and fleet positions become filters too,
 * without changes to the renderer.
 */
export interface MapFilter {
  id: MapFilterId
  label: string
  /** Colour group per system index: an index into {@link colours}, or {@link HIDDEN}. */
  groups: Uint8Array
  /** CSS colours per group. Theme variables (`var(--primary)`) work; the renderer resolves them. */
  colours: string[]
  legend: MapLegendItem[]
}

export interface MapLegendItem {
  label: string
  colour: string
}

/** A system in this group is not drawn (its gates still are). */
export const HIDDEN = 255

export type MapFilterId = 'security'

export const mapFilters: { id: MapFilterId; label: string; create: (model: MapModel) => MapFilter }[] = [
  { id: 'security', label: 'Security', create: securityFilter },
]

export function createFilter(id: MapFilterId, model: MapModel): MapFilter {
  return (mapFilters.find((f) => f.id === id) ?? mapFilters[0]).create(model)
}

/** Colours systems by security status as EVE shows it, from 1.0 down to null security. */
export function securityFilter(model: MapModel): MapFilter {
  const groups = new Uint8Array(model.count)
  for (let i = 0; i < model.count; i++) groups[i] = securityBucket(model.security[i])

  const colours = Array.from({ length: BUCKET_COUNT }, (_, bucket) => securityBucketColor(bucket))
  const legend: MapLegendItem[] = []
  for (let bucket = 10; bucket >= 0; bucket--) legend.push({ label: (bucket / 10).toFixed(1), colour: colours[bucket] })
  legend.push({ label: 'Null', colour: colours[NULL_BUCKET] })

  return { id: 'security', label: 'Security', groups, colours, legend }
}
