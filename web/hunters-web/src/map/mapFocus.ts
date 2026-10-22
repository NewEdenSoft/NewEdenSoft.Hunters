import { useSyncExternalStore } from 'react'

/** Which character the map is on: focused once, or followed as it moves. */
export interface MapFocusState {
  /** The character last focused, shown on the map with a follow button; null when dismissed. */
  focused: number | null
  /** The character the map follows, or null. */
  followed: number | null
}

// Mirrors the desktop's MapFocus: shared by the Characters panel and the markers on the map.
let state: MapFocusState = { focused: null, followed: null }
const listeners = new Set<() => void>()
const requestListeners = new Set<(characterId: number) => void>()

function set(next: MapFocusState): void {
  if (next.focused === state.focused && next.followed === state.followed) return
  state = next
  listeners.forEach((listener) => listener())
}

/** Moves the map to the character. Focusing another character stops following. */
export function focusCharacter(characterId: number): void {
  set({ focused: characterId, followed: state.followed === characterId ? characterId : null })
  requestListeners.forEach((listener) => listener(characterId))
}

/** Moves the map to the character and keeps it there as the character moves. */
export function followCharacter(characterId: number): void {
  set({ ...state, followed: characterId })
  focusCharacter(characterId)
}

export function toggleFollow(characterId: number): void {
  if (state.followed === characterId) stopFollowing()
  else followCharacter(characterId)
}

/** The user dragged the map, taking over. */
export function stopFollowing(): void {
  set({ ...state, followed: null })
}

/** Forgets the focus and stops following (dismissed, or logged out). */
export function clearFocus(): void {
  set({ focused: null, followed: null })
}

export function getMapFocus(): MapFocusState {
  return state
}

/** Calls the listener each time the map should move to a character. */
export function onFocusRequested(listener: (characterId: number) => void): () => void {
  requestListeners.add(listener)
  return () => requestListeners.delete(listener)
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function useMapFocus(): MapFocusState {
  return useSyncExternalStore(subscribe, getMapFocus)
}
