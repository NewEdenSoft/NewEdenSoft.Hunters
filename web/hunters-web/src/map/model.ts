import type { MapPack } from '../api/map'
import { securityBucket } from './security'

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
  /** Colour bucket per system, see {@link securityBucket}. */
  buckets: Uint8Array
  /** Interleaved x, y in light years, per layout. */
  positions: Record<MapLayout, Float32Array>
  bounds: Record<MapLayout, Bounds>
  /** Gate endpoints as pairs of system indexes. */
  gates: Uint32Array
  regionNames: Map<number, string>
  indexOf: Map<number, number>
}

export function buildMapModel(pack: MapPack): MapModel {
  const count = pack.systems.length
  const ids = new Int32Array(count)
  const regionIds = new Int32Array(count)
  const security = new Float32Array(count)
  const buckets = new Uint8Array(count)
  const topDown = new Float32Array(count * 2)
  const flat = new Float32Array(count * 2)
  const names: string[] = new Array(count)
  const indexOf = new Map<number, number>()

  pack.systems.forEach((s, i) => {
    ids[i] = s.id
    names[i] = s.name
    regionIds[i] = s.regionId
    security[i] = s.security
    buckets[i] = securityBucket(s.security)
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
    buckets,
    positions: { topDown, flat },
    bounds: { topDown: boundsOf(topDown), flat: boundsOf(flat) },
    gates: gates.subarray(0, g),
    regionNames: new Map(pack.regions.map((r) => [r.id, r.name])),
    indexOf,
  }
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
