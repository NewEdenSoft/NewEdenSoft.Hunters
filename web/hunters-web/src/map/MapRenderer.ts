import { MARKER_ICONS, type CharacterMarker } from '../characters/markers'
import { HIDDEN, type MapFilter } from './filters'
import type { HuntingBadgeGroup, HuntingLayer } from './hunting'
import { GridIndex } from './gridIndex'
import type { MapLayout, MapModel } from './model'

/** A hovered system and where it is on screen (CSS pixels, relative to the canvas). */
export interface MapHover {
  index: number
  x: number
  y: number
  /** The canvas width, so a panel beside the system can stay on screen. */
  areaWidth: number
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

/** Marker badges (CSS pixels): size, space between them, how far the leader line reaches up and right, the corner cut. */
const MARKER_SIZE = 18
const MARKER_GAP = 3
const MARKER_LEAD = 12
const MARKER_CUT = 4
const MARKER_FONT_PX = 11
/** Hunting profile badges show from this zoom (pixels per light year); further out, the glow alone does. */
const HUNT_BADGE_ZOOM = 50
/** Hunting profile badges: smaller than the character markers, in a row below the system's name. */
const HUNT_BADGE = 17
const HUNT_GAP = 3
const HUNT_CUT = 4
const HUNT_FONT_PX = 10
/** Faint badges (seen there, but not at this hour) are drawn at this opacity. */
const HUNT_FAINT_ALPHA = 0.4

/** Offline characters' markers are drawn at this opacity. */
const OFFLINE_ALPHA = 0.45

/** Focusing zooms in at least this far (pixels per light year), so system names show. */
export const FOCUS_SCALE = 60
const FOCUS_MS = 350
/** A second click on the same system within this is a double click (mouse and touch alike). */
const DOUBLE_CLICK_MS = 350
/** A press that moves less than this is a click, not a drag. */
const CLICK_SLOP_PX = 4

/** Holding a finger this long on a system opens its menu (touch has no right click). */
const LONG_PRESS_MS = 500

/** One character's marker on the map; offline characters are drawn dimmed at their last known system. */
export interface MapMarker {
  characterId: number
  name: string
  marker: CharacterMarker
  online: boolean
}

/** A system's menu was asked for (right click, or a long press): the system and where, in CSS pixels relative to the canvas. */
export interface MapContextMenuRequest {
  index: number
  x: number
  y: number
}

/** The markers of the characters in one system, drawn side by side at the end of one leader line. */
export interface MapMarkerGroup {
  systemIndex: number
  markers: MapMarker[]
}

interface Box {
  left: number
  top: number
  right: number
  bottom: number
}

/** One group's leader line (from the system's edge to the anchor) and its row of badges, in CSS pixels. */
interface MarkerRow {
  group: MapMarkerGroup
  lineX: number
  lineY: number
  anchorX: number
  anchorY: number
  bounds: Box
}

/** One system's hunting badge row, in CSS pixels. */
interface HuntRow {
  group: HuntingBadgeGroup
  sx: number
  sy: number
  bounds: Box
}

interface FocusAnimation {
  fromScale: number
  fromX: number
  fromY: number
  toScale: number
  toX: number
  toY: number
  start: number | null
}

/** Marker icons, parsed once. */
const iconPaths = new Map<string, Path2D>()

interface Palette {
  gate: string
  halo: string
  region: string
  label: string
  labelHover: string
  /** The hover ring and the character markers. */
  hover: string
  /** The focused system's rings and label. */
  focused: string
  /** The selected system's ring and label. */
  selection: string
  /** The glow behind systems where a shown hunting profile is likely now. */
  huntGlow: string
  /** The blue behind systems a jump drive can reach from the focused system. */
  jumpRange: string
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
 * skipping any that would overlap; last, the characters' markers, joined by a line to the upper
 * right of their systems. Frames are drawn only when something changes. {@link focusOn} moves the
 * view to a system with a short animation.
 */
export class MapRenderer {
  onHover: ((hover: MapHover | null) => void) | null = null
  /** A character's marker was clicked. */
  onMarkerClick: ((characterId: number) => void) | null = null
  /** A click (or tap) on a system, or on empty space (-1): selects it, or clears the selection. */
  onSystemClick: ((index: number) => void) | null = null
  /** A second click on the same system within {@link DOUBLE_CLICK_MS}: focuses it, when enabled. */
  onSystemDoubleClick: ((index: number) => void) | null = null
  /** The user dragged the map (for example, to stop following a character). */
  onUserPan: (() => void) | null = null
  /** The user asked for a system's menu: right click, or a long press on touch. */
  onContextMenu: ((request: MapContextMenuRequest) => void) | null = null
  /** The user moved the view in any way (drag, zoom, pinch): menus over the map close. */
  onUserMove: (() => void) | null = null

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
  private markerFont = ''
  private markerGroups: MapMarkerGroup[] = []
  private hunting: HuntingLayer | null = null
  /** 1 per system a jump drive can reach from the focused system, or null when the range isn't shown. */
  private jumpRange: Uint8Array | null = null
  private huntFont = ''
  /** Resolved badge colours, by CSS colour. */
  private readonly huntColours = new Map<string, string>()

