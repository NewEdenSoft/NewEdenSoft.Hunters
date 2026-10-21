import { HIDDEN, type MapFilter } from './filters'
import { GridIndex } from './gridIndex'
import type { MapLayout, MapModel } from './model'

/** A hovered system and where it is on screen (CSS pixels, relative to the canvas). */
export interface MapHover {
  index: number
  x: number
  y: number
}

/** Zoom (pixels per light year) from which system names are drawn; below it, region names are. */
export const LABEL_ZOOM = 28

const HOVER_RADIUS_PX = 10

/** Dash patterns in CSS pixels: gates between constellations, and between regions. Gates within a constellation are solid. */
const CONSTELLATION_DASH = [6, 4]
const REGION_DASH = [18, 8]

/** Gate styles by what a gate connects: within a constellation, between constellations, between regions. */
const GATE_DASHES = [[], CONSTELLATION_DASH, REGION_DASH]

/** Sizes on screen (CSS pixels) grow with the zoom (pixels per light year) between these limits. */
const SYSTEM_RADIUS = { perZoom: 0.06, min: 0.9, max: 6.5 }
const GATE_WIDTH = { base: 0.25, perZoom: 0.02, min: 0.4, max: 2 }

/** Clear space between a system and the gates around it, fading in from {@link MARGIN_FROM_ZOOM}. */
const SYSTEM_MARGIN = { perZoom: 0.15, max: 5 }
const MARGIN_FROM_ZOOM = 12

/** Gates are drawn a little transparent so the systems stay the brightest thing on the map. */
const GATE_ALPHA = 0.55

/** A gate between two colours: its ends in world coordinates and a gradient from one colour to the other. */
interface GradientGate {
  x1: number
  y1: number
  x2: number
  y2: number
  gradient: CanvasGradient
}

/**
 * Gates ready to draw, in world coordinates, coloured by the filter colours of their two systems.
 * Gates whose ends share a colour are batched into one path per colour and style; the rest (about
 * a quarter with the security filter) each get a gradient, created once and reused every frame.
 */
interface GateLayer {
  /** Per colour, one path per gate style. */
  plain: { colour: string; paths: Path2D[] }[]
  /** Per gate style. */
  gradients: GradientGate[][]
}
const LABEL_FONT_PX = 11
const REGION_FONT_PX = 12
/** Clear space around a region name, so neighbouring names never read as one. */
const REGION_GAP_PX = 14
const LABEL_CELL_PX = 6

interface Palette {
  gate: string
  halo: string
  region: string
  label: string
  labelHover: string
  hover: string
  /** Resolved colours of the filter's groups. */
  groups: string[]
}

/**
 * Draws the map on a canvas with Canvas 2D (docs/decisions.md, D21) and handles pan, zoom and hover.
 * Framework-agnostic: React hosts it and listens to {@link onHover}.
 *
 * Each frame: gates (solid within a constellation, dashed between constellations and regions,
 * coloured from one system's colour to the other's) from cached paths and gradients under the view
 * transform; systems in view,
 * batched by security colour; then, when zoomed in, names to the bottom right of each system,
 * skipping any that would overlap. Frames are drawn only when something changes.
 */
export class MapRenderer {
  onHover: ((hover: MapHover | null) => void) | null = null

  private readonly canvas: HTMLCanvasElement
  private readonly ctx: CanvasRenderingContext2D
  private readonly model: MapModel
  private readonly labelWidths = new Map<number, number>()
  private readonly pointers = new Map<number, { x: number; y: number }>()
  private readonly resizeObserver: ResizeObserver
  private readonly themeObserver: MutationObserver

  private layout: MapLayout
  private filter: MapFilter
  /** System indexes per colour group of the filter. */
  private byGroup: Uint32Array[]
  private xy: Float32Array
  private grid: GridIndex
  private gates: GateLayer
  private palette: Palette
  private labelFont = ''
  private regionFont = ''

  private dpr = 1
  private width = 0
  private height = 0
  private scale = 1
  private fitScale = 1
  private offsetX = 0
  private offsetY = 0
  private fitted = false
  private hovered = -1
  private frame = 0
  private pinchDistance = 0

