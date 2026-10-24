import { describe, expect, it } from 'vitest'
import { formatLy, isCynoTarget, jumpRangeOf } from './jumpRange'
import { buildMapModel } from './model'

/** Systems along x, at 0, 3, 5, 8, 9 ly; one raised 4 ly; Pochven and Zarzakh in range. */
const model = buildMapModel({
  sdeBuild: 1,
  regions: [{ id: 1, name: 'Region' }, { id: 10000070, name: 'Pochven' }],
  systems: [
    { id: 1, x: 0, security: -0.5 },
    { id: 2, x: 3, security: 0.4 },
    { id: 3, x: 5, security: 0.45 }, // Shown as 0.5: high-sec.
    { id: 4, x: 8, security: -0.1 },
    { id: 5, x: 9, security: -0.1 },
    { id: 6, x: 6, security: -0.2, height: 4 }, // sqrt(36 + 16) = 7.2 ly.
    { id: 7, x: 2, security: -1, regionId: 10000070 },
    { id: 30100000, x: 1, security: -1 },
  ].map((s) => ({
    id: s.id,
    name: `S${s.id}`,
    regionId: s.regionId ?? 1,
    constellationId: 1,
    security: s.security,
    topDown: { x: s.x, y: 0 },
    flat: { x: s.x, y: 0 },
    height: s.height ?? 0,
  })),
  gates: [],
})
const names = (mask: Uint8Array) => [...mask].flatMap((v, i) => (v ? [model.names[i]] : []))

describe('jumpRangeOf', () => {
  it('reaches the cyno targets within the range, in 3D', () => {
    expect(names(jumpRangeOf(model, 0, 8))).toEqual(['S2', 'S4', 'S6'])
    expect(names(jumpRangeOf(model, 0, 7))).toEqual(['S2'])
  })

  it('leaves out high-sec (shown 0.5 and up), Pochven, Zarzakh and the origin', () => {
    expect(isCynoTarget(model, model.indexOf.get(3)!)).toBe(false)
    expect(isCynoTarget(model, model.indexOf.get(7)!)).toBe(false)
    expect(isCynoTarget(model, model.indexOf.get(30100000)!)).toBe(false)
    expect(jumpRangeOf(model, 1, 10)[1]).toBe(0)
  })
})

describe('formatLy', () => {
  it('shows whole ranges plainly and others with decimals', () => {
    expect(formatLy(8)).toBe('8 ly')
    expect(formatLy(7.5)).toBe('7.50 ly')
    expect(formatLy(12.25)).toBe('12.3 ly')
  })
})
