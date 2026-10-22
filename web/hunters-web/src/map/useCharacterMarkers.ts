import { useSuspenseQuery } from '@tanstack/react-query'
import { useEffect, useMemo, useState, useSyncExternalStore } from 'react'
import { accountQuery } from '../api/account'
import { getMarker, useCharacterPrefs } from '../characters/characterPrefs'
import { useTracking } from '../tracking/trackingContext'
import { systemChanged } from '../tracking/types'
import { getMapFocus, onFocusRequested } from './mapFocus'
import type { MapMarkerGroup } from './MapRenderer'
import type { MapFocusTarget } from './MapView'
import type { MapModel } from './model'

const noop = () => () => {}

/** Re-renders on every tracker change; returns a number that changes with it. */
function useTrackerVersion(): number {
  const tracking = useTracking()
  const [subscribe, getVersion] = useMemo(() => {
    let version = 0
    return [
      (onChange: () => void) =>
        tracking?.tracker.subscribe(() => {
          version++
          onChange()
        }) ?? noop(),
      () => version,
    ] as const
  }, [tracking])
  return useSyncExternalStore(tracking ? subscribe : noop, getVersion)
}

/**
 * The account's tracked characters with a known location, grouped by system for the map, main
 * first. They follow the tracker and the markers chosen on the Account page.
 */
export function useCharacterMarkers(model: MapModel): MapMarkerGroup[] {
  const { data: account } = useSuspenseQuery(accountQuery)
  const tracking = useTracking()
  const trackerVersion = useTrackerVersion()
  const prefsVersion = useCharacterPrefs()

  return useMemo(() => {
    void trackerVersion
    void prefsVersion
    const groups = new Map<number, MapMarkerGroup>()
    const characters = [...account.characters].sort((a, b) => Number(b.isMain) - Number(a.isMain) || a.name.localeCompare(b.name))
    for (const c of characters) {
      const state = tracking?.tracker.get(c.id)
      const index = state?.location ? model.indexOf.get(state.location.solarSystemId) : undefined
      if (!state || index === undefined) continue
      let group = groups.get(index)
      if (!group) groups.set(index, (group = { systemIndex: index, markers: [] }))
      group.markers.push({ characterId: c.id, name: c.name, marker: getMarker(c.id, c.name), online: state.isOnline === true })
    }
    return [...groups.values()]
  }, [account, tracking, model, trackerVersion, prefsVersion])
}

/**
 * Where the map should go: the focused character's system when focus is asked for (as soon as its
 * location is known), and the followed character's system each time it changes system.
 */
export function useMapFocusTarget(model: MapModel): MapFocusTarget | null {
  const tracking = useTracking()
  const [target, setTarget] = useState<MapFocusTarget | null>(null)

  useEffect(() => {
    if (!tracking) return
    // A character focused before its location was known; the map moves there once it is.
    let awaiting: number | null = null
    const go = (solarSystemId: number, zoomIn: boolean) => {
      const systemIndex = model.indexOf.get(solarSystemId)
      if (systemIndex !== undefined) setTarget({ systemIndex, zoomIn })
    }

    const offRequest = onFocusRequested((id) => {
      const location = tracking.tracker.get(id)?.location
      awaiting = location ? null : id
      if (location) go(location.solarSystemId, true)
    })
    const offTracker = tracking.tracker.subscribe((change) => {
      const location = change.current?.location
      if (!location) return
      if (change.characterId === awaiting) {
        awaiting = null
        go(location.solarSystemId, true)
      } else if (change.characterId === getMapFocus().followed && systemChanged(change)) {
        // Following keeps the user's zoom.
        go(location.solarSystemId, false)
      }
    })
    return () => {
      offRequest()
      offTracker()
    }
  }, [tracking, model])

  return target
}
