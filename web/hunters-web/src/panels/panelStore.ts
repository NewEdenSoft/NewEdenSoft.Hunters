import { useCallback, useSyncExternalStore } from 'react'
import { panelDefinitions, type PanelId } from './definitions'
import { isOnTop, restack } from './stacking'

/** One panel's layout. Position and size are in CSS pixels from the top left of the map. */
export interface PanelState {
  x: number
  y: number
  width: number
  /** Set once the user resizes the panel; until then it fits its content. */
  height?: number
  open: boolean
  collapsed: boolean
  /** Stacking order; the panel clicked last is on top. */
  z: number
}

// Panel layout is a per-browser convenience, kept like the map layout and theme.
const STORAGE_KEY = 'hunters.panels'

type Layout = Record<PanelId, PanelState>

const listeners = new Set<() => void>()
let layout = read()

function defaults(): Layout {
  return Object.fromEntries(
    panelDefinitions.map((d, i) => [
      d.id,
      { x: d.initial.x, y: d.initial.y, width: d.initial.width, open: d.openByDefault ?? true, collapsed: false, z: i + 1 },
    ]),
  ) as Layout
}

/** The saved layout, with defaults for panels it does not know and anything unreadable. */
function read(): Layout {
  const result = defaults()
  try {
    const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}') as Record<string, Partial<PanelState>>
    for (const id of Object.keys(result) as PanelId[]) {
      const s = saved[id]
      if (!s || !isFinite(s.x) || !isFinite(s.y) || !isFinite(s.width)) continue
      result[id] = {
        x: s.x!,
        y: s.y!,
        width: s.width!,
        height: isFinite(s.height) ? s.height : undefined,
        open: s.open !== false,
        collapsed: s.collapsed === true,
        z: isFinite(s.z) ? s.z! : result[id].z,
      }
    }
  } catch {
    // Unreadable or unavailable storage: defaults.
  }
  // Older layouts counted up with every click; from 1 again, keeping the order.
  const zs = restack(Object.fromEntries(Object.entries(result).map(([id, s]) => [id, s.z])))
  for (const id of Object.keys(result) as PanelId[]) result[id].z = zs[id]
  return result
}

function isFinite(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}

function save(): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(layout))
  } catch {
    // Storage unavailable (private mode); the layout still applies for this session.
  }
}

function notify(): void {
  listeners.forEach((listener) => listener())
}

/**
 * Changes a panel. While dragging, pass `persist: false` for every move and save once at the end,
 * so storage is not written on every pointer move.
 */
export function updatePanel(id: PanelId, change: Partial<PanelState>, { persist = true } = {}): void {
  layout = { ...layout, [id]: { ...layout[id], ...change } }
  if (persist) save()
  notify()
}

/** The search at the top of the map (with the character chip and the selection): a layer like the panels, not saved. */
export const SEARCH_LAYER = 'search'

// The search starts on top, as it was before any panel is clicked.
let searchZ = Object.keys(layout).length + 1

function stackOf(): Record<string, number> {
  return { ...Object.fromEntries(Object.entries(layout).map(([id, s]) => [id, s.z])), [SEARCH_LAYER]: searchZ }
}

/** Puts a panel, or the search, on top of everything else over the map: the one clicked or focused last. */
export function bringToFront(id: PanelId | typeof SEARCH_LAYER): void {
  const zs = stackOf()
  if (isOnTop(zs, id)) return
  const next = restack(zs, id)
  searchZ = next[SEARCH_LAYER]
  layout = Object.fromEntries(Object.entries(layout).map(([key, s]) => [key, s.z === next[key] ? s : { ...s, z: next[key] }])) as Layout
  save()
  notify()
}

/** The search's z-index, kept in step with the store. */
export function useSearchLayer(): number {
  return useSyncExternalStore(subscribe, () => searchZ)
}

/** Opens a closed panel (on top, expanded) or closes an open one. */
export function togglePanel(id: PanelId): void {
  if (layout[id].open) {
    updatePanel(id, { open: false })
  } else {
    updatePanel(id, { open: true, collapsed: false })
    bringToFront(id)
  }
}

export function getPanel(id: PanelId): PanelState {
  return layout[id]
}

/** Puts every panel back where it started. */
export function resetPanels(): void {
  layout = defaults()
  searchZ = Object.keys(layout).length + 1
  save()
  notify()
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  // Another tab changed it.
  const onStorage = (e: StorageEvent) => {
    if (e.key === STORAGE_KEY) {
      layout = read()
      listener()
    }
  }
  window.addEventListener('storage', onStorage)
  return () => {
    listeners.delete(listener)
    window.removeEventListener('storage', onStorage)
  }
}

/** One panel's layout, kept in step with the store. */
export function usePanel(id: PanelId): PanelState {
  return useSyncExternalStore(subscribe, useCallback(() => layout[id], [id]))
}
