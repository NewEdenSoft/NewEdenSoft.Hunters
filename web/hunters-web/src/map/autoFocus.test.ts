import { describe, expect, it } from 'vitest'
import { autoFocusReason, defaultMapBehaviour, jumpsWithin } from './autoFocus'
import { buildMapModel } from './model'

const point = { x: 0, y: 0 }
/** A chain of systems 1 - 2 - 3 - 4 - 5 - 6, and 7 with no gates. */
const model = buildMapModel({
  sdeBuild: 1,
  regions: [{ id: 1, name: 'Region' }],
  systems: [1, 2, 3, 4, 5, 6, 7].map((id) => ({ id, name: `S${id}`, regionId: 1, constellationId: 1, security: -0.5, topDown: point, flat: point })),
  gates: [1, 2, 3, 4, 5].map((id) => ({ from: id, to: id + 1 })),
})
const wormhole = 31000005

describe('jumpsWithin', () => {
  it('counts gate jumps up to the limit', () => {
    expect(jumpsWithin(model, 0, 3, 3)).toBe(true)
    expect(jumpsWithin(model, 0, 3, 2)).toBe(false)
    expect(jumpsWithin(model, 0, 6, 10)).toBe(false) // No gates lead to S7.
  })
})

describe('autoFocusReason', () => {
  const behaviour = { ...defaultMapBehaviour, minJumps: 3 }

  it('focuses on coming from unknown space into known space', () => {
    expect(autoFocusReason(model, wormhole, 2, behaviour)).toBe('known-space')
    expect(autoFocusReason(model, wormhole, 2, { ...behaviour, enterKnownSpace: false })).toBeNull()
    // Into unknown space: nothing on the map to focus.
    expect(autoFocusReason(model, 2, wormhole, behaviour)).toBeNull()
  })

  it('focuses after a move of at least the minimum jumps, or with no gate route', () => {
    expect(autoFocusReason(model, 1, 2, behaviour)).toBeNull()
    expect(autoFocusReason(model, 1, 3, behaviour)).toBeNull()
    expect(autoFocusReason(model, 1, 4, behaviour)).toBe('long-move')
    expect(autoFocusReason(model, 1, 7, behaviour)).toBe('long-move')
    expect(autoFocusReason(model, 1, 4, { ...behaviour, longMove: false })).toBeNull()
  })

  it('never focuses a first known location', () => {
    expect(autoFocusReason(model, null, 4, behaviour)).toBeNull()
  })
})
