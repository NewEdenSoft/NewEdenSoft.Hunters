import { useSuspenseQuery } from '@tanstack/react-query'
import { useEffect, useRef } from 'react'
import { accountQuery } from '../api/account'
import { getMapBehaviour } from '../settings/mapSettings'
import { useTracking } from '../tracking/trackingContext'
import { systemChanged } from '../tracking/types'
import { autoFocusReason, type AutoFocusReason } from './autoFocus'
import { getMapFocus } from './mapFocus'
import type { MapModel } from './model'

/**
 * Focuses a system by itself when the settings allow it (Settings, "Map behaviour"): when the followed
 * character, or the main while nobody is followed, comes into known space from unknown space, or moves
 * at least the minimum jumps in one go. `onFocus` gets the system's index and why.
 */
export function useAutoFocus(model: MapModel, onFocus: (systemIndex: number, reason: AutoFocusReason) => void): void {
  const { data: account } = useSuspenseQuery(accountQuery)
  const tracking = useTracking()
  const latest = useRef(onFocus)
  useEffect(() => {
    latest.current = onFocus
  })
  const mainId = account.characters.find((c) => c.isMain)?.id ?? null

  useEffect(() => {
    if (!tracking) return
    return tracking.tracker.subscribe((change) => {
      const source = getMapFocus().followed ?? mainId
      const to = change.current?.location?.solarSystemId
      if (change.characterId !== source || to === undefined || !systemChanged(change)) return
      const reason = autoFocusReason(model, change.previous?.location?.solarSystemId ?? null, to, getMapBehaviour())
      const index = model.indexOf.get(to)
      if (reason && index !== undefined) latest.current(index, reason)
    })
  }, [tracking, model, mainId])
}
