import { describe, expect, it } from 'vitest'
import { defaultNearby, distanceText, inJumpRange, NEARBY_LIMIT, nearbyRequest, parseNearby } from './nearby'

describe('parseNearby', () => {
  it('keeps a valid setting', () => {
    expect(parseNearby({ jumps: 10, byJumpRange: true, sort: 'carriers' })).toEqual({ jumps: 10, byJumpRange: true, sort: 'carriers' })
    expect(parseNearby({ jumps: null, byJumpRange: true, sort: 'activity' })).toEqual({ jumps: null, byJumpRange: true, sort: 'activity' })
  })

  it('falls back to the defaults for anything else', () => {
    expect(parseNearby(null)).toEqual(defaultNearby)
    expect(parseNearby({ jumps: 7, byJumpRange: 'yes', sort: '' })).toEqual(defaultNearby)
  })
})

describe('nearbyRequest', () => {
  it('asks from the focused system with the chosen limits', () => {
    expect(nearbyRequest({ jumps: 5, byJumpRange: true, sort: 'carriers' }, 30000142, 8, ['carriers'])).toEqual({
      from: 30000142,
      jumps: 5,
      ly: 8,
      profile: 'carriers',
      limit: NEARBY_LIMIT + 1,
    })
  })

  it('sorts by activity for a profile the server does not list', () => {
    expect(nearbyRequest({ jumps: 5, byJumpRange: false, sort: 'gone' }, 1, 8, ['carriers'])?.profile).toBeNull()
  })

  it('asks nothing without a focus, or with neither gates nor jump range', () => {
    expect(nearbyRequest(defaultNearby, null, 8, [])).toBeNull()
    expect(nearbyRequest({ jumps: null, byJumpRange: false, sort: 'activity' }, 1, 8, [])).toBeNull()
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
