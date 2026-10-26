import type { MapModel } from './model'

const neighbours = new WeakMap<MapModel, number[][]>()

/** Each system's gate neighbours (by index), built once per model. */
export function neighboursOf(model: MapModel): number[][] {
  let list = neighbours.get(model)
  if (!list) {
    list = Array.from({ length: model.count }, () => [])
    for (let g = 0; g < model.gates.length; g += 2) {
      list[model.gates[g]].push(model.gates[g + 1])
      list[model.gates[g + 1]].push(model.gates[g])
    }
    neighbours.set(model, list)
  }
  return list
}

/**
 * The shortest gate route from `from` to `to` (system indexes, both ends included; a breadth-first
 * search), or null when no gates lead there. Shortest by jumps, like the server's reach, not safest.
 */
export function gateRoute(model: MapModel, from: number, to: number): number[] | null {
  if (from === to) return [from]
  const adjacent = neighboursOf(model)
  const previous = new Int32Array(model.count).fill(-1)
  previous[from] = from
  let frontier = [from]
  while (frontier.length > 0) {
    const next: number[] = []
    for (const system of frontier) {
      for (const n of adjacent[system]) {
        if (previous[n] >= 0) continue
        previous[n] = system
        if (n === to) {
          const route = [to]
          for (let at = to; at !== from; at = previous[at]) route.push(previous[at])
          return route.reverse()
        }
        next.push(n)
      }
    }
    frontier = next
  }
  return null
}
