import type { MapPack } from '../api/map'

/** Which 2D layout to draw: the 3D map seen from above, or CCP's flattened 2D map. */
export type MapLayout = 'topDown' | 'flat'

export interface Bounds {
  minX: number
  minY: number
  maxX: number
  maxY: number
}

/**
 * The map pack as flat typed arrays, indexed by system index (0..count-1). Built once per pack;
 * the renderer and hit-testing never touch per-system objects.
 */
export interface MapModel {
  sdeBuild: number
  count: number
  ids: Int32Array
  names: string[]
  regionIds: Int32Array
  security: Float32Array
  /** Interleaved x, y in light years, per layout. */
  positions: Record<MapLayout, Float32Array>
  bounds: Record<MapLayout, Bounds>
  /** Gate endpoints as pairs of system indexes. */
  gates: Uint32Array
  regionNames: Map<number, string>
  /** Regions for labels, largest (most systems) first. */
  regions: MapRegionLabel[]
  indexOf: Map<number, number>
}

export interface MapRegionLabel {
  id: number
  name: string
  /**
   * Where to put the region's name per layout: the mean position of its systems, or null when the
   * region is too scattered for one label to mean anything (Pochven in the top-down layout).
   */
  centre: Record<MapLayout, { x: number; y: number } | null>
}

/** A region is labelled only if its systems spread at most this many times the median region's spread. */
const MAX_REGION_SPREAD = 2

export function buildMapModel(pack: MapPack): MapModel {
  const count = pack.systems.length
  const ids = new Int32Array(count)
  const regionIds = new Int32Array(count)
  const security = new Float32Array(count)
  const topDown = new Float32Array(count * 2)
  const flat = new Float32Array(count * 2)
  const names: string[] = new Array(count)
  const indexOf = new Map<number, number>()

  pack.systems.forEach((s, i) => {
    ids[i] = s.id
    names[i] = s.name
    regionIds[i] = s.regionId
    security[i] = s.security
    topDown[i * 2] = s.topDown.x
    topDown[i * 2 + 1] = s.topDown.y
    flat[i * 2] = s.flat.x
    flat[i * 2 + 1] = s.flat.y
    indexOf.set(s.id, i)
  })

  const gates = new Uint32Array(pack.gates.length * 2)
  let g = 0
  for (const gate of pack.gates) {
    const from = indexOf.get(gate.from)
    const to = indexOf.get(gate.to)
    if (from === undefined || to === undefined) continue
    gates[g++] = from
    gates[g++] = to
  }

  return {
    sdeBuild: pack.sdeBuild,
    count,
    ids,
    names,
    regionIds,
    security,
    positions: { topDown, flat },
    bounds: { topDown: boundsOf(topDown), flat: boundsOf(flat) },
    gates: gates.subarray(0, g),
    regionNames: new Map(pack.regions.map((r) => [r.id, r.name])),
    regions: regionLabels(pack, regionIds, { topDown, flat }),
    indexOf,
  }
}

function regionLabels(pack: MapPack, regionIds: Int32Array, positions: Record<MapLayout, Float32Array>): MapRegionLabel[] {
  const members = new Map<number, number[]>()
  regionIds.forEach((regionId, i) => {
    const list = members.get(regionId)
    if (list) list.push(i)
    else members.set(regionId, [i])
  })

  const regions = pack.regions.filter((r) => members.has(r.id))
  const centres = { topDown: labelCentres(regions, members, positions.topDown), flat: labelCentres(regions, members, positions.flat) }

  return regions
    .map((r, i) => ({ id: r.id, name: r.name, centre: { topDown: centres.topDown[i], flat: centres.flat[i] } }))
    .sort((a, b) => members.get(b.id)!.length - members.get(a.id)!.length)
}

/** Mean position of each region's systems, or null for regions spread much wider than the median region. */
function labelCentres(regions: { id: number }[], members: Map<number, number[]>, xy: Float32Array) {
  const stats = regions.map((r) => {
    const systems = members.get(r.id)!
    let x = 0
    let y = 0
    for (const i of systems) {
      x += xy[i * 2]
      y += xy[i * 2 + 1]
    }
    x /= systems.length
    y /= systems.length
    let squares = 0
    for (const i of systems) squares += (xy[i * 2] - x) ** 2 + (xy[i * 2 + 1] - y) ** 2
    return { x, y, spread: Math.sqrt(squares / systems.length) }
  })

  const spreads = stats.map((s) => s.spread).sort((a, b) => a - b)
  const median = spreads[Math.floor(spreads.length / 2)] ?? 0
  return stats.map((s) => (s.spread <= median * MAX_REGION_SPREAD ? { x: s.x, y: s.y } : null))
}

export function boundsOf(xy: Float32Array): Bounds {
  const b: Bounds = { minX: Infinity, minY: Infinity, maxX: -Infinity, maxY: -Infinity }
  for (let i = 0; i < xy.length; i += 2) {
    b.minX = Math.min(b.minX, xy[i])
    b.maxX = Math.max(b.maxX, xy[i])
    b.minY = Math.min(b.minY, xy[i + 1])
    b.maxY = Math.max(b.maxY, xy[i + 1])
  }
  return b
}
