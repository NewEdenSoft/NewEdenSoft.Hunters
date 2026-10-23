import type { ActivityColumns, ActivityCurrent } from '../api/activity'
import { heatBands, securityBucketColor, type HeatMetric } from '../theme/themes'
import type { MapModel } from './model'
import { BUCKET_COUNT, NULL_BUCKET, securityBucket } from './security'

/**
 * A colour filter decides how each system is drawn: which colour group it belongs to, or whether it
 * is hidden. Security status and the hourly activity are filters; routes and fleet positions become
 * filters too, without changes to the renderer.
 */
export interface MapFilter {
  id: MapFilterId
  label: string
  /** Colour group per system index: an index into {@link colours}, or {@link HIDDEN}. */
  groups: Uint8Array
  /** CSS colours per group. Theme variables (`var(--primary)`) work; the renderer resolves them. */
  colours: string[]
  legend: MapLegendItem[]
  /** Why the filter shows nothing yet (no activity data, no previous hour), or null. */
  notice: string | null
  /** Optional radius multiplier per group, so busier systems are also drawn larger. */
  sizes?: number[]
}

export interface MapLegendItem {
  label: string
  colour: string
}

/** A system in this group is not drawn (its gates still are). */
export const HIDDEN = 255

export type MapFilterId = 'security' | 'npc' | 'npc-rising' | 'npc-falling' | 'ship' | 'jumps'

/** What the filters may need besides the map. */
export interface MapFilterContext {
  /** The latest hour of activity; null while loading or when the server has none. */
  activity: ActivityCurrent | null
  /** For NPC rising and falling: systems with fewer NPC kills in the last hour stay dim. */
  minNpc: number
}

export interface MapFilterDefinition {
  id: MapFilterId
  label: string
  /** Needs the server's activity data (the discovery document lists the `activity` feature). */
  activity: boolean
  create: (model: MapModel, context: MapFilterContext) => MapFilter
}

export const mapFilters: MapFilterDefinition[] = [
  { id: 'security', label: 'Security', activity: false, create: (model) => securityFilter(model) },
  { id: 'npc', label: 'NPC kills', activity: true, create: (model, c) => countFilter('npc', 'NPC kills', model, c, (a) => a.npcKills, 'npcKills') },
  { id: 'npc-rising', label: 'NPC rising', activity: true, create: (model, c) => changeFilter('npc-rising', 'NPC rising', model, c, 1) },
  { id: 'npc-falling', label: 'NPC falling', activity: true, create: (model, c) => changeFilter('npc-falling', 'NPC falling', model, c, -1) },
  { id: 'ship', label: 'Ship kills', activity: true, create: (model, c) => countFilter('ship', 'Ship kills', model, c, (a) => a.shipKills, 'shipKills') },
  { id: 'jumps', label: 'Jumps', activity: true, create: (model, c) => countFilter('jumps', 'Jumps', model, c, (a) => a.shipJumps, 'jumps') },
]

/** The minimum NPC kills in the last hour offered for the rising and falling filters. */
export const minNpcChoices = [0, 25, 100, 250]

export function createFilter(id: MapFilterId, model: MapModel, context: MapFilterContext): MapFilter {
  return (mapFilters.find((f) => f.id === id) ?? mapFilters[0]).create(model, context)
}

/** Colours systems by security status as EVE shows it, from 1.0 down to null security. */
export function securityFilter(model: MapModel): MapFilter {
  const groups = new Uint8Array(model.count)
  for (let i = 0; i < model.count; i++) groups[i] = securityBucket(model.security[i])

  const colours = Array.from({ length: BUCKET_COUNT }, (_, bucket) => securityBucketColor(bucket))
  const legend: MapLegendItem[] = []
  for (let bucket = 10; bucket >= 0; bucket--) legend.push({ label: (bucket / 10).toFixed(1), colour: colours[bucket] })
  legend.push({ label: 'Null', colour: colours[NULL_BUCKET] })

  return { id: 'security', label: 'Security', groups, colours, legend, notice: null }
}

/** Group 0 is quiet (below the first band), drawn faint so any activity stands out; groups 1–5 are the heat bands. */
const HEAT_COLOURS = ['var(--line)', 'var(--heat-1)', 'var(--heat-2)', 'var(--heat-3)', 'var(--heat-4)', 'var(--heat-5)']
/** The heat groups' sizes: quiet systems smaller, the busiest twice the normal size (docs/design.md: activity also reads as size). */
export const HEAT_SIZES = [0.8, 1, 1.2, 1.45, 1.7, 2]