  constructor(canvas: HTMLCanvasElement, model: MapModel, layout: MapLayout, filter: MapFilter) {
    this.canvas = canvas
    this.ctx = canvas.getContext('2d')!
    this.model = model
    this.layout = layout
    this.xy = model.positions[layout]
    this.grid = new GridIndex(this.xy, 1)
    this.filter = filter
    this.byGroup = groupSystems(filter)
    this.palette = resolvePalette(filter)
    this.gates = this.buildGates()
    this.labelFont = `${LABEL_FONT_PX}px ${getComputedStyle(document.body).fontFamily}`
    const displayFont = getComputedStyle(document.documentElement).getPropertyValue('--font-display').trim()
    this.regionFont = `600 ${REGION_FONT_PX}px ${displayFont || getComputedStyle(document.body).fontFamily}`

    canvas.addEventListener('wheel', this.onWheel, { passive: false })
    canvas.addEventListener('pointerdown', this.onPointerDown)
    canvas.addEventListener('pointermove', this.onPointerMove)
    canvas.addEventListener('pointerup', this.onPointerUp)
    canvas.addEventListener('pointercancel', this.onPointerUp)
    canvas.addEventListener('pointerleave', this.onPointerLeave)

    this.resizeObserver = new ResizeObserver(() => this.resize())
    this.resizeObserver.observe(canvas)

    // Theme colours are CSS variables; re-read them when the theme changes.
    this.themeObserver = new MutationObserver(() => {
      this.palette = resolvePalette(this.filter)
      this.gates = this.buildGates()
      this.requestDraw()
    })
    this.themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-hunters-theme', 'style'] })

    this.resize()
  }

  setLayout(layout: MapLayout): void {
    if (layout === this.layout) return
    this.layout = layout
    this.xy = this.model.positions[layout]
    this.grid = new GridIndex(this.xy, 1)
    this.gates = this.buildGates()
    this.setHovered(-1)
    this.fit()
  }

  /** Changes how systems are coloured (or hidden). */
  setFilter(filter: MapFilter): void {
    if (filter === this.filter) return
    this.filter = filter
    this.byGroup = groupSystems(filter)
    this.palette = resolvePalette(filter)
    this.gates = this.buildGates()
    if (this.hovered >= 0 && filter.groups[this.hovered] === HIDDEN) this.setHovered(-1)
    this.requestDraw()
  }

  /** Zooms out to show the whole map. */
  fit(): void {
    const b = this.model.bounds[this.layout]
    const margin = 24
    const width = Math.max(b.maxX - b.minX, 1)
    const height = Math.max(b.maxY - b.minY, 1)
    this.fitScale = Math.min((this.width - margin * 2) / width, (this.height - margin * 2) / height)
    this.scale = this.fitScale
    this.offsetX = this.width / 2 - ((b.minX + b.maxX) / 2) * this.scale
    this.offsetY = this.height / 2 - ((b.minY + b.maxY) / 2) * this.scale
    this.requestDraw()
  }

  /** Zooms by a factor around a point on screen (CSS pixels). */
  zoomAt(x: number, y: number, factor: number): void {
    const scale = Math.min(Math.max(this.scale * factor, this.fitScale * 0.5), this.fitScale * 80)
    const applied = scale / this.scale
    this.offsetX = x - (x - this.offsetX) * applied
    this.offsetY = y - (y - this.offsetY) * applied
    this.scale = scale
    this.requestDraw()
  }

  destroy(): void {
    cancelAnimationFrame(this.frame)
    this.resizeObserver.disconnect()
    this.themeObserver.disconnect()
    this.canvas.removeEventListener('wheel', this.onWheel)
    this.canvas.removeEventListener('pointerdown', this.onPointerDown)
    this.canvas.removeEventListener('pointermove', this.onPointerMove)
    this.canvas.removeEventListener('pointerup', this.onPointerUp)
    this.canvas.removeEventListener('pointercancel', this.onPointerUp)
    this.canvas.removeEventListener('pointerleave', this.onPointerLeave)
    this.onHover = null
  }

