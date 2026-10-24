/**
 * The stacking order over the map: each layer (a panel, or the search at the top) has a z-index, and the
 * layer clicked or focused last is on top. The order is renumbered 1..n on every change, so z-indexes
 * stay small and the map's menus (z-index 1100) stay above every layer.
 */
export function restack(zs: Record<string, number>, front?: string): Record<string, number> {
  const keys = Object.keys(zs)
  // Lowest first; equal z-indexes keep their order.
  const order = keys.map((key, i) => ({ key, z: zs[key], i })).sort((a, b) => a.z - b.z || a.i - b.i).map((e) => e.key)
  if (front !== undefined && order.includes(front)) order.push(...order.splice(order.indexOf(front), 1))
  return Object.fromEntries(order.map((key, i) => [key, i + 1]))
}

/** Whether a layer is alone on top already. */
export function isOnTop(zs: Record<string, number>, key: string): boolean {
  return Object.entries(zs).every(([other, z]) => other === key || z < zs[key])
}
