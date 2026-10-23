import { describe, expect, it } from 'vitest'
import type { ActivityColumns, ActivityCurrent } from '../api/activity'
import { heatBands } from '../theme/themes'
import { createFilter, heatGroup, parseColourParam, parseMinNpcParam } from './filters'
import { buildMapModel } from './model'

const point = { x: 0, y: 0 }
const model = buildMapModel({
  sdeBuild: 7,
  regions: [{ id: 1, name: 'Region' }],
  systems: ['A', 'B', 'C', 'D'].map((name, i) => ({ id: i + 1, name, regionId: 1, constellationId: 1, security: -0.5, topDown: point, flat: point })),
  gates: [],
})

const columns = (lastHour: number[], previousHour: number[] | null = null): ActivityColumns => ({
  lastHour,
  previousHour,
  day: lastHour,
  typical: null,
  peakRatio: lastHour.map(() => null),
  hoursToPeak: lastHour.map(() => null),
  peakHourOfWeek: lastHour.map(() => null),
})

/** NPC kills last hour 0 / 120 / 600 / 30, the hour before 0 / 20 / 900 / 0; ship kills 0 / 2 / 30 / 0. */
const activity = (overrides: Partial<ActivityCurrent> = {}): ActivityCurrent => {
  const systems = {
    npcKills: columns([0, 120, 600, 30], [0, 20, 900, 0]),
    shipKills: columns([0, 2, 30, 0]),
    podKills: columns([0, 0, 0, 0]),
    shipJumps: columns([5, 25, 400, 0]),
  }
  return { version: 1, sdeBuild: 7, killsPeriodEnd: '', jumpsPeriodEnd: '', killsDayHours: 24, jumpsDayHours: 24, systems, regions: systems, ...overrides }
}

const groups = (id: Parameters<typeof createFilter>[0], a: ActivityCurrent | null, minNpc = 0) => [...createFilter(id, model, { activity: a, minNpc }).groups]

describe('heatGroup', () => {
  it('is 0 below the first bound and the band reached above it', () => {
    const bands = [1, 100, 250, 500, 1000]
    expect([0, 1, 99, 100, 499, 500, 5000].map((v) => heatGroup(v, bands))).toEqual([0, 1, 1, 2, 3, 4, 5])
  })
})

describe('activity filters', () => {
  it('colour counts by the bands in themes.json', () => {
    // NPC bands 1 / 100 / 250 / 500 / 1000; ship kills 1 / 3 / 6 / 11 / 26; jumps 1 / 20 / 50 / 120 / 300.
    expect(heatBands('npcKills')).toEqual([1, 100, 250, 500, 1000])
    expect(groups('npc', activity())).toEqual([0, 2, 4, 1])
    expect(groups('ship', activity())).toEqual([0, 1, 5, 0])
    expect(groups('jumps', activity())).toEqual([1, 2, 5, 0])
  })

  it('colour the rise or the fall from the hour before', () => {
    // Changes: 0, +100, -300, +30. NPC change bands 1 / 50 / 100 / 200 / 400.
    expect(groups('npc-rising', activity())).toEqual([0, 3, 0, 1])
    expect(groups('npc-falling', activity())).toEqual([0, 0, 4, 0])
  })

  it('keep systems under the minimum NPC kills dim when rising or falling', () => {
    expect(groups('npc-rising', activity(), 100)).toEqual([0, 3, 0, 0])
  })

  it('show a notice instead without a previous hour, without data, or for another map version', () => {
    const noPrevious = activity()
    noPrevious.systems = { ...noPrevious.systems, npcKills: columns([0, 120, 600, 30]) }
    expect(createFilter('npc-rising', model, { activity: noPrevious, minNpc: 0 }).notice).toMatch(/hour before is missing/)
    expect(createFilter('npc', model, { activity: null, minNpc: 0 }).notice).toMatch(/No activity data/)
    expect(createFilter('npc', model, { activity: activity({ sdeBuild: 8 }), minNpc: 0 }).notice).toMatch(/another map version/)
    expect(groups('npc', null)).toEqual([0, 0, 0, 0])
  })

  it('leave security alone', () => {
    expect(createFilter('security', model, { activity: null, minNpc: 0 }).notice).toBeNull()
  })
})

describe('search parameters', () => {
  it('keep known colours other than the default', () => {
    expect(parseColourParam('npc-rising')).toBe('npc-rising')
    expect(parseColourParam('security')).toBeUndefined()
    expect(parseColourParam('nope')).toBeUndefined()
  })

  it('keep only the offered minimums', () => {
    expect(parseMinNpcParam('100')).toBe(100)
    expect(parseMinNpcParam(25)).toBe(25)
    for (const v of ['0', '7', 'x', undefined]) expect(parseMinNpcParam(v)).toBeUndefined()
  })
})