  // ---------- Drawing ----------

  private requestDraw(): void {
    if (this.frame) return
    this.frame = requestAnimationFrame(() => {
      this.frame = 0
      this.draw()
    })
  }

  private draw(): void {
    const { ctx, dpr, scale, offsetX, offsetY, xy, palette } = this
    ctx.setTransform(1, 0, 0, 1, 0, 0)
    ctx.clearRect(0, 0, this.canvas.width, this.canvas.height)

    // Gates: in world coordinates, so widths and dashes are divided by the zoom to stay the same on screen.
    ctx.setTransform(dpr * scale, 0, 0, dpr * scale, dpr * offsetX, dpr * offsetY)
    ctx.lineWidth = this.gateWidth() / scale
    ctx.globalAlpha = GATE_ALPHA
    const viewLeft = -offsetX / scale
    const viewTop = -offsetY / scale
    const viewRight = (this.width - offsetX) / scale
    const viewBottom = (this.height - offsetY) / scale
    for (let style = 0; style < GATE_DASHES.length; style++) {
      ctx.setLineDash(GATE_DASHES[style].map((d) => d / scale))
      for (const { colour, paths } of this.gates.plain) {
        ctx.strokeStyle = colour
        ctx.stroke(paths[style])
      }
      for (const gate of this.gates.gradients[style]) {
        if (
          Math.max(gate.x1, gate.x2) < viewLeft ||
          Math.min(gate.x1, gate.x2) > viewRight ||
          Math.max(gate.y1, gate.y2) < viewTop ||
          Math.min(gate.y1, gate.y2) > viewBottom
        )
          continue
        ctx.strokeStyle = gate.gradient
        ctx.beginPath()
        ctx.moveTo(gate.x1, gate.y1)
        ctx.lineTo(gate.x2, gate.y2)
        ctx.stroke()
      }
    }
    ctx.setLineDash([])
    ctx.globalAlpha = 1

    // Systems: a constant size on screen, so drawn in screen space; one fill per filter colour group.
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
    const radius = this.systemRadius()
    const margin = this.systemMargin()
    const pad = radius + margin + 2

    // Zoomed in, clear a ring around each system so gates stop short of it: one batched cut-out of
    // the gate layer, cheaper than shortening every gate per frame.
    if (margin > 0) {
      ctx.globalCompositeOperation = 'destination-out'
      ctx.beginPath()
      for (const systems of this.byGroup) {
        for (const i of systems) {
          const sx = xy[i * 2] * scale + offsetX
          const sy = xy[i * 2 + 1] * scale + offsetY
          if (sx < -pad || sy < -pad || sx > this.width + pad || sy > this.height + pad) continue
          ctx.moveTo(sx + radius + margin, sy)
          ctx.arc(sx, sy, radius + margin, 0, Math.PI * 2)
        }
      }
      ctx.fill()
      ctx.globalCompositeOperation = 'source-over'
    }

    for (let group = 0; group < this.byGroup.length; group++) {
      const systems = this.byGroup[group]
      if (systems.length === 0) continue
      ctx.beginPath()
      for (const i of systems) {
        const sx = xy[i * 2] * scale + offsetX
        const sy = xy[i * 2 + 1] * scale + offsetY
        if (sx < -pad || sy < -pad || sx > this.width + pad || sy > this.height + pad) continue
        ctx.moveTo(sx + radius, sy)
        ctx.arc(sx, sy, radius, 0, Math.PI * 2)
      }
      ctx.fillStyle = palette.groups[group]
      ctx.fill()
    }

    if (this.hovered >= 0) {
      const sx = xy[this.hovered * 2] * scale + offsetX
      const sy = xy[this.hovered * 2 + 1] * scale + offsetY
      ctx.beginPath()
      ctx.arc(sx, sy, radius + 4, 0, Math.PI * 2)
      ctx.lineWidth = 1.5
      ctx.strokeStyle = palette.hover
      ctx.stroke()
    }

    if (scale >= LABEL_ZOOM) this.drawLabels(radius)
    else this.drawRegionLabels()
  }