  private dpr = 1
  private width = 0
  private height = 0
  private scale = 1
  private fitScale = 1
  private offsetX = 0
  private offsetY = 0
  private fitted = false
  private hovered = -1
  /** The focused system (the staging point): always drawn, double-ringed and labelled. */
  private focused = -1
  /** The selected system (the one clicked): always drawn, ringed and labelled. */
  private selection = -1
  /** The last click on a system, to tell a double click (also on touch, which has no dblclick). */
  private lastClick: { index: number; at: number } | null = null
  private longPressTimer = 0
  /** When a long press last opened a menu, so the browser's own long-press menu event is ignored. */
  private longPressedAt = 0
  private frame = 0
  private pinchDistance = 0
  private pressedAt: { x: number; y: number } | null = null
  private panned = false
  private animation: FocusAnimation | null = null
  private animationFrame = 0
  private pendingFocus: { systemIndex: number; zoomIn: boolean } | null = null

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
    this.markerFont = `600 ${MARKER_FONT_PX}px ${displayFont || getComputedStyle(document.body).fontFamily}`
    this.huntFont = `700 ${HUNT_FONT_PX}px ${displayFont || getComputedStyle(document.body).fontFamily}`

    canvas.addEventListener('wheel', this.onWheel, { passive: false })
    canvas.addEventListener('pointerdown', this.onPointerDown)
    canvas.addEventListener('pointermove', this.onPointerMove)
    canvas.addEventListener('pointerup', this.onPointerUp)
    canvas.addEventListener('pointercancel', this.onPointerUp)
    canvas.addEventListener('pointerleave', this.onPointerLeave)
    canvas.addEventListener('contextmenu', this.onContextMenuEvent)

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

  /** The focused system (-1 for none): drawn even if the filter hides it, with a double ring and its name. */
  setFocused(index: number): void {
    if (index === this.focused) return
    this.focused = index
    this.requestDraw()
  }

  /** The selected system (-1 for none): drawn even if the filter hides it, with a ring and its name. */
  setSelection(index: number): void {
    if (index === this.selection) return
    this.selection = index
    this.requestDraw()
  }

  /** The systems in jump range (1 each, see map/jumpRange.ts), or null to show none. Changing it only redraws. */
  setJumpRange(inRange: Uint8Array | null): void {
    this.jumpRange = inRange
    this.requestDraw()
  }

  /** The hunting profiles shown on the map (glow and badges), or null for none. Changing it only redraws. */
  setHunting(layer: HuntingLayer | null): void {
    this.hunting = layer
    this.requestDraw()
  }

  /** The characters to mark on the map. Changing them only redraws. */
  setMarkers(groups: MapMarkerGroup[]): void {
    this.markerGroups = groups
    this.requestDraw()
  }

  /**
   * Moves the view to a system with a short animation, zooming in to at least {@link FOCUS_SCALE}
   * when `zoomIn` (otherwise keeping the zoom). Before the canvas has a size, the focus waits and is
   * applied without animation.
   */
  focusOn(systemIndex: number, zoomIn: boolean): void {
    if (!this.fitted) {
      this.pendingFocus = { systemIndex, zoomIn }
      return
    }
    this.animation = {
      fromScale: this.scale,
      fromX: (this.width / 2 - this.offsetX) / this.scale,
      fromY: (this.height / 2 - this.offsetY) / this.scale,
      toScale: zoomIn ? this.clampScale(Math.max(this.scale, FOCUS_SCALE)) : this.scale,
      toX: this.xy[systemIndex * 2],
      toY: this.xy[systemIndex * 2 + 1],
      start: null,
    }
    this.setHovered(-1)
    cancelAnimationFrame(this.animationFrame)
    this.animationFrame = requestAnimationFrame(this.animate)
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
    this.animation = null
    const scale = this.clampScale(this.scale * factor)
    const applied = scale / this.scale
    this.offsetX = x - (x - this.offsetX) * applied
    this.offsetY = y - (y - this.offsetY) * applied
    this.scale = scale
    this.requestDraw()
  }

