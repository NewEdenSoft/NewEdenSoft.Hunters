import { useCallback, useEffect, useRef, useState } from 'react'
import type { ActivityColumns } from '../api/activity'
import { ProfileMark } from '../components/map/ProfileMark'
import { SystemContextMenu } from '../components/map/SystemContextMenu'
import { securityColor } from '../theme/themes'
import type { MapFilter } from './filters'
import type { HuntingLayer, PlaceProfile } from './hunting'
import { formatLy, isCynoTarget } from './jumpRange'
import { MapRenderer, type MapContextMenuRequest, type MapHover, type MapMarkerGroup } from './MapRenderer'
import { lyBetween, type MapLayout, type MapModel } from './model'
import { formatSecurity } from './security'

/** The jump range shown on the map: from the origin (the focused system), how far, and the systems it reaches. */
export interface JumpRangeView {
  origin: number
  ly: number
  inRange: Uint8Array
}

/** A system to move the map to; a new object for every request, so each one is applied. */
export interface MapFocusTarget {
  systemIndex: number
  /** Zoom in far enough to read system names; otherwise keep the zoom (following). */
  zoomIn: boolean
}

interface MapViewProps {
  model: MapModel
  layout: MapLayout
  filter: MapFilter
  markers?: MapMarkerGroup[]
  focus?: MapFocusTarget | null
  onMarkerClick?: (characterId: number) => void
  onUserPan?: () => void
  /** The focused system's index (the staging point), or -1: double-ringed and always labelled. */
  focused?: number
  /** The selected system's index (the one clicked), or -1: ringed and always labelled. */
  selection?: number
  /** A click on a system selects it; on empty space (-1) clears the selection. */
  onSelectSystem?: (index: number) => void
  /** A double click on a system (it focuses it, when that is enabled). */
  onDoubleClickSystem?: (index: number) => void
  /** Focus a system from its menu (right click, or a long press on touch). */
  onFocusSystem?: (index: number) => void
  onClearSystem?: () => void
  /** NPC kills per system, shown in the hover panel with the change from the hour before. */
  npcKills?: ActivityColumns | null
  /** The shown hunting profiles: a glow and badges per system. */
  hunting?: HuntingLayer | null
  /** The shown hunting profiles seen at each system, listed in the hover panel. */
  profilesAt?: Map<number, PlaceProfile[]> | null
  /** The jump range: a blue glow on the systems it reaches; the hover panel tells the distance. */
  jumpRange?: JumpRangeView | null
}

/**
 * Hosts the canvas renderer: the map, the characters' markers, focusing on a system, the focused
 * system, a system's menu, and a panel with the hovered system's name, security, region and the
 * account's characters there.
 */
