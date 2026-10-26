import { describe, expect, it } from 'vitest'
import type { HuntingOverview } from '../api/hunting'
import { buildHuntingLayer, likelyNow, profilesBySystem } from './hunting'
import { buildMapModel } from './model'

const point = { x: 0, y: 0 }
const model = buildMapModel({
  sdeBuild: 1,
  regions: [{ id: 1, name: 'Region' }],
  systems: [1, 2, 3, 4].map((id) => ({ id, name: `S${id}`, regionId: 1, constellationId: 1, security: -0.5, topDown: point, flat: point })),
  gates: [],
})

const at = (...weights: [number, number][]) => {
  const hours = new Array<number>(24).fill(0)
  for (const [hour, weight] of weights) hours[hour] = weight
  return hours
}

const style = (profile: string) => ({ code: profile.slice(0, 2).toUpperCase(), colour: `#${profile.length}` })

/** Carriers: strong in S1 at 19:00, weaker in S2 at 19:00, in S3 only at 03:00. CRAB: S2 at 19:00. S99 is not on the map. */
const overview: HuntingOverview = {
  version: 1,
  updatedAt: '',
  profiles: [
    { profile: 'carriers', systemIds: [1, 2, 3, 99], score: [8, 2, 5, 9], week: [], month: [], lastSeen: [], hours: [at([19, 4]), at([19, 1], [20, 1]), at([3, 2]), at([19, 1])], reliable: [] },
    { profile: 'crab', systemIds: [2], score: [3], week: [], month: [], lastSeen: [], hours: [at([19, 1])], reliable: [] },
  ],
}

describe('likelyNow', () => {
  it('is the score times this hour\'s share of the sightings', () => {
    expect(likelyNow(8, at([19, 3], [20, 1]), 19)).toBe(6)
    expect(likelyNow(8, at([19, 3], [20, 1]), 3)).toBe(0)
    expect(likelyNow(8, at(), 19)).toBe(0)
  })
})

describe('buildHuntingLayer', () => {
  it('glows each system by the strongest shown profile likely now, relative to its strongest system', () => {
    const layer = buildHuntingLayer(model, overview, ['carriers'], 19, style)

    // Likely now: S1 8, S2 1 (half its sightings are at 19:00), S3 0.
    expect([...layer.glow].map((g) => Math.round(g * 1000) / 1000)).toEqual([1, Math.round(Math.sqrt(1 / 8) * 1000) / 1000, 0, 0])
  })

  it('takes the strongest of several shown profiles', () => {
    const layer = buildHuntingLayer(model, overview, ['carriers', 'crab'], 19, style)

    expect(layer.glow[1]).toBe(1) // CRAB's only system is its strongest.
  })

  it('badges every shown profile seen in a system, faint when not usually there at this hour', () => {
    const layer = buildHuntingLayer(model, overview, ['carriers', 'crab'], 19, style)
    const bySystem = Object.fromEntries(layer.badges.map((g) => [model.names[g.systemIndex], g.badges.map((b) => `${b.code}${b.faint ? ' faint' : ''}`)]))

    expect(bySystem).toEqual({ S1: ['CA'], S2: ['CA', 'CR'], S3: ['CA faint'] })
  })

  it('shows nothing for profiles not chosen or not in the overview', () => {
    const layer = buildHuntingLayer(model, overview, ['ishtars'], 19, style)

    expect(layer.badges).toEqual([])
    expect([...layer.glow]).toEqual([0, 0, 0, 0])
  })
})

describe('profilesBySystem', () => {
  it('lists the shown profiles seen at a system, likely now first, then by score', () => {
    const bySystem = profilesBySystem(model, overview, ['carriers', 'crab'], 19, style, (id) => id.toUpperCase())

    // S2: carriers likely 1 (score 2), CRAB likely 3 (score 3).
    expect(bySystem.get(1)!.map((p) => `${p.name} ${p.likely} ${p.peakNow}`)).toEqual(['CRAB 3 1', 'CARRIERS 1 1'])
    // S3: carriers seen, but only at 03:00.
    expect(bySystem.get(2)!.map((p) => `${p.name} ${p.likely} ${p.peakNow}`)).toEqual(['CARRIERS 0 0'])
    expect(bySystem.has(3)).toBe(false)
  })

  it('leaves out the profiles not shown', () => {
    const bySystem = profilesBySystem(model, overview, ['crab'], 19, style, (id) => id)

    expect([...bySystem.keys()]).toEqual([1])
    expect(bySystem.get(1)!.map((p) => p.profile)).toEqual(['crab'])
  })
})