  destroy(): void {
    cancelAnimationFrame(this.frame)
    cancelAnimationFrame(this.animationFrame)
    this.resizeObserver.disconnect()
    this.themeObserver.disconnect()
    this.canvas.removeEventListener('wheel', this.onWheel)
    this.canvas.removeEventListener('pointerdown', this.onPointerDown)
    this.canvas.removeEventListener('pointermove', this.onPointerMove)
    this.canvas.removeEventListener('pointerup', this.onPointerUp)
    this.canvas.removeEventListener('pointercancel', this.onPointerUp)
    this.canvas.removeEventListener('pointerleave', this.onPointerLeave)
    this.canvas.removeEventListener('contextmenu', this.onContextMenuEvent)
    clearTimeout(this.longPressTimer)
    this.onHover = null
    this.onMarkerClick = null
    this.onUserPan = null
    this.onContextMenu = null
    this.onSystemClick = null
    this.onSystemDoubleClick = null
    this.onUserMove = null
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
    // Some filters draw groups larger (busier activity), so the size reads without the colour.
    const sizes = this.filter.sizes
    const pad = radius * Math.max(1, ...(sizes ?? [])) + margin + 2

    // Zoomed in, clear a ring around each system so gates stop short of it: one batched cut-out of
    // the gate layer, cheaper than shortening every gate per frame.
    if (margin > 0) {
      ctx.globalCompositeOperation = 'destination-out'
      ctx.beginPath()
      this.byGroup.forEach((systems, group) => {
        const r = radius * (sizes?.[group] ?? 1) + margin
        for (const i of systems) {
          const sx = xy[i * 2] * scale + offsetX
          const sy = xy[i * 2 + 1] * scale + offsetY
          if (sx < -pad || sy < -pad || sx > this.width + pad || sy > this.height + pad) continue
          ctx.moveTo(sx + r, sy)
          ctx.arc(sx, sy, r, 0, Math.PI * 2)
        }
      })
      ctx.fill()
      ctx.globalCompositeOperation = 'source-over'
    }

    if (this.jumpRange) this.drawJumpRange(radius, pad)
    if (this.hunting) this.drawHuntGlow(radius, pad)

    for (let group = 0; group < this.byGroup.length; group++) {
      const systems = this.byGroup[group]
      if (systems.length === 0) continue
      const r = radius * (sizes?.[group] ?? 1)
      ctx.beginPath()
      for (const i of systems) {
        const sx = xy[i * 2] * scale + offsetX
        const sy = xy[i * 2 + 1] * scale + offsetY
        if (sx < -pad || sy < -pad || sx > this.width + pad || sy > this.height + pad) continue
        ctx.moveTo(sx + r, sy)
        ctx.arc(sx, sy, r, 0, Math.PI * 2)
      }
      ctx.fillStyle = palette.groups[group]
      ctx.fill()
    }

    if (this.hovered >= 0) {
      const sx = xy[this.hovered * 2] * scale + offsetX
      const sy = xy[this.hovered * 2 + 1] * scale + offsetY
      ctx.beginPath()
      ctx.arc(sx, sy, this.radiusOf(this.hovered, radius) + 4, 0, Math.PI * 2)
      ctx.lineWidth = 1.5
      ctx.strokeStyle = palette.hover
      ctx.stroke()
    }

    if (this.focused >= 0) this.drawFocused(this.radiusOf(this.focused, radius))
    if (this.selection >= 0 && this.selection !== this.focused) this.drawSelection(this.radiusOf(this.selection, radius))

    if (scale >= LABEL_ZOOM) {
      // Badge rows first, so names can keep clear of them.
      const huntRows = this.hunting && this.hunting.badges.length > 0 && scale >= HUNT_BADGE_ZOOM ? this.layoutHuntRows(radius) : []
      this.drawLabels(radius, huntRows.map((r) => r.bounds), huntRows.map((r) => r.group.systemIndex))
      if (huntRows.length > 0) this.drawHuntBadges(radius, huntRows)
    } else {
      this.drawRegionLabels()
      // The focused and selected systems keep their names when zoomed out, where no other system has one.
      if (this.focused >= 0) this.drawFocusedLabel(radius)
      if (this.selection >= 0 && this.selection !== this.focused) this.drawSelectionLabel(radius)
    }

    if (this.markerGroups.length > 0) this.drawMarkers()
  }

