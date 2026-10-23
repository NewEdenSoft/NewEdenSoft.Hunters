import { describe, expect, it } from 'vitest'
import { buildMapModel } from './model'
import { parseSystemParam, searchSystems } from './systemSearch'

const point = { x: 0, y: 0 }
const model = buildMapModel({
  sdeBuild: 1,
  regions: [{ id: 10000002, name: 'The Forge' }],
  systems: ['Jita', 'Ajitayan', 'Jitanmaken', '1DQ1-A', 'Amarr', 'Niarja', 'Perimeter'].map((name, i) => ({
    id: 30000001 + i,
    name,
    regionId: 10000002,
    constellationId: 20000001,
    security: 0.5,
    topDown: point,
    flat: point,
  })),
  gates: [],
})

const names = (query: string, limit?: number) => searchSystems(model, query, limit).map((i) => model.names[i])

describe('searchSystems', () => {
  it('puts the exact name first, then names starting with it, then names containing it', () => {
    expect(names('jita')).toEqual(['Jita', 'Jitanmaken', 'Ajitayan'])
  })

  it('ignores case and punctuation', () => {
    expect(names('1dq1a')).toEqual(['1DQ1-A'])
    expect(names('1DQ1-')).toEqual(['1DQ1-A'])
    expect(names('  AMARR ')).toEqual(['Amarr'])
  })

  it('orders alphabetically within a rank', () => {
    expect(names('ar')).toEqual(['Amarr', 'Niarja'])
  })

  it('returns at most the limit', () => {
    expect(names('a', 2)).toHaveLength(2)
  })

  it('finds nothing for an empty or punctuation-only query', () => {
    expect(names('')).toEqual([])
    expect(names(' - ')).toEqual([])
    expect(names('zzz')).toEqual([])
  })
})

describe('parseSystemParam', () => {
  it('accepts positive integers from the URL', () => {
    expect(parseSystemParam('30000142')).toBe(30000142)
    expect(parseSystemParam(30000142)).toBe(30000142)
  })

  it('rejects anything else', () => {
    for (const value of [undefined, '', 'jita', '-1', '0', '1.5', 1.5, null, {}]) expect(parseSystemParam(value)).toBeUndefined()
  })
})
