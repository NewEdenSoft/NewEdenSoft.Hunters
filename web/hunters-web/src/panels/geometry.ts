/** A panel's place in the panel layer, in CSS pixels from its top left corner. */
export interface Rect {
  x: number
  y: number
  width: number
  height: number
}

export interface Size {
  width: number
  height: number
}

/** How close (CSS pixels) an edge must come to another edge to snap to it. */
export const SNAP_DISTANCE = 12

/** The space panels keep from the edges of the layer when snapped to them. */
export const EDGE_MARGIN = 12

/** The space between two panels snapped side by side. */
export const PANEL_GAP = 8

/**
 * Keeps a panel inside the area. A panel larger than the area is shrunk to fit; otherwise it is
 * moved back in, so a window made smaller never strands a panel out of reach.
 */
export function clampRect(rect: Rect, area: Size): Rect {
  const width = Math.min(rect.width, area.width)
  const height = Math.min(rect.height, area.height)
  return {
    x: clamp(rect.x, 0, area.width - width),
    y: clamp(rect.y, 0, area.height - height),
    width,
    height,
  }
}

/**
 * Snaps a moved panel to the area's edges (with {@link EDGE_MARGIN}) and to the edges of other
 * panels it is beside (aligned, or {@link PANEL_GAP} apart), when within {@link SNAP_DISTANCE}.
 * Each axis snaps on its own, to the nearest candidate.
 */
export function snapMove(rect: Rect, others: readonly Rect[], area: Size): Rect {
  const xs = [EDGE_MARGIN, area.width - EDGE_MARGIN - rect.width]
  const ys = [EDGE_MARGIN, area.height - EDGE_MARGIN - rect.height]
  for (const o of others) {
    if (overlaps(rect.y, rect.height, o.y, o.height)) {
      xs.push(o.x, o.x + o.width - rect.width, o.x + o.width + PANEL_GAP, o.x - PANEL_GAP - rect.width)
    }
    if (overlaps(rect.x, rect.width, o.x, o.width)) {
      ys.push(o.y, o.y + o.height - rect.height, o.y + o.height + PANEL_GAP, o.y - PANEL_GAP - rect.height)
    }
  }
  return { ...rect, x: nearest(rect.x, xs), y: nearest(rect.y, ys) }
}

/**
 * Snaps a resized panel's right and bottom edges to the area's edges and to other panels' edges.
 * It stays inside the area but never below the minimum size; near the corner the minimum wins and
 * {@link clampRect} moves the panel back in.
 */
export function snapResize(rect: Rect, others: readonly Rect[], area: Size, min: Size): Rect {
  const rights = [area.width - EDGE_MARGIN]
  const bottoms = [area.height - EDGE_MARGIN]
  for (const o of others) {
    if (overlaps(rect.y, rect.height, o.y, o.height)) rights.push(o.x + o.width, o.x - PANEL_GAP)
    if (overlaps(rect.x, rect.width, o.x, o.width)) bottoms.push(o.y + o.height, o.y - PANEL_GAP)
  }
  const right = nearest(rect.x + rect.width, rights)
  const bottom = nearest(rect.y + rect.height, bottoms)
  return {
    ...rect,
    width: clamp(right - rect.x, min.width, area.width - rect.x),
    height: clamp(bottom - rect.y, min.height, area.height - rect.y),
  }
}

/** Whether two spans come within snapping distance of each other. */
function overlaps(start: number, length: number, otherStart: number, otherLength: number): boolean {
  return start < otherStart + otherLength + SNAP_DISTANCE && otherStart < start + length + SNAP_DISTANCE
}

function nearest(value: number, candidates: readonly number[]): number {
  let best = value
  let bestDistance = SNAP_DISTANCE + 1
  for (const candidate of candidates) {
    const distance = Math.abs(candidate - value)
    if (distance <= SNAP_DISTANCE && distance < bestDistance) {
      best = candidate
      bestDistance = distance
    }
  }
  return best
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), Math.max(min, max))
}
