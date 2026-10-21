import { useCallback, useSyncExternalStore } from 'react'
import type { MapLayout } from '../map/model'

// Map settings, kept in this browser like the theme (theme/themes.ts).
const LAYOUT_KEY = 'hunters.map.layout'
const DEFAULT_LAYOUT: MapLayout = 'flat'

export const mapLayouts: { id: MapLayout; label: string }[] = [
  { id: 'flat', label: '2D map' },
  { id: 'topDown', label: 'Top-down' },
]

const listeners = new Set<() => void>()

function readLayout(): MapLayout {
  try {
    const value = localStorage.getItem(LAYOUT_KEY)
    return mapLayouts.some((l) => l.id === value) ? (value as MapLayout) : DEFAULT_LAYOUT
  } catch {
    return DEFAULT_LAYOUT
  }
}

let layout = readLayout()

function setLayout(next: MapLayout): void {
  layout = next
  try {
    localStorage.setItem(LAYOUT_KEY, next)
  } catch {
    // Storage unavailable (private mode); the layout still applies for this session.
  }
  listeners.forEach((listener) => listener())
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  // Another tab changed it.
  const onStorage = (e: StorageEvent) => {
    if (e.key === LAYOUT_KEY) {
      layout = readLayout()
      listener()
    }
  }
  window.addEventListener('storage', onStorage)
  return () => {
    listeners.delete(listener)
    window.removeEventListener('storage', onStorage)
  }
}

/** The map layout the user chose, shared by the map page and the settings page. */
export function useMapLayout(): [MapLayout, (layout: MapLayout) => void] {
  const current = useSyncExternalStore(subscribe, () => layout)
  return [current, useCallback((next: MapLayout) => setLayout(next), [])]
}
