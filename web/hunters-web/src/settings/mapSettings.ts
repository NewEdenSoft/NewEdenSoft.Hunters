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

// The hunting profiles shown on the map, in the order they were turned on.
const HUNTING_KEY = 'hunters.map.hunting'
const huntingListeners = new Set<() => void>()

function readHunting(): string[] {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(HUNTING_KEY) ?? '[]')
    return Array.isArray(value) ? value.filter((v): v is string => typeof v === 'string') : []
  } catch {
    return []
  }
}

let hunting = readHunting()

function setHunting(next: string[]): void {
  hunting = next
  try {
    localStorage.setItem(HUNTING_KEY, JSON.stringify(next))
  } catch {
    // Storage unavailable; the choice still applies for this session.
  }
  huntingListeners.forEach((listener) => listener())
}

function subscribeHunting(listener: () => void): () => void {
  huntingListeners.add(listener)
  const onStorage = (e: StorageEvent) => {
    if (e.key === HUNTING_KEY) {
      hunting = readHunting()
      listener()
    }
  }
  window.addEventListener('storage', onStorage)
  return () => {
    huntingListeners.delete(listener)
    window.removeEventListener('storage', onStorage)
  }
}

/** The hunting profiles shown on the map (glow and badges), and a toggle for one of them. */
export function useShownHuntingProfiles(): [string[], (profile: string) => void] {
  const current = useSyncExternalStore(subscribeHunting, () => hunting)
  const toggle = useCallback((profile: string) => {
    setHunting(hunting.includes(profile) ? hunting.filter((p) => p !== profile) : [...hunting, profile])
  }, [])
  return [current, toggle]
}