  /**
   * Each group: a leader line from the system's edge up and to the right, ending at the bottom-left
   * corner of a row of badges. Drawn at every zoom, over everything else.
   */
  private drawMarkers(): void {
    const { ctx, palette } = this
    ctx.font = this.markerFont
    ctx.textAlign = 'center'
    ctx.textBaseline = 'middle'
    ctx.lineJoin = 'miter'

    for (const row of this.layoutMarkers()) {
      ctx.globalAlpha = row.group.markers.some((m) => m.online) ? 1 : OFFLINE_ALPHA
      ctx.strokeStyle = palette.hover
      ctx.lineWidth = 1
      ctx.beginPath()
      ctx.moveTo(row.lineX, row.lineY)
      ctx.lineTo(row.anchorX, row.anchorY)
      ctx.stroke()

      row.group.markers.forEach((m, k) => {
        const badge = markerBadge(row, k)
        const shape = chamfer(badge.left, badge.top, MARKER_SIZE, MARKER_CUT)
        ctx.globalAlpha = 1
        ctx.fillStyle = palette.halo
        ctx.fill(shape)
        ctx.globalAlpha = m.online ? 1 : OFFLINE_ALPHA
        ctx.lineWidth = 1
        ctx.stroke(shape)

        const icon = m.marker.kind === 'icon' ? MARKER_ICONS[m.marker.value] : undefined
        if (icon) {
          // Icons are 2-unit strokes in a 24 x 24 box, drawn at 60% of the badge.
          const size = MARKER_SIZE * 0.6
          ctx.save()
          ctx.translate(badge.left + (MARKER_SIZE - size) / 2, badge.top + (MARKER_SIZE - size) / 2)
          ctx.scale(size / 24, size / 24)
          ctx.lineWidth = 2.4
          ctx.lineCap = 'round'
          ctx.lineJoin = 'round'
          ctx.stroke(iconPath(icon))
          ctx.restore()
        } else {
          ctx.fillStyle = palette.hover
          ctx.fillText(m.marker.value, badge.left + MARKER_SIZE / 2, badge.top + MARKER_SIZE / 2 + 0.5)
        }
      })
    }

    ctx.globalAlpha = 1
    ctx.textAlign = 'start'
  }

  /**
   * Where each group's line and badges go, in drawing order. Shared by drawing and clicking, so a
   * click finds the badge that was drawn. Groups out of view are left out, and a row that would
   * overlap one already placed (neighbouring systems, zoomed out) steps further up and to the right.
   */
  private layoutMarkers(): MarkerRow[] {
    const { xy, scale, offsetX, offsetY } = this
    const radius = this.systemRadius()
    const edge = radius * Math.SQRT1_2
    const rows: MarkerRow[] = []
    for (const group of this.markerGroups) {
      const sx = xy[group.systemIndex * 2] * scale + offsetX
      const sy = xy[group.systemIndex * 2 + 1] * scale + offsetY
      let anchorX = sx + radius + MARKER_LEAD
      let anchorY = sy - radius - MARKER_LEAD
      const rowWidth = group.markers.length * (MARKER_SIZE + MARKER_GAP) - MARKER_GAP
      if (sx > this.width || anchorX + rowWidth < 0 || anchorY - MARKER_SIZE > this.height || sy < 0) continue

      let bounds: Box = { left: anchorX, top: anchorY - MARKER_SIZE, right: anchorX + rowWidth, bottom: anchorY }
      for (let step = 0; step < 8 && rows.some((r) => overlap(r.bounds, bounds)); step++) {
        const lift = MARKER_SIZE + MARKER_GAP
        anchorX += lift
        anchorY -= lift
        bounds = { left: bounds.left + lift, top: bounds.top - lift, right: bounds.right + lift, bottom: bounds.bottom - lift }
      }
      rows.push({ group, lineX: sx + edge, lineY: sy - edge, anchorX, anchorY, bounds })
    }
    return rows
  }

  /** The character whose marker badge is under a point (CSS pixels), or null. Rows drawn later are on top. */
  markerAt(x: number, y: number): number | null {
    const rows = this.layoutMarkers()
    for (let r = rows.length - 1; r >= 0; r--) {
      for (let k = 0; k < rows[r].group.markers.length; k++) {
        const b = markerBadge(rows[r], k)
        if (x >= b.left && x <= b.left + MARKER_SIZE && y >= b.top && y <= b.top + MARKER_SIZE) return rows[r].group.markers[k].characterId
      }
    }
    return null
  }

  private clampScale(scale: number): number {
    return Math.min(Math.max(scale, this.fitScale * 0.5), this.fitScale * 80)
  }

  /** Puts a world point in the middle of the canvas at a zoom. */
  private centreOn(worldX: number, worldY: number, scale: number): void {
    this.scale = scale
    this.offsetX = this.width / 2 - worldX * scale
    this.offsetY = this.height / 2 - worldY * scale
  }