export function MapView({
  model,
  layout,
  filter,
  markers = [],
  focus = null,
  onMarkerClick,
  onUserPan,
  focused = -1,
  selection = -1,
  onSelectSystem,
  onDoubleClickSystem,
  onFocusSystem,
  onClearSystem,
  npcKills = null,
  hunting = null,
  profilesAt = null,
  jumpRange = null,
}: MapViewProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const rendererRef = useRef<MapRenderer | null>(null)
  const [hover, setHover] = useState<MapHover | null>(null)
  const [menu, setMenu] = useState<MapContextMenuRequest | null>(null)
  const closeMenu = useCallback(() => setMenu(null), [])

  // The renderer lives as long as the model; everything else is applied to it without recreating it.
  const latest = useRef({ layout, filter, markers, focus, focused, selection, hunting, jumpRange, onMarkerClick, onUserPan, onSelectSystem, onDoubleClickSystem })
  useEffect(() => {
    latest.current = { layout, filter, markers, focus, focused, selection, hunting, jumpRange, onMarkerClick, onUserPan, onSelectSystem, onDoubleClickSystem }
  })

  useEffect(() => {
    const { layout, filter, markers, focus, focused, selection, hunting, jumpRange } = latest.current
    const renderer = new MapRenderer(canvasRef.current!, model, layout, filter)
    renderer.onHover = setHover
    renderer.onMarkerClick = (id) => latest.current.onMarkerClick?.(id)
    renderer.onUserPan = () => latest.current.onUserPan?.()
    renderer.onSystemClick = (index) => latest.current.onSelectSystem?.(index)
    renderer.onSystemDoubleClick = (index) => latest.current.onDoubleClickSystem?.(index)
    renderer.onContextMenu = setMenu
    renderer.onUserMove = () => setMenu(null)
    renderer.setFocused(focused)
    renderer.setSelection(selection)
    renderer.setHunting(hunting)
    renderer.setJumpRange(jumpRange?.inRange ?? null)
    renderer.setMarkers(markers)
    if (focus) renderer.focusOn(focus.systemIndex, focus.zoomIn)
    rendererRef.current = renderer
    return () => {
      renderer.destroy()
      rendererRef.current = null
    }
  }, [model])

  useEffect(() => {
    rendererRef.current?.setLayout(layout)
  }, [layout])

  useEffect(() => {
    rendererRef.current?.setFilter(filter)
  }, [filter])

  useEffect(() => {
    rendererRef.current?.setMarkers(markers)
  }, [markers])

  useEffect(() => {
    rendererRef.current?.setFocused(focused)
  }, [focused])

  useEffect(() => {
    rendererRef.current?.setSelection(selection)
  }, [selection])

  useEffect(() => {
    rendererRef.current?.setHunting(hunting)
  }, [hunting])

  useEffect(() => {
    rendererRef.current?.setJumpRange(jumpRange?.inRange ?? null)
  }, [jumpRange])

  useEffect(() => {
    if (focus) rendererRef.current?.focusOn(focus.systemIndex, focus.zoomIn)
  }, [focus])

  const here = hover ? markers.find((g) => g.systemIndex === hover.index)?.markers : undefined

  return (
    <div className="relative size-full overflow-hidden">
      {/* No text selection or callout on a long press: that opens the system's menu. */}
      <canvas ref={canvasRef} className="block size-full touch-none select-none [-webkit-touch-callout:none]" aria-label="Map of known space" />
      {hover && (
        <SystemPanel
          model={model}
          hover={hover}
          characters={here?.map((m) => (m.online ? m.name : `${m.name} (offline)`)).join(', ')}
          npcKills={npcKills}
          profiles={profilesAt?.get(hover.index)}
          jumpRange={jumpRange}
        />
      )}
      {menu && (
        <SystemContextMenu
          model={model}
          index={menu.index}
          x={menu.x}
          y={menu.y}
          onClose={closeMenu}
          items={
            menu.index === focused
              ? [{ label: 'Clear focus', icon: 'close', onSelect: () => onClearSystem?.() }]
              : [{ label: 'Focus system', icon: 'target', onSelect: () => onFocusSystem?.(menu.index) }]
          }
        />
      )}
    </div>
  )
}

/** Wider than the panel ever gets (max-w-72), so it is never pushed off the right edge. */
const PANEL_ROOM = 300

function SystemPanel({
  model,
  hover,
  characters,
  npcKills,
  profiles,
  jumpRange,
}: {
  model: MapModel
  hover: MapHover
  characters?: string
  npcKills: ActivityColumns | null
  profiles?: PlaceProfile[]
  jumpRange: JumpRangeView | null
}) {
  const { areaWidth } = hover
  const security = model.security[hover.index]
  // Beside the system, or to its left near the right edge (a phone is narrow).
  const left = hover.x + 14 + PANEL_ROOM > areaWidth ? Math.max(8, Math.min(hover.x + 14, areaWidth - PANEL_ROOM)) : hover.x + 14
  return (
    <div
      className="pointer-events-none absolute grid max-w-72 gap-0.5 border border-line-strong bg-surface/95 px-3 py-2 shadow-lg backdrop-blur-sm"
      style={{ left, top: hover.y + 14 }}
    >
      <span className="font-display font-semibold text-fg">{model.names[hover.index]}</span>
      <span className="flex items-center gap-2 text-[0.8125rem] text-fg-muted">
        <span className="font-mono font-semibold" style={{ color: securityColor(security) }}>
          {formatSecurity(security)}
        </span>
        {model.regionNames.get(model.regionIds[hover.index])}
      </span>
      {jumpRange && hover.index !== jumpRange.origin && <JumpRangeLine model={model} range={jumpRange} index={hover.index} />}
      {npcKills && <NpcKillsLine npcKills={npcKills} index={hover.index} />}
      {profiles && profiles.length > 0 && <ProfilesLines profiles={profiles} />}
      {characters && <span className="text-[0.8125rem] text-primary">{characters}</span>}
    </div>
  )
}

