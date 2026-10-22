import { needsReauth, type CharacterInfo } from '../api/account'
import { useTrackedCharacter, useWhereabouts } from '../tracking/trackingContext'
import { isTracked, useCharacterPrefs } from './characterPrefs'

/** How a character is doing, as the Characters panel and the Account page show it. */
export interface CharacterStatus {
  tracked: boolean
  online: boolean
  /** Needs a login, or polling fails. */
  problem: boolean
  needsLogin: boolean
  /** "Jita", "Jita · Docked", or why there is no place to show. */
  place: string
  ship: string | null
  /** One line: "Online · Jita · Docked at Jita IV - Moon 4 - … · Ishtar", or why there is none. */
  line: string
  /** Everything, for a tooltip: the full station name and the last error too. */
  details: string
}

export function useCharacterStatus(character: CharacterInfo): CharacterStatus {
  useCharacterPrefs()
  const tracked = isTracked(character.id)
  const state = useTrackedCharacter(character.id)
  const where = useWhereabouts(state)

  const needsLogin = needsReauth(character) || state?.status === 'needsLogin'
  const failing = tracked && state?.status === 'failing'
  const online = !needsLogin && tracked && state?.isOnline === true
  const ship = tracked && !needsLogin ? (where?.ship ?? null) : null

  let place: string
  let line: string
  if (needsLogin) {
    place = line = 'Needs login'
  } else if (!tracked) {
    place = line = 'Not tracked'
  } else if (where) {
    place = where.docked ? `${where.system} · Docked` : where.system
    line = [online ? 'Online' : 'Offline', where.system, where.docked, where.ship, failing ? 'retrying' : null].filter(Boolean).join(' · ')
  } else {
    place = failing ? 'Location unavailable' : 'Locating…'
    line = failing ? 'Location unavailable, retrying' : 'Locating…'
  }

  const details = [...new Set([character.name, online ? 'Online' : null, place, where?.docked, ship, tracked ? state?.error : null])]
    .filter(Boolean)
    .join('\n')

  return { tracked, online, problem: needsLogin || failing, needsLogin, place, ship, line, details }
}
