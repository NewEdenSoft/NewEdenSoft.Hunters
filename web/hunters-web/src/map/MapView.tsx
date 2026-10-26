import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ActivityColumns } from '../api/activity'
import { ProfileMark } from '../components/map/ProfileMark'
import { navigateItem, SystemContextMenu } from '../components/map/SystemContextMenu'
import { useDestination } from '../components/map/useDestination'
import { cn } from '../components/ui/styles'
import { securityColor } from '../theme/themes'
import type { MapFilter } from './filters'
import type { HuntingLayer, PlaceProfile } from './hunting'
import { formatLy, isCynoTarget } from './jumpRange'
import { MapRenderer, type MapContextMenuRequest, type MapHover, type MapMarkerGroup } from './MapRenderer'
import { lyBetween, type MapLayout, type MapModel } from './model'
import { gateRoute } from './route'
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
  /**
   * A gate route to highlight (system indexes, origin first), with bars moving along it towards the
   * destination; null for none. While a system on the map is hovered, the route there from the focused system shows instead.
   */
  route?: number[] | null
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
  route = null,
}: MapViewProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const rendererRef = useRef<MapRenderer | null>(null)
  const [hover, setHover] = useState<MapHover | null>(null)
  const [menu, setMenu] = useState<MapContextMenuRequest | null>(null)
  const closeMenu = useCallback(() => setMenu(null), [])
  // The system the menu was last opened on: "Navigate to" sets the destination there, its outcome shown on the map.
  const [menuSystem, setMenuSystem] = useState(-1)
  const destination = useDestination(menuSystem >= 0 ? model.ids[menuSystem] : 0, menuSystem >= 0 ? model.names[menuSystem] : '')

  // The renderer lives as long as the model; everything else is applied to it without recreating it.
  const latest = useRef({ layout, filter, markers, focus, focused, selection, hunting, jumpRange, route, onMarkerClick, onUserPan, onSelectSystem, onDoubleClickSystem })
  useEffect(() => {
    latest.current = { layout, filter, markers, focus, focused, selection, hunting, jumpRange, route, onMarkerClick, onUserPan, onSelectSystem, onDoubleClickSystem }
  })

  useEffect(() => {
    const { layout, filter, markers, focus, focused, selection, hunting, jumpRange, route } = latest.current
    const renderer = new MapRenderer(canvasRef.current!, model, layout, filter)
    renderer.onHover = setHover
    renderer.onMarkerClick = (id) => latest.current.onMarkerClick?.(id)
    renderer.onUserPan = () => latest.current.onUserPan?.()
    renderer.onSystemClick = (index) => latest.current.onSelectSystem?.(index)
    renderer.onSystemDoubleClick = (index) => latest.current.onDoubleClickSystem?.(index)
    renderer.onContextMenu = (request) => {
      setMenu(request)
      setMenuSystem(request.index)
    }
    renderer.onUserMove = () => setMenu(null)
    renderer.setFocused(focused)
    renderer.setSelection(selection)
    renderer.setHunting(hunting)
    renderer.setJumpRange(jumpRange?.inRange ?? null)
    renderer.setRoute(route)
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

  // A system hovered on the map shows the route there from the focused system instead, and its jumps in the hover panel.
  const hovered = hover?.index ?? -1
  const hoverRoute = useMemo(() => (focused >= 0 && hovered >= 0 ? gateRoute(model, focused, hovered) : null), [model, focused, hovered])
  const shownRoute = focused >= 0 && hovered >= 0 ? hoverRoute : route
  useEffect(() => {
    rendererRef.current?.setRoute(shownRoute)
  }, [shownRoute])

  useEffect(() => {
    if (focus) rendererRef.current?.focusOn(focus.systemIndex, focus.zoomIn)
  }, [focus])

  const here = hover ? markers.find((g) => g.systemIndex === hover.index)?.markers : undefined

  return (
    // The map has no browser menu: a right click on a system opens its own, elsewhere nothing.
    <div className="relative size-full overflow-hidden" onContextMenu={(e) => e.preventDefault()}>
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
          focused={focused}
          route={hoverRoute}
        />
      )}
      {menu && (
        <SystemContextMenu
          name={model.names[menu.index]}
          security={model.security[menu.index]}
          x={menu.x}
          y={menu.y}
          onClose={closeMenu}
          items={[
            menu.index === focused
              ? { label: 'Clear focus', icon: 'close', onSelect: () => onClearSystem?.() }
              : { label: 'Focus system', icon: 'target', onSelect: () => onFocusSystem?.(menu.index) },
            navigateItem(destination),
          ]}
        />
      )}
      {destination.status && (
        <div
          role="status"
          className={cn(
            'pointer-events-none absolute bottom-3 left-1/2 grid max-w-[calc(100%-1.5rem)] -translate-x-1/2 border border-line-strong bg-surface/95 px-3 py-1.5 text-[0.8125rem] shadow-lg backdrop-blur-sm',
            destination.status.tone === 'ok' ? 'text-ok' : destination.status.tone === 'danger' ? 'text-danger' : 'text-fg-muted',
          )}
        >
          {destination.status.lines.map((line) => (
            <span key={line}>{line}</span>
          ))}
        </div>
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
  focused,
  route,
}: {
  model: MapModel
  hover: MapHover
  characters?: string
  npcKills: ActivityColumns | null
  profiles?: PlaceProfile[]
  jumpRange: JumpRangeView | null
  focused: number
  /** The gate route from the focused system, or null (none focused, or no gates lead here). */
  route: number[] | null
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
      {focused >= 0 && hover.index !== focused && <JumpsLine from={model.names[focused]} route={route} />}
      {jumpRange && hover.index !== jumpRange.origin && <JumpRangeLine model={model} range={jumpRange} index={hover.index} />}
      {npcKills && <NpcKillsLine npcKills={npcKills} index={hover.index} />}
      {profiles && profiles.length > 0 && <ProfilesLines profiles={profiles} />}
      {characters && <span className="text-[0.8125rem] text-primary">{characters}</span>}
    </div>
  )
}

/** Gate jumps from the focused system, the shortest way, or that no gates lead here. */
function JumpsLine({ from, route }: { from: string; route: number[] | null }) {
  const jumps = route === null ? null : route.length - 1
  return (
    <span className="flex flex-wrap items-baseline gap-x-2 text-[0.8125rem] text-fg-muted">
      {jumps === null ? <span className="text-fg-faint">no gates</span> : <span className="font-mono font-semibold text-fg">{jumps === 1 ? '1 jump' : `${jumps} jumps`}</span>}
      from {from}
    </span>
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
 * "peak now" that this is its busiest hour here, "seen" otherwise; "reliable" that someone with a
 * routine here is usually here at this hour (the map's ring).
 */
function ProfilesLines({ profiles }: { profiles: PlaceProfile[] }) {
  return (
    <span className="mt-0.5 grid gap-0.5 border-t border-line pt-1 text-[0.8125rem]">
      {profiles.map((p) => (
        <span key={p.profile} className="flex items-baseline gap-2">
          <ProfileMark style={p} className="w-5 text-[0.6875rem]" />
          <span className={p.likely > 0 ? 'min-w-0 truncate text-fg' : 'min-w-0 truncate text-fg-muted'}>{p.name}</span>
          {p.reliableNow > 0 && (
            <span className="ml-auto shrink-0 text-hunt-reliable" title="Someone with a routine here is usually here at this hour (UTC)">
              reliable
            </span>
          )}
          <span
            className={cn('shrink-0', p.reliableNow <= 0 && 'ml-auto', p.likely > 0 ? 'text-warning' : 'text-fg-faint')}
            title={p.likely > 0 ? 'Usually seen here at this hour (UTC)' : 'Seen here, but not usually at this hour'}
          >
            {p.likely > 0 ? (p.peakNow >= 0.8 ? 'peak now' : 'now') : 'seen'}
          </span>
        </span>
      ))}
    </span>
  )
}