/** How far the system is from the focused one, and whether a jump drive can land there in range. */
function JumpRangeLine({ model, range, index }: { model: MapModel; range: JumpRangeView; index: number }) {
  const ly = lyBetween(model, range.origin, index)
  const status = range.inRange[index] ? 'in range' : !isCynoTarget(model, index) ? 'no cyno' : 'out of range'
  return (
    <span className="flex flex-wrap items-baseline gap-x-2 text-[0.8125rem] text-fg-muted">
      <span className="font-mono font-semibold text-fg">{formatLy(Math.round(ly * 100) / 100)}</span>
      from {model.names[range.origin]}
      <span
        className={range.inRange[index] ? 'text-jump-range' : 'text-fg-faint'}
        title={status === 'no cyno' ? 'Jump drives cannot land here: high-sec, Pochven or Zarzakh' : `Jump range ${formatLy(range.ly)}`}
      >
        {status}
      </span>
    </span>
  )
}

/** NPC kills in the last hour, and how many more or fewer than the hour before (when there is one). */
function NpcKillsLine({ npcKills, index }: { npcKills: ActivityColumns; index: number }) {
  const last = npcKills.lastHour[index]
  const change = npcKills.previousHour ? last - npcKills.previousHour[index] : null
  return (
    <span className="flex flex-wrap items-baseline gap-x-2 text-[0.8125rem] text-fg-muted">
      NPC kills
      <span className="font-mono font-semibold text-fg">{last}</span>
      {change !== null && change !== 0 && (
        <span className={change > 0 ? 'font-mono text-ok' : 'font-mono text-danger'} title="Since the hour before">
          {change > 0 ? `▲ +${change}` : `▼ ${change}`}
        </span>
      )}
      {change === 0 && <span className="font-mono text-fg-faint">±0</span>}
      <PeakHint npcKills={npcKills} index={index} />
    </span>
  )
}

/** Usual NPC kills for this hour of the week, and whether the system is in its busy hours. */
function PeakHint({ npcKills, index }: { npcKills: ActivityColumns; index: number }) {
  const typical = npcKills.typical?.[index]
  const ratio = npcKills.peakRatio[index]
  const toPeak = npcKills.hoursToPeak[index]
  const peak =
    ratio !== null && ratio >= 0.8 ? 'Peak now' : toPeak !== null && toPeak > 0 && toPeak <= 6 ? `Peak in ${toPeak} h` : null
  return (
    <>
      {typical !== undefined && typical !== null && <span className="text-fg-faint">usual {Math.round(typical)}</span>}
      {peak && <span className={peak === 'Peak now' ? 'text-warning' : 'text-fg-muted'}>{peak}</span>}
    </>
  )
}

/**
 * The shown hunting profiles seen here, likely now first. "now" means usually seen here at this hour,
 * "peak now" that this is its busiest hour here, "seen" otherwise.
 */
function ProfilesLines({ profiles }: { profiles: PlaceProfile[] }) {
  return (
    <span className="mt-0.5 grid gap-0.5 border-t border-line pt-1 text-[0.8125rem]">
      {profiles.map((p) => (
        <span key={p.profile} className="flex items-baseline gap-2">
          <ProfileMark style={p} className="w-5 text-[0.6875rem]" />
          <span className={p.likely > 0 ? 'min-w-0 truncate text-fg' : 'min-w-0 truncate text-fg-muted'}>{p.name}</span>
          <span
            className={p.likely > 0 ? 'ml-auto shrink-0 text-warning' : 'ml-auto shrink-0 text-fg-faint'}
            title={p.likely > 0 ? 'Usually seen here at this hour (UTC)' : 'Seen here, but not usually at this hour'}
          >
            {p.likely > 0 ? (p.peakNow >= 0.8 ? 'peak now' : 'now') : 'seen'}
          </span>
        </span>
      ))}
    </span>
  )
}
