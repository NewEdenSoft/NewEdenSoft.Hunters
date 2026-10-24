import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { accountQuery } from '../../api/account'
import { setDestination } from '../../tracking/esi'
import { useTracking } from '../../tracking/trackingContext'

export interface DestinationCharacter {
  id: number
  name: string
  isMain: boolean
  online: boolean
}

export interface Destination {
  /** The tracked characters that can be used (no new login needed). */
  tracked: DestinationCharacter[]
  /** Every usable character of the account, online first, then the main. */
  usable: DestinationCharacter[]
  busy: boolean
  /** One line per outcome: who it was set for, then each failure. */
  status: { tone: 'ok' | 'danger' | 'muted'; lines: string[] } | null
  setFor: (characters: DestinationCharacter[]) => Promise<void>
}

/** Sets characters' autopilot destination to the system through ESI, with the tracker's tokens. */
export function useDestination(systemId: number, name: string): Destination {
  const tracking = useTracking()
  const account = useQuery(accountQuery)
  const [busy, setBusy] = useState(false)
  const [status, setStatus] = useState<Destination['status']>(null)

  const usable = (account.data?.characters ?? [])
    .filter((c) => c.status === 'Active')
    .map((c) => ({ id: c.id, name: c.name, isMain: c.isMain, online: tracking?.tracker.get(c.id)?.isOnline === true }))
    .sort((a, b) => Number(b.online) - Number(a.online) || Number(b.isMain) - Number(a.isMain) || a.name.localeCompare(b.name))
  const trackedIds = new Set(tracking?.tracker.characters.filter((c) => c.status !== 'needsLogin').map((c) => c.characterId))
  const tracked = usable.filter((c) => trackedIds.has(c.id))

  /** One character: renews a rejected token once. Returns null when set, else why not. */
  const setOne = async (characterId: number): Promise<string | null> => {
    if (!tracking) return 'Not signed in.'
    try {
      let result = await setDestination(systemId, await tracking.tokens.getAccessToken(characterId))
      if (result.status === 'unauthorized') {
        tracking.tokens.invalidate(characterId)
        result = await setDestination(systemId, await tracking.tokens.getAccessToken(characterId))
      }
      if (result.status === 'ok') return null
      return result.status === 'forbidden' ? 'not allowed to set waypoints; log in with it again' : result.error
    } catch (error) {
      return (error as Error).message
    }
  }

  const setFor = async (characters: DestinationCharacter[]) => {
    if (characters.length === 0) return
    setBusy(true)
    setStatus({ tone: 'muted', lines: [`Setting the destination for ${characters.map((c) => c.name).join(', ')}…`] })
    const results = await Promise.all(characters.map(async (c) => ({ c, error: await setOne(c.id) })))
    const done = results.filter((r) => r.error === null).map((r) => r.c.name)
    const failed = results.filter((r) => r.error !== null)
    setBusy(false)
    if (failed.length === 0) {
      setStatus({ tone: 'ok', lines: [`Destination ${name} set for ${done.join(', ')}.`] })
      // A success needs no keeping: it clears after a few seconds.
      setTimeout(() => setStatus((current) => (current?.tone === 'ok' ? null : current)), 4000)
    } else {
      setStatus({
        tone: 'danger',
        lines: [...(done.length > 0 ? [`Set for ${done.join(', ')}.`] : []), ...failed.map((r) => `${r.c.name}: ${r.error}`)],
      })
    }
  }

  return { tracked, usable, busy, status, setFor }
}
