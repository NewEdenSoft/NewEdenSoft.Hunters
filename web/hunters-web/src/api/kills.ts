import { queryOptions } from '@tanstack/react-query'
import { api } from './http'

// Mirrors NewEdenSoft.Hunters.Contracts.Kills.

/** A pilot (or an NPC or structure without one) and its ship; ids are null where there are none. */
export interface KillParty {
  characterId: number | null
  corporationId: number | null
  allianceId: number | null
  shipTypeId: number | null
}

export interface RecentKill {
  killmailId: number
  hash: string
  time: string
  /** The victim died to NPCs only. */
  npc: boolean
  /** One player on the killmail. */
  solo: boolean
  /** zKillboard's estimate of what was destroyed and dropped, in ISK. */
  totalValue: number
  victim: KillParty
  /** Null when unknown (kills stored before the server kept it). */
  finalBlow: KillParty | null
  attackerCount: number
}

export interface SystemKills {
  systemId: number
  kills: RecentKill[]
}

/** A system's newest kills, asked again every minute while shown (the ETag keeps that cheap). */
export const systemKillsQuery = (systemId: number, limit = 10) =>
  queryOptions({
    queryKey: ['kills', 'system', systemId, limit],
    queryFn: ({ signal }) => api<SystemKills>(`/api/kills/systems/${systemId}?limit=${limit}`, { signal }),
    staleTime: 30_000,
    refetchInterval: 60_000,
  })