  private readonly animate = (time: number) => {
    const a = this.animation
    if (!a) return
    a.start ??= time
    const progress = Math.min((time - a.start) / FOCUS_MS, 1)
    const t = 1 - (1 - progress) ** 3
    // The zoom changes geometrically, so zooming in feels even; the centre moves in a straight line.
    this.centreOn(a.fromX + (a.toX - a.fromX) * t, a.fromY + (a.toY - a.fromY) * t, a.fromScale * (a.toScale / a.fromScale) ** t)
    this.draw()
    if (progress < 1) this.animationFrame = requestAnimationFrame(this.animate)
    else this.animation = null
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
  private drawLabels(radius: number, reserved: Box[] = [], first: number[] = []): void {
    const { ctx, xy, scale, offsetX, offsetY } = this
    ctx.font = this.labelFont
    ctx.textBaseline = 'top'
    const taken = new Set<number>()
    const lineHeight = LABEL_FONT_PX + 2
    // Space other things are drawn in (hunting badges): no name goes there. The names of the systems
    // those belong to are placed before it is taken, after the hovered and focused systems'.
    const placeFirst = () => {
      for (const i of first) if (i !== this.hovered && i !== this.focused && i !== this.selection) place(i, this.palette.label, false)
    }
    const reserve = () => {
      for (const box of reserved) {
        for (let cx = Math.floor(box.left / LABEL_CELL_PX); cx <= Math.floor(box.right / LABEL_CELL_PX); cx++)
          for (let cy = Math.floor(box.top / LABEL_CELL_PX); cy <= Math.floor(box.bottom / LABEL_CELL_PX); cy++) taken.add(cx * 100_000 + cy)
      }
    }

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

    // The hovered, focused and selected systems always get their labels first.
    if (this.hovered >= 0) place(this.hovered, this.palette.labelHover, true)
    if (this.focused >= 0 && this.focused !== this.hovered) place(this.focused, this.palette.focused, true)
    if (this.selection >= 0 && this.selection !== this.hovered && this.selection !== this.focused) place(this.selection, this.palette.selection, true)
    placeFirst()
    reserve()
    const done = new Set(first)
    for (let i = 0; i < this.model.count; i++) {
      if (i !== this.hovered && i !== this.focused && i !== this.selection && !done.has(i) && this.filter.groups[i] !== HIDDEN)
        place(i, this.palette.label, false)
    }
  }

  /**
   * A cool blue behind each system a jump drive can reach from the focused system: a soft disc, so a
   * range reads as one blue area zoomed out and as marked systems zoomed in. Under the hunting glow.
   */
  private drawJumpRange(radius: number, pad: number): void {
    const { ctx, xy, scale, offsetX, offsetY, palette } = this
    const inRange = this.jumpRange!
    const reach = Math.max(radius * 3, 9)
    const inner = withAlpha(palette.jumpRange, 0.8)
    const outer = withAlpha(palette.jumpRange, 0)
    for (let i = 0; i < inRange.length; i++) {
      if (!inRange[i]) continue
      const sx = xy[i * 2] * scale + offsetX
      const sy = xy[i * 2 + 1] * scale + offsetY
      if (sx < -reach - pad || sy < -reach - pad || sx > this.width + reach + pad || sy > this.height + reach + pad) continue
      const gradient = ctx.createRadialGradient(sx, sy, 0, sx, sy, reach)
      gradient.addColorStop(0, inner)
      gradient.addColorStop(0.5, withAlpha(palette.jumpRange, 0.45))
      gradient.addColorStop(1, outer)
      ctx.fillStyle = gradient
      ctx.beginPath()
      ctx.arc(sx, sy, reach, 0, Math.PI * 2)
      ctx.fill()
    }
  }

  /**
   * A soft glow behind each system where a shown hunting profile is likely now: brighter and wider the
   * stronger it is. Under the system dots, over the gates.
   */
  private drawHuntGlow(radius: number, pad: number): void {
    const { ctx, xy, scale, offsetX, offsetY, palette } = this
    const glow = this.hunting!.glow
    const transparent = withAlpha(palette.huntGlow, 0)
    for (let i = 0; i < glow.length; i++) {
      const g = glow[i]
      if (g <= 0) continue
      const sx = xy[i * 2] * scale + offsetX
      const sy = xy[i * 2 + 1] * scale + offsetY
      const reach = Math.max(radius * 3.2, 9) * (0.7 + 0.5 * g)
      if (sx < -reach - pad || sy < -reach - pad || sx > this.width + reach + pad || sy > this.height + reach + pad) continue
      const gradient = ctx.createRadialGradient(sx, sy, 0, sx, sy, reach)
      gradient.addColorStop(0, withAlpha(palette.huntGlow, 0.25 + 0.6 * g))
      gradient.addColorStop(1, transparent)
      ctx.fillStyle = gradient
      ctx.beginPath()
      ctx.arc(sx, sy, reach, 0, Math.PI * 2)
      ctx.fill()
    }
  }

  /**
   * The shown hunting profiles seen at each system, as small badges in each profile's colour, in a row
   * below the system's name with a short line from the system. Faint where the profile is not usually
   * there at this hour. Drawn only when names are.
   */
  private drawHuntBadges(radius: number, rows: HuntRow[]): void {
    const { ctx, palette } = this
    const edge = radius * Math.SQRT1_2
    ctx.font = this.huntFont
    ctx.textAlign = 'center'
    ctx.textBaseline = 'middle'
    ctx.lineWidth = 1
    for (const { group, sx, sy, bounds } of rows) {
      const { left, top } = bounds

      ctx.globalAlpha = group.badges.some((b) => !b.faint) ? 1 : HUNT_FAINT_ALPHA
      ctx.strokeStyle = this.huntColour(group.badges[0].colour)
      ctx.beginPath()
      ctx.moveTo(sx + edge, sy + edge)
      ctx.lineTo(left, top + HUNT_BADGE / 2)
      ctx.stroke()

      group.badges.forEach((badge, k) => {
        const x = left + k * (HUNT_BADGE + HUNT_GAP)
        const shape = chamfer(x, top, HUNT_BADGE, HUNT_CUT)
        const colour = this.huntColour(badge.colour)
        ctx.globalAlpha = 1
        ctx.fillStyle = palette.halo
        ctx.fill(shape)
        ctx.globalAlpha = badge.faint ? HUNT_FAINT_ALPHA : 1
        ctx.strokeStyle = colour
        ctx.stroke(shape)
        if (badge.icon) {
          // Icons are strokes in a 24 x 24 box, drawn at 85% of the badge.
          const size = HUNT_BADGE * 0.85
          ctx.save()
          ctx.translate(x + (HUNT_BADGE - size) / 2, top + (HUNT_BADGE - size) / 2)
          ctx.scale(size / 24, size / 24)
          ctx.lineWidth = 1.9
          ctx.lineCap = 'round'
          ctx.lineJoin = 'round'
          ctx.stroke(iconPath(badge.icon))
          ctx.restore()
        } else {
          ctx.fillStyle = colour
          ctx.fillText(badge.code, x + HUNT_BADGE / 2, top + HUNT_BADGE / 2 + 0.5)
        }
      })
    }
    ctx.globalAlpha = 1
    ctx.textAlign = 'start'
  }

  /**
   * Where each system's badge row goes, below its name: rows out of view are left out, and a row that
   * would overlap one already placed (neighbouring systems) is left out too.
   */
  private layoutHuntRows(radius: number): HuntRow[] {
    const { xy, scale, offsetX, offsetY } = this
    const rows: HuntRow[] = []
    for (const group of this.hunting!.badges) {
      const sx = xy[group.systemIndex * 2] * scale + offsetX
      const sy = xy[group.systemIndex * 2 + 1] * scale + offsetY
      const left = sx + radius + 4
      const top = sy + radius + LABEL_FONT_PX + 5
      const bounds: Box = { left, top, right: left + group.badges.length * (HUNT_BADGE + HUNT_GAP) - HUNT_GAP, bottom: top + HUNT_BADGE }
      if (bounds.left > this.width || bounds.right < 0 || bounds.top > this.height || bounds.bottom < 0) continue
      if (rows.some((r) => overlap(r.bounds, bounds))) continue
      rows.push({ group, sx, sy, bounds })
    }
    return rows
  }

  private huntColour(css: string): string {
    let colour = this.huntColours.get(css)
    if (colour === undefined) this.huntColours.set(css, (colour = resolveColor(css)))
    return colour
  }

  /** A system's drawn radius: the filter may draw its group larger. */
  private radiusOf(i: number, radius: number): number {
    const group = this.filter.groups[i]
    return group === HIDDEN ? radius : radius * (this.filter.sizes?.[group] ?? 1)
  }

  /** Two rings around the focused system; its dot too if the filter hides it. */
  private drawFocused(radius: number): void {
    const { ctx, xy, scale, offsetX, offsetY, palette } = this
    const sx = xy[this.focused * 2] * scale + offsetX
    const sy = xy[this.focused * 2 + 1] * scale + offsetY
    if (this.filter.groups[this.focused] === HIDDEN) {
      ctx.beginPath()
      ctx.arc(sx, sy, radius, 0, Math.PI * 2)
      ctx.fillStyle = palette.focused
      ctx.fill()
    }
    ctx.strokeStyle = palette.focused
    ctx.lineWidth = 2
    ctx.beginPath()
    ctx.arc(sx, sy, radius + 5, 0, Math.PI * 2)
    ctx.stroke()
    ctx.lineWidth = 1
    ctx.beginPath()
    ctx.arc(sx, sy, radius + 9, 0, Math.PI * 2)
    ctx.stroke()
  }

  /** The focused system's name when zoomed out, to the bottom right of its rings. */
  private drawFocusedLabel(radius: number): void {
    const { ctx, xy, scale, offsetX, offsetY } = this
    ctx.font = this.labelFont
    ctx.textBaseline = 'top'
    ctx.fillStyle = this.palette.focused
    ctx.fillText(
      this.model.names[this.focused],
      xy[this.focused * 2] * scale + offsetX + radius + 10,
      xy[this.focused * 2 + 1] * scale + offsetY + radius + 4,
    )
  }

  /**
   * The selected system: one ring with four ticks around it, like a crosshair, in the text colour, so it
   * reads apart from the focus's double ring. Its dot too if the filter hides it.
   */
  private drawSelection(radius: number): void {
    const { ctx, xy, scale, offsetX, offsetY, palette } = this
    const sx = xy[this.selection * 2] * scale + offsetX
    const sy = xy[this.selection * 2 + 1] * scale + offsetY
    if (this.filter.groups[this.selection] === HIDDEN) {
      ctx.beginPath()
      ctx.arc(sx, sy, radius, 0, Math.PI * 2)
      ctx.fillStyle = palette.selection
      ctx.fill()
    }
    ctx.strokeStyle = palette.selection
    ctx.lineWidth = 1.5
    ctx.beginPath()
    ctx.arc(sx, sy, radius + 5, 0, Math.PI * 2)
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
      ctx.moveTo(sx + dx * (radius + 8), sy + dy * (radius + 8))
      ctx.lineTo(sx + dx * (radius + 13), sy + dy * (radius + 13))
    }
    ctx.stroke()
  }