  /**
   * Region names at the centre of their systems, when zoomed out too far for system names. Larger
   * regions are placed first; a name that would overlap one already drawn is skipped.
   */
  private drawRegionLabels(): void {
    const { ctx, scale, offsetX, offsetY, palette } = this
    ctx.font = this.regionFont
    ctx.letterSpacing = '2px'
    ctx.textAlign = 'center'
    ctx.textBaseline = 'middle'
    ctx.lineJoin = 'round'
    ctx.lineWidth = 3
    ctx.strokeStyle = palette.halo
    ctx.fillStyle = palette.region

    const taken = new Set<number>()
    const height = REGION_FONT_PX + 4
    for (const region of this.model.regions) {
      const centre = region.centre[this.layout]
      if (!centre) continue
      const x = centre.x * scale + offsetX
      const y = centre.y * scale + offsetY
      const text = region.name.toUpperCase()
      const width = ctx.measureText(text).width
      if (x + width / 2 < 0 || x - width / 2 > this.width || y < -height || y > this.height + height) continue

      const x0 = Math.floor((x - width / 2 - REGION_GAP_PX) / LABEL_CELL_PX)
      const x1 = Math.floor((x + width / 2 + REGION_GAP_PX) / LABEL_CELL_PX)
      const y0 = Math.floor((y - height / 2 - REGION_GAP_PX / 2) / LABEL_CELL_PX)
      const y1 = Math.floor((y + height / 2 + REGION_GAP_PX / 2) / LABEL_CELL_PX)
      let free = true
      for (let cx = x0; cx <= x1 && free; cx++)
        for (let cy = y0; cy <= y1; cy++)
          if (taken.has(cx * 100_000 + cy)) {
            free = false
            break
          }
      if (!free) continue
      for (let cx = x0; cx <= x1; cx++) for (let cy = y0; cy <= y1; cy++) taken.add(cx * 100_000 + cy)

      ctx.strokeText(text, x, y)
      ctx.fillText(text, x, y)
    }

    ctx.letterSpacing = '0px'
    ctx.textAlign = 'start'
  }

  /** System names to the bottom right of each system in view, skipping any that would overlap one already drawn. */
  private drawLabels(radius: number): void {
    const { ctx, xy, scale, offsetX, offsetY } = this
    ctx.font = this.labelFont
    ctx.textBaseline = 'top'
    const taken = new Set<number>()
    const lineHeight = LABEL_FONT_PX + 2

    const place = (i: number, color: string, force: boolean) => {
      const x = xy[i * 2] * scale + offsetX + radius + 2
      const y = xy[i * 2 + 1] * scale + offsetY + radius
      if (x > this.width || y > this.height || x < -200 || y < -lineHeight) return
      const width = this.labelWidth(i)
      const x0 = Math.floor(x / LABEL_CELL_PX)
      const x1 = Math.floor((x + width) / LABEL_CELL_PX)
      const y0 = Math.floor(y / LABEL_CELL_PX)
      const y1 = Math.floor((y + lineHeight) / LABEL_CELL_PX)
      if (!force) {
        for (let cx = x0; cx <= x1; cx++)
          for (let cy = y0; cy <= y1; cy++) if (taken.has(cx * 100_000 + cy)) return
      }
      for (let cx = x0; cx <= x1; cx++) for (let cy = y0; cy <= y1; cy++) taken.add(cx * 100_000 + cy)
      ctx.fillStyle = color
      ctx.fillText(this.model.names[i], x, y)
    }

    // The hovered system always gets its label first.
    if (this.hovered >= 0) place(this.hovered, this.palette.labelHover, true)
    for (let i = 0; i < this.model.count; i++) {
      if (i !== this.hovered && this.filter.groups[i] !== HIDDEN) place(i, this.palette.label, false)
    }
  }

