/**
 * A uniform grid over interleaved x, y points, for finding the point nearest to the cursor
 * without scanning every system.
 */
export class GridIndex {
  private readonly cells = new Map<number, number[]>()
  private readonly xy: Float32Array
  private readonly cellSize: number

  constructor(xy: Float32Array, cellSize: number) {
    this.xy = xy
    this.cellSize = cellSize
    for (let i = 0; i < xy.length / 2; i++) {
      const key = this.key(this.cell(xy[i * 2]), this.cell(xy[i * 2 + 1]))
      const list = this.cells.get(key)
      if (list) list.push(i)
      else this.cells.set(key, [i])
    }
  }

  /** Index of the point nearest to (x, y) within maxDistance, or -1. */
  nearest(x: number, y: number, maxDistance: number): number {
    const reach = Math.ceil(maxDistance / this.cellSize)
    const cx = this.cell(x)
    const cy = this.cell(y)
    let best = -1
    let bestDistance = maxDistance * maxDistance
    for (let gx = cx - reach; gx <= cx + reach; gx++) {
      for (let gy = cy - reach; gy <= cy + reach; gy++) {
        const list = this.cells.get(this.key(gx, gy))
        if (!list) continue
        for (const i of list) {
          const dx = this.xy[i * 2] - x
          const dy = this.xy[i * 2 + 1] - y
          const distance = dx * dx + dy * dy
          if (distance <= bestDistance) {
            bestDistance = distance
            best = i
          }
        }
      }
    }
    return best
  }

  private cell(value: number): number {
    return Math.floor(value / this.cellSize)
  }

  // Cell coordinates stay well inside ±2^15 for the map's ±100 ly at 1 ly cells.
  private key(gx: number, gy: number): number {
    return (gx + 32768) * 65536 + (gy + 32768)
  }
}
