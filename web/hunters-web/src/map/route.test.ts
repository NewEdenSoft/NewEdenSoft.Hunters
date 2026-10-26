import { describe, expect, it } from 'vitest'
import { buildMapModel } from './model'
import { gateRoute } from './route'

const point = { x: 0, y: 0 }
/** A chain 1 - 2 - 3 - 4 - 5 with a shortcut 1 - 4, and 6 with no gates. */
const model = buildMapModel({
  sdeBuild: 1,
  regions: [{ id: 1, name: 'Region' }],
  systems: [1, 2, 3, 4, 5, 6].map((id) => ({ id, name: `S${id}`, regionId: 1, constellationId: 1, security: -0.5, topDown: point, flat: point })),
  gates: [...[1, 2, 3, 4].map((id) => ({ from: id, to: id + 1 })), { from: 1, to: 4 }],
})

describe('gateRoute', () => {
  it('takes the fewest gate jumps, both ends included', () => {
    expect(gateRoute(model, 0, 4)).toEqual([0, 3, 4])
    expect(gateRoute(model, 4, 1)).toEqual([4, 3, 2, 1])
    expect(gateRoute(model, 2, 2)).toEqual([2])
  })

  it('is null where no gates lead', () => {
    expect(gateRoute(model, 0, 5)).toBeNull()
  })
})