  /** The selected system's name when zoomed out, to the bottom right of its ring. */
  private drawSelectionLabel(radius: number): void {
    const { ctx, xy, scale, offsetX, offsetY } = this
    ctx.font = this.labelFont
    ctx.textBaseline = 'top'
    ctx.fillStyle = this.palette.selection
    ctx.fillText(
      this.model.names[this.selection],
      xy[this.selection * 2] * scale + offsetX + radius + 10,
      xy[this.selection * 2 + 1] * scale + offsetY + radius + 4,
    )
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
      // A focus asked for before the canvas had a size: jump there.
      if (this.pendingFocus) {
        const { systemIndex, zoomIn } = this.pendingFocus
        this.pendingFocus = null
        this.centreOn(this.xy[systemIndex * 2], this.xy[systemIndex * 2 + 1], zoomIn ? this.clampScale(Math.max(this.scale, FOCUS_SCALE)) : this.scale)
      }
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
    this.animation = null
    const step = e.deltaMode === WheelEvent.DOM_DELTA_LINE ? 0.05 : 0.0015
    this.zoomAt(e.offsetX, e.offsetY, Math.exp(-e.deltaY * step))
    this.updateHover(e.offsetX, e.offsetY)
    this.onUserMove?.()
  }

  private readonly onPointerDown = (e: PointerEvent) => {
    // Only the main button pans and clicks; the right button opens a system's menu (onContextMenuEvent).
    if (e.pointerType === 'mouse' && e.button !== 0) return
    this.canvas.setPointerCapture(e.pointerId)
    this.pointers.set(e.pointerId, { x: e.offsetX, y: e.offsetY })
    this.pinchDistance = this.pointers.size === 2 ? this.pointerDistance() : 0
    this.pressedAt = this.pointers.size === 1 ? { x: e.offsetX, y: e.offsetY } : null
    this.panned = false

    // Touch has no right click: holding a finger still on a system opens its menu.
    clearTimeout(this.longPressTimer)
    if (e.pointerType !== 'mouse' && this.pressedAt) {
      const { x, y } = this.pressedAt
      this.longPressTimer = window.setTimeout(() => {
        if (this.pointers.size !== 1 || this.panned) return
        const index = this.systemAt(x, y)
        if (index < 0) return
        this.longPressedAt = performance.now()
        this.pressedAt = null // So lifting the finger is not a tap.
        this.onContextMenu?.({ index, x, y })
      }, LONG_PRESS_MS)
    }
  }

