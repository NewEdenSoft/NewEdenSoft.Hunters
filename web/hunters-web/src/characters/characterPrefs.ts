import { useSyncExternalStore } from 'react'
import { defaultMarker, isValidMarker, type CharacterMarker } from './markers'

/** The user's choices for one character in this browser. */
interface CharacterPrefs {
  marker?: CharacterMarker
  /** Poll the character's location while the app is open; true unless switched off. */
  tracked?: boolean
}

// Per-browser choices, kept like the theme and map layout; not shared with the server or other devices.
const STORAGE_KEY = 'hunters.characters'

const listeners = new Set<() => void>()
let prefs = read()
/** Bumped on every change, for components that read several characters. */
let version = 0

function read(): Record<string, CharacterPrefs> {
  try {
    const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}') as unknown
    return typeof saved === 'object' && saved !== null ? (saved as Record<string, CharacterPrefs>) : {}
  } catch {
    return {}
  }
}

function update(characterId: number, change: CharacterPrefs): void {
  prefs = { ...prefs, [characterId]: { ...prefs[characterId], ...change } }
  version++
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(prefs))
  } catch {
    // Storage unavailable (private mode); the choice still applies for this session.
  }
  listeners.forEach((listener) => listener())
}

/** The chosen marker, or the default for the name when none was chosen (or the saved one is not valid). */
export function getMarker(characterId: number, characterName: string): CharacterMarker {
  const marker = prefs[characterId]?.marker
  return isValidMarker(marker) ? marker : defaultMarker(characterName)
}

export function setMarker(characterId: number, marker: CharacterMarker): void {
  const { kind, value } = marker
  if (!isValidMarker(marker)) throw new Error(`'${value}' is not a ${kind} marker.`)
  update(characterId, { marker })
}

export function isTracked(characterId: number): boolean {
  return prefs[characterId]?.tracked !== false
}

export function setTracked(characterId: number, tracked: boolean): void {
  if (isTracked(characterId) !== tracked) update(characterId, { tracked })
}

export function subscribeCharacterPrefs(listener: () => void): () => void {
  listeners.add(listener)
  // Another tab changed them.
  const onStorage = (e: StorageEvent) => {
    if (e.key === STORAGE_KEY) {
      prefs = read()
      version++
      listener()
    }
  }
  window.addEventListener('storage', onStorage)
  return () => {
    listeners.delete(listener)
    window.removeEventListener('storage', onStorage)
  }
}

/** Re-renders when any character's choices change; read them with {@link getMarker} and {@link isTracked}. */
export function useCharacterPrefs(): number {
  return useSyncExternalStore(subscribeCharacterPrefs, () => version)
}

/** For tests: forget everything and read storage again. */
export function reloadCharacterPrefs(): void {
  prefs = read()
  version++
}
