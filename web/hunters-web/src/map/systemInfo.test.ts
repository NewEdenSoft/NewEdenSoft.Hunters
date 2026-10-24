import { describe, expect, it } from 'vitest'
import type { ActivityColumns, ActivityMetrics } from '../api/activity'
import type { RecentKill } from '../api/kills'
import {
  dotlanSystemUrl,
  formatIsk,
  killNameIds,
  metricRows,
  partyName,
  peakHint,
  profileStatus,
  sortProfiles,
  timeAgo,
} from './systemInfo'

const columns = (last: number[], previous: number[] | null, extra: Partial<ActivityColumns> = {}): ActivityColumns => ({
  lastHour: last,
  previousHour: previous,
  day: last.map((v) => v * 10),
  typical: last.map((v) => v / 2),
  peakRatio: last.map(() => null),
  hoursToPeak: last.map(() => null),
  peakHourOfWeek: last.map(() => null),
  ...extra,
})

describe('metricRows', () => {
  it('gives each metric its last hour, change, day, usual and peak', () => {
    const metrics: ActivityMetrics = {
      npcKills: columns([5, 120], [5, 80], { peakRatio: [null, 0.9] }),
      shipKills: columns([0, 3], null),
      podKills: columns([0, 1], [0, 2]),
      shipJumps: columns([10, 400], [12, 380], { hoursToPeak: [null, 3] }),
    }
    expect(metricRows(metrics, 1)).toEqual([
      { label: 'NPC kills', lastHour: 120, change: 40, day: 1200, typical: 60, peak: 'Peak now' },
      { label: 'Ship kills', lastHour: 3, change: null, day: 30, typical: 1.5, peak: null },
      { label: 'Pod kills', lastHour: 1, change: -1, day: 10, typical: 0.5, peak: null },
      { label: 'Jumps', lastHour: 400, change: 20, day: 4000, typical: 200, peak: 'Peak in 3 h' },
    ])
  })
})

describe('peakHint', () => {
  it('is peak now from 80% of the peak, else a peak within six hours', () => {
    expect(peakHint(0.8, null)).toBe('Peak now')
    expect(peakHint(0.5, 6)).toBe('Peak in 6 h')
    expect(peakHint(0.5, 7)).toBeNull()
    expect(peakHint(null, 0)).toBeNull()
  })
})

describe('profileStatus and sortProfiles', () => {
  const hours = (h: Record<number, number>) => Array.from({ length: 24 }, (_, i) => h[i] ?? 0)
  const seen = { profile: 'seen', score: 9, hours: hours({ 3: 1 }) }
  const now = { profile: 'now', score: 2, hours: hours({ 10: 1, 11: 4 }) }
  const peak = { profile: 'peak', score: 1, hours: hours({ 10: 5 }) }

  it('says whether the profile is usually here at this hour, or at its busiest', () => {
    expect(profileStatus(seen, 10)).toBe('seen')
    expect(profileStatus(now, 10)).toBe('now')
    expect(profileStatus(peak, 10)).toBe('peak now')
  })

  it('puts peak now first, then now, then the rest by score', () => {
    expect(sortProfiles([seen, now, peak], 10).map((p) => p.profile)).toEqual(['peak', 'now', 'seen'])
  })
})

describe('formatIsk', () => {
  it('shortens like EVE players do', () => {
    expect(formatIsk(950)).toBe('950')
    expect(formatIsk(12_500)).toBe('12.5k')
    expect(formatIsk(3_200_000)).toBe('3.2M')
    expect(formatIsk(1_050_000_000)).toBe('1.05B')
    expect(formatIsk(250_400_000)).toBe('250M')
    expect(formatIsk(2_000_000)).toBe('2M')
  })
})

describe('timeAgo', () => {
  const now = Date.parse('2026-10-24T08:00:00Z')
  it('counts minutes, hours, then days', () => {
    expect(timeAgo('2026-10-24T07:59:40Z', now)).toBe('now')
    expect(timeAgo('2026-10-24T07:48:00Z', now)).toBe('12m')
    expect(timeAgo('2026-10-24T05:00:00Z', now)).toBe('3h')
    expect(timeAgo('2026-10-19T08:00:00Z', now)).toBe('5d')
  })
})

describe('kill names', () => {
  const kill: RecentKill = {
    killmailId: 1,
    hash: 'h',
    time: '2026-10-24T07:00:00Z',
    npc: false,
    solo: false,
    totalValue: 1,
    victim: { characterId: 90, corporationId: 98, allianceId: null, shipTypeId: 587 },
    finalBlow: { characterId: null, corporationId: null, allianceId: null, shipTypeId: 30428 },
    attackerCount: 3,
  }

  it('collects every id once', () => {
    expect(killNameIds([kill, { ...kill, finalBlow: null }]).sort((a, b) => a - b)).toEqual([90, 98, 587, 30428])
  })

  it('names the pilot, else the corporation, else the ship', () => {
    const names = new Map([
      [90, 'Victim'],
      [98, 'Corp'],
      [30428, 'Guristas Thug'],
    ])
    expect(partyName(kill.victim, (id) => names.get(id))).toBe('Victim')
    expect(partyName({ ...kill.victim, characterId: null }, (id) => names.get(id))).toBe('Corp')
    expect(partyName(kill.finalBlow!, (id) => names.get(id))).toBe('Guristas Thug')
  })
})

describe('dotlanSystemUrl', () => {
  it('writes spaces as underscores', () => {
    expect(dotlanSystemUrl('Old Man Star')).toBe('https://evemaps.dotlan.net/system/Old_Man_Star')
    expect(dotlanSystemUrl('1DQ1-A')).toBe('https://evemaps.dotlan.net/system/1DQ1-A')
  })
})
