import { useEffect, useState, type ReactNode } from 'react'
import type { AccountInfo } from '../api/account'
import { isTracked, useCharacterPrefs } from '../characters/characterPrefs'
import { fetchLocationEsi, fetchNames } from './esi'
import { EveNames } from './names'
import { ServerTokenProvider } from './tokens'
import { CharacterTracker } from './tracker'
import { TrackingContext, type Tracking } from './trackingContext'

/**
 * Runs the one character tracker of the signed-in session: it tracks the account's characters that
 * are usable (no new login needed) and switched on, follows changes to both, and polls less while the
 * tab is hidden. When the user logs out, this provider unmounts with the signed-in layout and stops
 * tracking; the tracker, its tokens and names go with it.
 */
export function TrackingProvider({ account, children }: { account: AccountInfo; children: ReactNode }) {
  const [tracking] = useState<Tracking>(() => ({
    tracker: new CharacterTracker(fetchLocationEsi, new ServerTokenProvider()),
    names: new EveNames(fetchNames),
  }))
  const prefsVersion = useCharacterPrefs()

  useEffect(() => {
    const onVisibility = () => tracking.tracker.setBackground(document.visibilityState === 'hidden')
    onVisibility()
    document.addEventListener('visibilitychange', onVisibility)
    return () => document.removeEventListener('visibilitychange', onVisibility)
  }, [tracking])

  // Stop everything when the signed-in pages go away. Stopping, not disposing: React's development
  // double mount runs the effects again, and the next one starts tracking again.
  useEffect(() => () => tracking.tracker.track([]), [tracking])

  // Follow the account and the Track switches. track() only starts and stops the characters that
  // changed, so the others keep their state.
  useEffect(() => {
    tracking.tracker.track(account.characters.filter((c) => c.status === 'Active' && isTracked(c.id)).map((c) => c.id))
  }, [tracking, account, prefsVersion])

  return <TrackingContext value={tracking}>{children}</TrackingContext>
}