/** The heat group of a value: 0 below the first bound, else the band it reaches (1–5). */
export function heatGroup(value: number, bands: number[]): number {
  let group = 0
  for (let b = 0; b < bands.length; b++) if (value >= bands[b]) group = b + 1
  return group
}

function heatLegend(bands: number[], format: (bound: number) => string): MapLegendItem[] {
  return [{ label: '0', colour: HEAT_COLOURS[0] }, ...bands.map((bound, b) => ({ label: format(bound), colour: HEAT_COLOURS[b + 1] }))]
}

/** Every system dim, with a reason: the activity is not there (yet). */
function unavailable(id: MapFilterId, label: string, model: MapModel, notice: string): MapFilter {
  return { id, label, groups: new Uint8Array(model.count), colours: HEAT_COLOURS, legend: [], notice }
}

/** The activity columns for this map, or a reason there are none. */
function columnsFor(model: MapModel, activity: ActivityCurrent | null): ActivityCurrent['systems'] | string {
  if (!activity) return 'No activity data from the server yet.'
  // The columns line up with the map pack they were built for.
  if (activity.sdeBuild !== model.sdeBuild || activity.systems.npcKills.lastHour.length !== model.count) return 'The activity is for another map version; it updates shortly.'
  return activity.systems
}

/** The activity columns for this map's systems, or null when there are none that line up with it. */
export function activityFor(model: MapModel, activity: ActivityCurrent | null): ActivityCurrent['systems'] | null {
  const systems = columnsFor(model, activity)
  return typeof systems === 'string' ? null : systems
}

/** A count in the last hour, in heat bands. */
export function countFilter(
  id: MapFilterId,
  label: string,
  model: MapModel,
  context: MapFilterContext,
  metric: (a: ActivityCurrent['systems']) => ActivityColumns,
  bandsOf: HeatMetric,
): MapFilter {
  const systems = columnsFor(model, context.activity)
  if (typeof systems === 'string') return unavailable(id, label, model, systems)

  const values = metric(systems).lastHour
  const bands = heatBands(bandsOf)
  const groups = new Uint8Array(model.count)
  for (let i = 0; i < model.count; i++) groups[i] = heatGroup(values[i], bands)
  return { id, label, groups, colours: HEAT_COLOURS, legend: heatLegend(bands, (b) => `${b}+`), notice: null, sizes: HEAT_SIZES }
}

/**
 * NPC kills rising (direction 1) or falling (-1) from the hour before, in heat bands of the change.
 * Systems with fewer than `minNpc` NPC kills in the last hour stay dim, so small changes in quiet
 * systems don't light up.
 */
export function changeFilter(id: MapFilterId, label: string, model: MapModel, context: MapFilterContext, direction: 1 | -1): MapFilter {
  const systems = columnsFor(model, context.activity)
  if (typeof systems === 'string') return unavailable(id, label, model, systems)
  const { lastHour, previousHour } = systems.npcKills
  if (!previousHour) return unavailable(id, label, model, 'The hour before is missing (downtime or a gap), so there is no change to show.')

  const bands = heatBands('npcChange')
  const groups = new Uint8Array(model.count)
  for (let i = 0; i < model.count; i++) {
    if (lastHour[i] < context.minNpc) continue
    groups[i] = heatGroup((lastHour[i] - previousHour[i]) * direction, bands)
  }
  const sign = direction > 0 ? '+' : '−'
  return { id, label, groups, colours: HEAT_COLOURS, legend: heatLegend(bands, (b) => `${sign}${b}`), notice: null, sizes: HEAT_SIZES }
}

/** The `?colour=` search parameter: a filter id other than the default, or undefined. */
export function parseColourParam(value: unknown): MapFilterId | undefined {
  return mapFilters.some((f) => f.id === value) && value !== mapFilters[0].id ? (value as MapFilterId) : undefined
}

/** The `?min=` search parameter: one of {@link minNpcChoices} other than 0, or undefined. */
export function parseMinNpcParam(value: unknown): number | undefined {
  const n = typeof value === 'number' ? value : typeof value === 'string' ? Number(value) : NaN
  return minNpcChoices.includes(n) && n > 0 ? n : undefined
}
