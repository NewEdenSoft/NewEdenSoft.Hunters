import { describe, expect, it } from 'vitest'
import { buildMapModel, lyBetween } from './model'

const system = (id: number, x: number, y: number, height?: number) => ({
  id,
  name: `S${id}`,
  regionId: 1,
  constellationId: 1,
  security: -0.5,
  topDown: { x, y },
  flat: { x, y },
  height,
})

describe('lyBetween', () => {
  it('measures in 3D with the height', () => {
    const model = buildMapModel({
      sdeBuild: 1,
      regions: [{ id: 1, name: 'Region' }],
      systems: [system(1, 0, 0, 0), system(2, 3, 4, 12), system(3, 3, 4)],
      gates: [],
    })

    expect(lyBetween(model, 0, 1)).toBeCloseTo(13)
    // A pack without heights (format 1): flat in the top-down plane.
    expect(lyBetween(model, 0, 2)).toBeCloseTo(5)
  })
})
