import type { MapModel } from './model'

/** Lower case, letters and digits only: `1DQ1-A` and `1dq1a` match. */
export function normalizeSystemName(name: string): string {
  return name.toLowerCase().replace(/[^a-z0-9]/g, '')
}

const keys = new WeakMap<MapModel, string[]>()

/** The normalised names of a model's systems, built once per model. */
function keysOf(model: MapModel): string[] {
  let names = keys.get(model)
  if (!names) {
    names = model.names.map(normalizeSystemName)
    keys.set(model, names)
  }
  return names
}

/**
 * Systems whose name matches the query, as system indexes: the exact name first, then names that start
 * with it, then names that contain it; alphabetical within each. Case and punctuation are ignored.
 */
export function searchSystems(model: MapModel, query: string, limit = 8): number[] {
  const q = normalizeSystemName(query)
  if (q.length === 0) return []

  const names = keysOf(model)
  const ranked: { index: number; rank: number }[] = []
  for (let i = 0; i < names.length; i++) {
    const at = names[i].indexOf(q)
    if (at < 0) continue
    ranked.push({ index: i, rank: names[i].length === q.length ? 0 : at === 0 ? 1 : 2 })
  }

  ranked.sort((a, b) => a.rank - b.rank || model.names[a.index].localeCompare(model.names[b.index]))
  return ranked.slice(0, limit).map((r) => r.index)
}

/** The `?system=` search parameter: a positive integer system id, or undefined. */
export function parseSystemParam(value: unknown): number | undefined {
  const id = typeof value === 'number' ? value : typeof value === 'string' ? Number(value) : NaN
  return Number.isSafeInteger(id) && id > 0 ? id : undefined
}
