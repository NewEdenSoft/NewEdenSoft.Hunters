import { useCallback, useSyncExternalStore } from 'react'
import type { MapLayout } from '../map/model'
import { defaultMapBehaviour, minJumpChoices, type MapBehaviour } from '../map/autoFocus'
import { MAX_JUMP_RANGE, MIN_JUMP_RANGE } from '../map/jumpRange'
import { defaultNearby, parseNearby, type NearbySetting } from '../map/nearby'

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

// How the focused system may change by itself, and the map's clicks (map/autoFocus.ts).
const BEHAVIOUR_KEY = 'hunters.map.behaviour'
const behaviourListeners = new Set<() => void>()

function readBehaviour(): MapBehaviour {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(BEHAVIOUR_KEY) ?? '{}')
    const saved = typeof value === 'object' && value !== null ? (value as Partial<Record<keyof MapBehaviour, unknown>>) : {}
    const flag = (key: 'enterKnownSpace' | 'longMove' | 'doubleClickFocus') =>
      typeof saved[key] === 'boolean' ? (saved[key] as boolean) : defaultMapBehaviour[key]
    return {
      enterKnownSpace: flag('enterKnownSpace'),
      longMove: flag('longMove'),
      doubleClickFocus: flag('doubleClickFocus'),
      minJumps: minJumpChoices.includes(saved.minJumps as number) ? (saved.minJumps as number) : defaultMapBehaviour.minJumps,
    }
  } catch {
    return defaultMapBehaviour
  }
}

let behaviour = readBehaviour()

function subscribeBehaviour(listener: () => void): () => void {
  behaviourListeners.add(listener)
  const onStorage = (e: StorageEvent) => {
    if (e.key === BEHAVIOUR_KEY) {
      behaviour = readBehaviour()
      listener()
    }
  }
  window.addEventListener('storage', onStorage)
  return () => {
    behaviourListeners.delete(listener)
    window.removeEventListener('storage', onStorage)
  }
}

/** The map's behaviour (auto-focus and double click), kept in this browser, and a setter for part of it. */
export function useMapBehaviour(): [MapBehaviour, (change: Partial<MapBehaviour>) => void] {
  const current = useSyncExternalStore(subscribeBehaviour, () => behaviour)
  const update = useCallback((change: Partial<MapBehaviour>) => {
    behaviour = { ...behaviour, ...change }
    try {
      localStorage.setItem(BEHAVIOUR_KEY, JSON.stringify(behaviour))
    } catch {
      // Storage unavailable; the choice still applies for this session.
    }
    behaviourListeners.forEach((listener) => listener())
  }, [])
  return [current, update]
}

/** The current behaviour, for code outside React (the auto-focus subscription). */
export function getMapBehaviour(): MapBehaviour {
  return behaviour
}

// What the Nearby panel looks for around the focused system (map/nearby.ts).
const NEARBY_KEY = 'hunters.nearby'
const nearbyListeners = new Set<() => void>()

function readNearby(): NearbySetting {
  try {
    return parseNearby(JSON.parse(localStorage.getItem(NEARBY_KEY) ?? 'null'))
  } catch {
    return defaultNearby
  }
}

let nearby = readNearby()

function subscribeNearby(listener: () => void): () => void {
  nearbyListeners.add(listener)
  const onStorage = (e: StorageEvent) => {
    if (e.key === NEARBY_KEY) {
      nearby = readNearby()
      listener()
    }
  }
  window.addEventListener('storage', onStorage)
  return () => {
    nearbyListeners.delete(listener)
    window.removeEventListener('storage', onStorage)
  }
}

/** The Nearby panel's limits and sort, kept in this browser, and a setter for part of them. */
export function useNearby(): [NearbySetting, (change: Partial<NearbySetting>) => void] {
  const current = useSyncExternalStore(subscribeNearby, () => nearby)
  const update = useCallback((change: Partial<NearbySetting>) => {
    nearby = { ...nearby, ...change }
    try {
      localStorage.setItem(NEARBY_KEY, JSON.stringify(nearby))
    } catch {
      // Storage unavailable; the choice still applies for this session.
    }
    nearbyListeners.forEach((listener) => listener())
  }, [])
  return [current, update]
}

// The jump range drawn around the focused system (map/jumpRange.ts).
const JUMP_RANGE_KEY = 'hunters.map.jumpRange'
const jumpRangeListeners = new Set<() => void>()

export interface JumpRangeSetting {
  /** Drawn on the map. */
  enabled: boolean
  /** The range in light years: a preset's, or the user's own. */
  ly: number
}

const defaultJumpRange: JumpRangeSetting = { enabled: false, ly: 8 }

function readJumpRange(): JumpRangeSetting {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(JUMP_RANGE_KEY) ?? '{}')
    const saved = typeof value === 'object' && value !== null ? (value as Partial<Record<keyof JumpRangeSetting, unknown>>) : {}
    const ly = typeof saved.ly === 'number' && saved.ly >= MIN_JUMP_RANGE && saved.ly <= MAX_JUMP_RANGE ? saved.ly : defaultJumpRange.ly
    return { enabled: saved.enabled === true, ly }
  } catch {
    return defaultJumpRange
  }
}

let jumpRange = readJumpRange()

function subscribeJumpRange(listener: () => void): () => void {
  jumpRangeListeners.add(listener)
  const onStorage = (e: StorageEvent) => {
    if (e.key === JUMP_RANGE_KEY) {
      jumpRange = readJumpRange()
      listener()
    }
  }
  window.addEventListener('storage', onStorage)
  return () => {
    jumpRangeListeners.delete(listener)
    window.removeEventListener('storage', onStorage)
  }
}

/** Whether the jump range is drawn and how far it reaches, kept in this browser, and a setter for part of it. */
export function useJumpRange(): [JumpRangeSetting, (change: Partial<JumpRangeSetting>) => void] {
  const current = useSyncExternalStore(subscribeJumpRange, () => jumpRange)
  const update = useCallback((change: Partial<JumpRangeSetting>) => {
    const next = { ...jumpRange, ...change }
    jumpRange = { ...next, ly: Math.min(MAX_JUMP_RANGE, Math.max(MIN_JUMP_RANGE, next.ly)) }
    try {
      localStorage.setItem(JUMP_RANGE_KEY, JSON.stringify(jumpRange))
    } catch {
      // Storage unavailable; the choice still applies for this session.
    }
    jumpRangeListeners.forEach((listener) => listener())
  }, [])
  return [current, update]
}
