import { beforeEach, describe, expect, it, vi } from 'vitest'
import { EveNames } from '../tracking/names'
import type { TrackedCharacter } from '../tracking/types'
import { getMarker, isTracked, reloadCharacterPrefs, setMarker, setTracked } from './characterPrefs'
import { defaultMarker, ICONS, isValidMarker, LETTERS, MARKER_ICONS, NUMBERS } from './markers'
import { describeWhereabouts, namesToResolve } from './whereabouts'

describe('markers', () => {
  it.each([
    ['Jita Trader', 'J'],
    ['vex Arkhan', 'V'],
    ['1st Hunter', 'S'],
    ['--', 'A'],
  ])('defaults %s to %s', (name, letter) => {
    expect(defaultMarker(name)).toEqual({ kind: 'letter', value: letter })
  })

  it('offers the same choices as the desktop', () => {
    expect(LETTERS).toHaveLength(26)
    expect(NUMBERS.map((m) => m.value)).toEqual(['1', '2', '3', '4', '5', '6', '7', '8', '9'])
    expect(ICONS.map((m) => m.value)).toEqual(['target', 'star', 'diamond', 'triangle', 'bolt', 'shield', 'eye', 'heart'])
    expect(Object.values(MARKER_ICONS).every((path) => path.startsWith('M'))).toBe(true)
  })

  it.each([
    { kind: 'letter', value: 'AB' },
    { kind: 'letter', value: 'a' },
    { kind: 'number', value: '0' },
    { kind: 'icon', value: 'rocket' },
    { kind: 'emoji', value: 'A' },
    'A',
    null,
  ])('refuses %o', (marker) => {
    expect(isValidMarker(marker)).toBe(false)
  })
})

describe('character preferences', () => {
  let storage: Map<string, string>

  beforeEach(() => {
    storage = new Map()
    vi.stubGlobal('localStorage', {
      getItem: (k: string) => storage.get(k) ?? null,
      setItem: (k: string, v: string) => storage.set(k, v),
    })
    vi.stubGlobal('window', { addEventListener() {}, removeEventListener() {} })
    reloadCharacterPrefs()
  })

  it('keeps markers and tracking switches per character, across reloads', () => {
    setMarker(1, { kind: 'icon', value: 'star' })
    setTracked(2, false)

    reloadCharacterPrefs()

    expect(getMarker(1, 'Jita Trader')).toEqual({ kind: 'icon', value: 'star' })
    expect(getMarker(3, 'Other Pilot')).toEqual({ kind: 'letter', value: 'O' })
    expect(isTracked(1)).toBe(true)
    expect(isTracked(2)).toBe(false)
  })

  it('falls back to the default when the saved marker is not valid', () => {
    storage.set('hunters.characters', JSON.stringify({ 1: { marker: { kind: 'icon', value: 'rocket' } } }))
    reloadCharacterPrefs()

    expect(getMarker(1, 'Jita Trader')).toEqual({ kind: 'letter', value: 'J' })
  })

  it('refuses invalid markers', () => {
    expect(() => setMarker(1, { kind: 'number', value: '42' })).toThrow()
  })
})

describe('whereabouts', () => {
  const names = new EveNames(async (ids) => {
    const all = new Map([
      [30000142, 'Jita'],
      [60003760, 'Jita IV - Moon 4 - Caldari Navy Assembly Plant'],
      [12005, 'Ishtar'],
    ])
    return new Map(ids.filter((id) => all.has(id)).map((id) => [id, all.get(id)!]))
  })

  const state = (stationId: number | null, structureId: number | null, shipName = 'Ishtar', typeId = 12005): TrackedCharacter => ({
    characterId: 1,
    status: 'tracking',
    isOnline: true,
    location: { solarSystemId: 30000142, stationId, structureId },
    ship: { typeId, name: shipName, itemId: 1 },
    error: null,
    updatedAt: 0,
  })

  beforeEach(async () => {
    await names.resolve([30000142, 60003760, 12005])
  })

  it('is empty until the location is known', () => {
    expect(describeWhereabouts(null, names)).toBeNull()
    expect(describeWhereabouts({ ...state(null, null), location: null }, names)).toBeNull()
  })

  it.each([
    [null, null, null],
    [60003760, null, 'Docked at Jita IV - Moon 4 - Caldari Navy Assembly Plant'],
    [null, 1035466617946, 'Docked'],
  ])('station %s, structure %s: %s', (station, structure, docked) => {
    expect(describeWhereabouts(state(station, structure), names)).toEqual({ system: 'Jita', docked, ship: 'Ishtar' })
  })

  it.each([
    ['Hunter One', 12005, 'Ishtar “Hunter One”'],
    ['Ishtar', 12005, 'Ishtar'],
    [' ', 12005, 'Ishtar'],
    ['Hunter One', 999, 'Hunter One'],
  ])('ship named %s of type %i reads %s', (name, typeId, expected) => {
    expect(describeWhereabouts(state(null, null, name, typeId), names)?.ship).toBe(expected)
  })

  it('never looks up player structures', () => {
    expect(namesToResolve(state(null, 1035466617946)).sort()).toEqual([12005, 30000142])
  })
})
