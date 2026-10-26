import { describe, expect, it } from 'vitest'
import type { ReachMetric } from '../api/reach'
import { defaultNearby, distanceText, inJumpRange, isActive, NEARBY_FETCH, nearbyRequest, parseNearby, withinJumps } from './nearby'

describe('parseNearby', () => {
  it('keeps a valid setting', () => {
    expect(parseNearby({ jumps: 10, byJumpRange: true, sort: 'carriers', showQuiet: true })).toEqual({ jumps: 10, byJumpRange: true, sort: 'carriers', showQuiet: true })
    expect(parseNearby({ jumps: null, byJumpRange: true, sort: 'activity' })).toEqual({ jumps: null, byJumpRange: true, sort: 'activity', showQuiet: false })
  })

  it('falls back to the defaults for anything else', () => {
    expect(parseNearby(null)).toEqual(defaultNearby)
    expect(parseNearby({ jumps: 7, byJumpRange: 'yes', sort: '' })).toEqual(defaultNearby)
  })
})

describe('nearbyRequest', () => {
  it('asks from the focused system with the chosen limits', () => {
    expect(nearbyRequest({ ...defaultNearby, jumps: 5, byJumpRange: true, sort: 'carriers' }, 30000142, 8, ['carriers'])).toEqual({
      from: 30000142,
      jumps: 5,
      ly: null,
      profile: 'carriers',
      sort: 'npc-rising',
      limit: NEARBY_FETCH,
    })
  })

  it('asks for the light years only without gate jumps', () => {
    expect(nearbyRequest({ ...defaultNearby, jumps: null, byJumpRange: true }, 1, 8, [])).toMatchObject({ jumps: null, ly: 8 })
  })

  it('sorts by activity for a profile the server does not list', () => {
    expect(nearbyRequest({ ...defaultNearby, sort: 'gone' }, 1, 8, ['carriers'])).toMatchObject({ profile: null, sort: 'npc-rising' })
  })

  it('sorts by the most NPC kills when asked', () => {
    expect(nearbyRequest({ ...defaultNearby, sort: 'npc-kills' }, 1, 8, ['carriers'])).toMatchObject({ profile: null, sort: 'npc-kills' })
  })

  it('asks nothing without a focus, or with neither gates nor jump range', () => {
    expect(nearbyRequest(defaultNearby, null, 8, [])).toBeNull()
    expect(nearbyRequest({ ...defaultNearby, jumps: null, byJumpRange: false }, 1, 8, [])).toBeNull()
  })
})

describe('isActive', () => {
  const metric = (lastHour: number, day: number): ReachMetric => ({ lastHour, previousHour: 0, day, typical: null, peakRatio: null, hoursToPeak: null })
  const quiet = { npcKills: metric(0, 0), shipKills: metric(0, 0), podKills: metric(0, 0), shipJumps: metric(400, 9000) }
  const profile = { profile: 'carriers', score: 1, lastSeen: '2026-10-20T00:00:00Z', likelyNow: 0, peakNow: null }

  it('is any kill in the last hour or day, or a profile seen', () => {
    expect(isActive({ activity: { ...quiet, npcKills: metric(3, 3) }, profiles: [] })).toBe(true)
    expect(isActive({ activity: { ...quiet, shipKills: metric(0, 1) }, profiles: [] })).toBe(true)
    expect(isActive({ activity: { ...quiet, podKills: metric(0, 2) }, profiles: [] })).toBe(true)
    expect(isActive({ activity: quiet, profiles: [profile] })).toBe(true)
  })

  it('leaves out systems with only traffic, and keeps the ones that cannot be judged', () => {
    expect(isActive({ activity: quiet, profiles: [] })).toBe(false)
    expect(isActive({ activity: null, profiles: [] })).toBe(true)
  })
})

describe('withinJumps', () => {
  it('keeps the systems within the gate jumps, or every one without them', () => {
    expect(withinJumps({ jumps: 3 }, 3)).toBe(true)
    expect(withinJumps({ jumps: 4 }, 3)).toBe(false)
    expect(withinJumps({ jumps: null }, 3)).toBe(false)
    expect(withinJumps({ jumps: null }, null)).toBe(true)
  })
})

describe('distanceText', () => {
  it('shows what was asked for', () => {
    expect(distanceText({ jumps: 3, ly: 4.24 }, true, false)).toBe('3 j')
    expect(distanceText({ jumps: 3, ly: 4.24 }, false, true)).toBe('4.2 ly')
    expect(distanceText({ jumps: 12, ly: 12.6 }, true, true)).toBe('12 j · 13 ly')
    expect(distanceText({ jumps: null, ly: 7.1 }, true, true)).toBe('no gates · 7.1 ly')
  })
})

describe('inJumpRange', () => {
  it('is a cyno target within the light years', () => {
    expect(inJumpRange({ cynoTarget: true, ly: 7.9 }, 8)).toBe(true)
    expect(inJumpRange({ cynoTarget: false, ly: 2 }, 8)).toBe(false)
    expect(inJumpRange({ cynoTarget: true, ly: 8.1 }, 8)).toBe(false)
    expect(inJumpRange({ cynoTarget: true, ly: 1 }, null)).toBe(false)
  })
})
