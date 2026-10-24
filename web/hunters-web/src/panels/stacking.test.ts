import { describe, expect, it } from 'vitest'
import { isOnTop, restack } from './stacking'

describe('restack', () => {
  it('puts the front layer on top and renumbers from 1', () => {
    expect(restack({ map: 40, characters: 41, search: 42 }, 'map')).toEqual({ characters: 1, search: 2, map: 3 })
  })

  it('renumbers without a front layer, keeping the order', () => {
    expect(restack({ map: 900, hunting: 2, characters: 1500 })).toEqual({ hunting: 1, map: 2, characters: 3 })
  })

  it('keeps the order of equal z-indexes', () => {
    expect(restack({ map: 1, hunting: 1, characters: 1 }, 'map')).toEqual({ hunting: 1, characters: 2, map: 3 })
  })

  it('ignores an unknown front layer', () => {
    expect(restack({ map: 2, characters: 1 }, 'fleet')).toEqual({ characters: 1, map: 2 })
  })
})

describe('isOnTop', () => {
  it('is true only when the layer is above every other one', () => {
    expect(isOnTop({ map: 3, search: 2 }, 'map')).toBe(true)
    expect(isOnTop({ map: 3, search: 3 }, 'map')).toBe(false)
    expect(isOnTop({ map: 1, search: 2 }, 'map')).toBe(false)
  })
})
