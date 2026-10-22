import { useSyncExternalStore } from 'react'

/** Below Tailwind's `sm` breakpoint (40rem): panels become a bottom sheet and the rail a bottom bar. */
const PHONE_QUERY = '(max-width: 39.99rem)'

function subscribe(onChange: () => void): () => void {
  const query = window.matchMedia(PHONE_QUERY)
  query.addEventListener('change', onChange)
  return () => query.removeEventListener('change', onChange)
}

/** True on phone-sized screens; follows rotation and window resizing. */
export function useIsPhone(): boolean {
  return useSyncExternalStore(subscribe, () => window.matchMedia(PHONE_QUERY).matches)
}