  private labelWidth(i: number): number {
    let width = this.labelWidths.get(i)
    if (width === undefined) {
      width = this.ctx.measureText(this.model.names[i]).width
      this.labelWidths.set(i, width)
    }
    return width
  }

  private systemRadius(): number {
    return clamp(this.scale * SYSTEM_RADIUS.perZoom, SYSTEM_RADIUS.min, SYSTEM_RADIUS.max)
  }

  /** Gate width in CSS pixels: thin zoomed out, so the map is not a tangle of lines. */
  private gateWidth(): number {
    return clamp(GATE_WIDTH.base + this.scale * GATE_WIDTH.perZoom, GATE_WIDTH.min, GATE_WIDTH.max)
  }

  private systemMargin(): number {
    return clamp((this.scale - MARGIN_FROM_ZOOM) * SYSTEM_MARGIN.perZoom, 0, SYSTEM_MARGIN.max)
  }

  /** Rebuilt when the layout, the filter or the theme colours change. */
  private buildGates(): GateLayer {
    const { gates, regionIds, constellationIds } = this.model
    const { groups } = this.filter
    const plain = new Map<string, Path2D[]>()
    const gradients: GradientGate[][] = GATE_DASHES.map(() => [])

    for (let g = 0; g < gates.length; g += 2) {
      const from = gates[g]
      const to = gates[g + 1]
      const style = regionIds[from] !== regionIds[to] ? 2 : constellationIds[from] !== constellationIds[to] ? 1 : 0
      const x1 = this.xy[from * 2]
      const y1 = this.xy[from * 2 + 1]
      const x2 = this.xy[to * 2]
      const y2 = this.xy[to * 2 + 1]

      // A gate to a hidden system keeps the neutral gate colour.
      const hidden = groups[from] === HIDDEN || groups[to] === HIDDEN
      const fromColour = hidden ? this.palette.gate : this.palette.groups[groups[from]]
      const toColour = hidden ? this.palette.gate : this.palette.groups[groups[to]]

      if (fromColour === toColour) {
        let paths = plain.get(fromColour)
        if (!paths) plain.set(fromColour, (paths = GATE_DASHES.map(() => new Path2D())))
        paths[style].moveTo(x1, y1)
        paths[style].lineTo(x2, y2)
      } else {
        const gradient = this.ctx.createLinearGradient(x1, y1, x2, y2)
        gradient.addColorStop(0, fromColour)
        gradient.addColorStop(1, toColour)
        gradients[style].push({ x1, y1, x2, y2, gradient })
      }
    }

    return { plain: [...plain].map(([colour, paths]) => ({ colour, paths })), gradients }
  }

  private resize(): void {
    const rect = this.canvas.getBoundingClientRect()
    if (rect.width === 0 || rect.height === 0) return
    const centerX = (this.width / 2 - this.offsetX) / this.scale
    const centerY = (this.height / 2 - this.offsetY) / this.scale
    this.dpr = window.devicePixelRatio || 1
    this.width = rect.width
    this.height = rect.height
    this.canvas.width = Math.round(rect.width * this.dpr)
    this.canvas.height = Math.round(rect.height * this.dpr)
    if (!this.fitted) {
      this.fitted = true
      this.fit()
      return
    }
    // Keep the same point of the map in the middle.
    this.offsetX = this.width / 2 - centerX * this.scale
    this.offsetY = this.height / 2 - centerY * this.scale
    this.requestDraw()
  }

  // ---------- Input ----------

  private readonly onWheel = (e: WheelEvent) => {
    e.preventDefault()
    const step = e.deltaMode === WheelEvent.DOM_DELTA_LINE ? 0.05 : 0.0015
    this.zoomAt(e.offsetX, e.offsetY, Math.exp(-e.deltaY * step))
    this.updateHover(e.offsetX, e.offsetY)
  }

  private readonly onPointerDown = (e: PointerEvent) => {
    this.canvas.setPointerCapture(e.pointerId)
    this.pointers.set(e.pointerId, { x: e.offsetX, y: e.offsetY })
    this.pinchDistance = this.pointers.size === 2 ? this.pointerDistance() : 0
  }

