import { describe, expect, it } from 'vitest'
import { clampRect, EDGE_MARGIN, PANEL_GAP, snapMove, snapResize, type Rect } from './geometry'

const area = { width: 1000, height: 700 }
const rect = (x: number, y: number, width = 200, height = 100): Rect => ({ x, y, width, height })

describe('clampRect', () => {
  it('leaves a panel inside the area alone', () => {
    expect(clampRect(rect(50, 60), area)).toEqual(rect(50, 60))
  })

  it('moves a panel back in when the area shrinks', () => {
    expect(clampRect(rect(900, 650), area)).toEqual(rect(800, 600))
    expect(clampRect(rect(-30, -5), area)).toEqual(rect(0, 0))
  })

  it('shrinks a panel larger than the area', () => {
    expect(clampRect(rect(10, 10, 1200, 900), area)).toEqual(rect(0, 0, 1000, 700))
  })
})

describe('snapMove', () => {
  it('snaps to the area edges with a margin', () => {
    expect(snapMove(rect(5, 300), [], area)).toEqual(rect(EDGE_MARGIN, 300))
    expect(snapMove(rect(785, 590), [], area)).toEqual(rect(1000 - EDGE_MARGIN - 200, 700 - EDGE_MARGIN - 100))
  })

  it('does not snap from further away', () => {
    expect(snapMove(rect(40, 300), [], area)).toEqual(rect(40, 300))
  })

  it('snaps beside another panel with a gap, and lines up with it', () => {
    const other = rect(100, 100)
    // Just right of the other panel, a little lower: next to it with a gap, tops aligned.
    expect(snapMove(rect(305, 106), [other], area)).toEqual(rect(300 + PANEL_GAP, 100))
    // Just below it, a little to the right: under it with a gap, left edges aligned.
    expect(snapMove(rect(104, 203), [other], area)).toEqual(rect(100, 200 + PANEL_GAP))
  })

  it('ignores panels that are not beside it', () => {
    const farBelow = rect(300, 500)
    expect(snapMove(rect(505, 100), [farBelow], area)).toEqual(rect(505, 100))
  })
})

describe('snapResize', () => {
  const min = { width: 180, height: 80 }

  it('snaps the right and bottom edges to the area', () => {
    expect(snapResize(rect(600, 400, 383, 285), [], area, min)).toEqual(rect(600, 400, 1000 - EDGE_MARGIN - 600, 700 - EDGE_MARGIN - 400))
  })

  it('stays inside the area, but never below the minimum size', () => {
    expect(snapResize(rect(100, 100, 50, 20), [], area, min)).toEqual(rect(100, 100, 180, 80))
    expect(snapResize(rect(300, 200, 900, 900), [], area, min)).toEqual(rect(300, 200, 700, 500))
    // Too close to the corner for the minimum: the minimum wins, and clampRect moves the panel back in.
    expect(snapResize(rect(900, 650, 400, 400), [], area, min)).toEqual(rect(900, 650, 180, 80))
  })

  it('snaps to stop just short of a panel to the right', () => {
    const other = rect(500, 100)
    expect(snapResize(rect(100, 100, 395, 100), [other], area, min)).toEqual(rect(100, 100, 400 - PANEL_GAP, 100))
  })
})