  private readonly onPointerMove = (e: PointerEvent) => {
    const previous = this.pointers.get(e.pointerId)
    if (!previous) {
      this.updateHover(e.offsetX, e.offsetY)
      this.canvas.style.cursor = this.markerAt(e.offsetX, e.offsetY) === null ? '' : 'pointer'
      return
    }

    this.pointers.set(e.pointerId, { x: e.offsetX, y: e.offsetY })
    if (this.pointers.size === 2) {
      const distance = this.pointerDistance()
      const [a, b] = [...this.pointers.values()]
      clearTimeout(this.longPressTimer)
      if (this.pinchDistance > 0) this.zoomAt((a.x + b.x) / 2, (a.y + b.y) / 2, distance / this.pinchDistance)
      this.pinchDistance = distance
      this.onUserMove?.()
      return
    }

    // Small movements while pressing are still a click.
    if (!this.panned) {
      const start = this.pressedAt ?? previous
      if (Math.abs(e.offsetX - start.x) < CLICK_SLOP_PX && Math.abs(e.offsetY - start.y) < CLICK_SLOP_PX) {
        this.pointers.set(e.pointerId, previous)
        return
      }
      this.panned = true
      this.animation = null
      clearTimeout(this.longPressTimer)
      this.onUserPan?.()
    }
    this.onUserMove?.()

    this.offsetX += e.offsetX - previous.x
    this.offsetY += e.offsetY - previous.y
    this.setHovered(-1)
    this.requestDraw()
  }