  private readonly onPointerMove = (e: PointerEvent) => {
    const previous = this.pointers.get(e.pointerId)
    if (!previous) {
      this.updateHover(e.offsetX, e.offsetY)
      return
    }

    this.pointers.set(e.pointerId, { x: e.offsetX, y: e.offsetY })
    if (this.pointers.size === 2) {
      const distance = this.pointerDistance()
      const [a, b] = [...this.pointers.values()]
      if (this.pinchDistance > 0) this.zoomAt((a.x + b.x) / 2, (a.y + b.y) / 2, distance / this.pinchDistance)
      this.pinchDistance = distance
      return
    }

    this.offsetX += e.offsetX - previous.x
    this.offsetY += e.offsetY - previous.y
    this.setHovered(-1)
    this.requestDraw()
  }

  private readonly onPointerUp = (e: PointerEvent) => {
    this.pointers.delete(e.pointerId)
    this.pinchDistance = 0
    if (e.pointerType === 'mouse') this.updateHover(e.offsetX, e.offsetY)
  }

  private readonly onPointerLeave = () => {
    if (this.pointers.size === 0) this.setHovered(-1)
  }

  private pointerDistance(): number {
    const [a, b] = [...this.pointers.values()]
    return Math.hypot(a.x - b.x, a.y - b.y)
  }

  private updateHover(x: number, y: number): void {
    const worldX = (x - this.offsetX) / this.scale
    const worldY = (y - this.offsetY) / this.scale
    const nearest = this.grid.nearest(worldX, worldY, HOVER_RADIUS_PX / this.scale)
    this.setHovered(nearest >= 0 && this.filter.groups[nearest] !== HIDDEN ? nearest : -1)
  }

  private setHovered(index: number): void {
    if (index === this.hovered) {
      if (index >= 0) this.emitHover()
      return
    }
    this.hovered = index
    this.emitHover()
    this.requestDraw()
  }

  private emitHover(): void {
    if (!this.onHover) return
    if (this.hovered < 0) {
      this.onHover(null)
      return
    }
    this.onHover({
      index: this.hovered,
      x: this.xy[this.hovered * 2] * this.scale + this.offsetX,
      y: this.xy[this.hovered * 2 + 1] * this.scale + this.offsetY,
    })
  }
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max)
}

function groupSystems(filter: MapFilter): Uint32Array[] {
  const lists: number[][] = filter.colours.map(() => [])
  filter.groups.forEach((group, i) => {
    if (group !== HIDDEN) lists[group].push(i)
  })
  return lists.map((list) => Uint32Array.from(list))
}

function resolvePalette(filter: MapFilter): Palette {
  return {
    gate: resolveColor('var(--line-strong)'),
    halo: resolveColor('var(--bg)'),
    region: resolveColor('var(--fg)'),
    label: resolveColor('var(--fg-muted)'),
    labelHover: resolveColor('var(--fg)'),
    hover: resolveColor('var(--primary)'),
    groups: filter.colours.map(resolveColor),
  }
}

let probe: { element: HTMLElement; ctx: CanvasRenderingContext2D } | null = null

/**
 * Resolves a CSS colour (the theme tokens are color-mix() over variables, which Canvas cannot read)
 * to rgba() by letting the browser compute it and reading the pixel back.
 */
function resolveColor(css: string): string {
  if (!probe) {
    const element = document.createElement('span')
    element.style.display = 'none'
    document.body.append(element)
    const canvas = document.createElement('canvas')
    canvas.width = canvas.height = 1
    probe = { element, ctx: canvas.getContext('2d', { willReadFrequently: true })! }
  }
  probe.element.style.color = css
  const computed = getComputedStyle(probe.element).color
  probe.ctx.clearRect(0, 0, 1, 1)
  probe.ctx.fillStyle = computed
  probe.ctx.fillRect(0, 0, 1, 1)
  const [r, g, b, a] = probe.ctx.getImageData(0, 0, 1, 1).data
  return `rgba(${r}, ${g}, ${b}, ${(a / 255).toFixed(3)})`
}