  private readonly onPointerUp = (e: PointerEvent) => {
    clearTimeout(this.longPressTimer)
    const click = this.pointers.size === 1 && !this.panned && this.pressedAt !== null
    this.pointers.delete(e.pointerId)
    this.pinchDistance = 0
    this.pressedAt = null
    if (click && e.type === 'pointerup') {
      const characterId = this.markerAt(e.offsetX, e.offsetY)
      if (characterId !== null) this.onMarkerClick?.(characterId)
      else {
        // Touch has no hover: a tap shows the system under the finger, or hides the panel.
        if (e.pointerType !== 'mouse') this.updateHover(e.offsetX, e.offsetY)
        this.click(this.systemAt(e.offsetX, e.offsetY))
      }
    }
    if (e.pointerType === 'mouse') this.updateHover(e.offsetX, e.offsetY)
  }

  /** A click on a system (or -1, empty space): a selection, and a double click if it is the second in a row. */
  private click(index: number): void {
    const now = performance.now()
    const double = index >= 0 && this.lastClick?.index === index && now - this.lastClick.at < DOUBLE_CLICK_MS
    this.lastClick = double ? null : { index, at: now }
    this.onSystemClick?.(index)
    if (double) this.onSystemDoubleClick?.(index)
  }

  private readonly onPointerLeave = (e: PointerEvent) => {
    // A finger "leaves" right after every tap; only a mouse leaving the map hides the panel.
    if (e.pointerType === 'mouse' && this.pointers.size === 0) this.setHovered(-1)
  }

  private pointerDistance(): number {
    const [a, b] = [...this.pointers.values()]
    return Math.hypot(a.x - b.x, a.y - b.y)
  }

  private updateHover(x: number, y: number): void {
    this.setHovered(this.systemAt(x, y))
  }

  /** The visible system under a point on screen (CSS pixels), or -1. The focused and selected systems are always visible. */
  private systemAt(x: number, y: number): number {
    const worldX = (x - this.offsetX) / this.scale
    const worldY = (y - this.offsetY) / this.scale
    const nearest = this.grid.nearest(worldX, worldY, HOVER_RADIUS_PX / this.scale)
    return nearest >= 0 && (this.filter.groups[nearest] !== HIDDEN || nearest === this.focused || nearest === this.selection) ? nearest : -1
  }

  /** A right click on a system opens its menu; elsewhere the browser's own menu stays. */
  private readonly onContextMenuEvent = (e: MouseEvent) => {
    const index = this.systemAt(e.offsetX, e.offsetY)
    if (index < 0) return
    e.preventDefault()
    // Touch browsers that also send this after a long press: the long press has opened the menu already.
    if (performance.now() - this.longPressedAt < 1000) return
    this.onContextMenu?.({ index, x: e.offsetX, y: e.offsetY })
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
      areaWidth: this.width,
    })
  }
}

function overlap(a: Box, b: Box): boolean {
  return a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom
}

function markerBadge(row: MarkerRow, k: number): { left: number; top: number } {
  return { left: row.anchorX + k * (MARKER_SIZE + MARKER_GAP), top: row.anchorY - MARKER_SIZE }
}

/** A square with the top-left and bottom-right corners cut off, the HUD's badge shape. */
function chamfer(left: number, top: number, size: number, cut: number): Path2D {
  const path = new Path2D()
  path.moveTo(left + cut, top)
  path.lineTo(left + size, top)
  path.lineTo(left + size, top + size - cut)
  path.lineTo(left + size - cut, top + size)
  path.lineTo(left, top + size)
  path.lineTo(left, top + cut)
  path.closePath()
  return path
}

function iconPath(data: string): Path2D {
  let path = iconPaths.get(data)
  if (!path) iconPaths.set(data, (path = new Path2D(data)))
  return path
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
    focused: resolveColor('var(--primary)'),
    selection: resolveColor('var(--fg)'),
    huntGlow: resolveColor('var(--hunt-glow)'),
    jumpRange: resolveColor('var(--jump-range)'),
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

/** An rgba() colour (as resolveColor returns) with another alpha. */
function withAlpha(rgba: string, alpha: number): string {
  return rgba.replace(/,\s*[\d.]+\)$/, `, ${alpha.toFixed(3)})`)
}
