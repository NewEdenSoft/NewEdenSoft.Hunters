import { useEffect, useRef, useState } from 'react'
import { securityColor } from '../theme/themes'
import type { MapFilter } from './filters'
import { MapRenderer, type MapHover, type MapMarkerGroup } from './MapRenderer'
import type { MapLayout, MapModel } from './model'
import { formatSecurity } from './security'

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
}

/**
 * Hosts the canvas renderer: the map, the characters' markers, focusing on a system, and a panel
 * with the hovered system's name, security, region and the account's characters there.
 */
export function MapView({ model, layout, filter, markers = [], focus = null, onMarkerClick, onUserPan }: MapViewProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const rendererRef = useRef<MapRenderer | null>(null)
  const [hover, setHover] = useState<MapHover | null>(null)

  // The renderer lives as long as the model; everything else is applied to it without recreating it.
  const latest = useRef({ layout, filter, markers, focus, onMarkerClick, onUserPan })
  useEffect(() => {
    latest.current = { layout, filter, markers, focus, onMarkerClick, onUserPan }
  })

  useEffect(() => {
    const { layout, filter, markers, focus } = latest.current
    const renderer = new MapRenderer(canvasRef.current!, model, layout, filter)
    renderer.onHover = setHover
    renderer.onMarkerClick = (id) => latest.current.onMarkerClick?.(id)
    renderer.onUserPan = () => latest.current.onUserPan?.()
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
    if (focus) rendererRef.current?.focusOn(focus.systemIndex, focus.zoomIn)
  }, [focus])

  const here = hover ? markers.find((g) => g.systemIndex === hover.index)?.markers : undefined

  return (
    <div className="relative size-full overflow-hidden">
      <canvas ref={canvasRef} className="block size-full touch-none" aria-label="Map of known space" />
      {hover && (
        <SystemPanel
          model={model}
          hover={hover}
          characters={here?.map((m) => (m.online ? m.name : `${m.name} (offline)`)).join(', ')}
        />
      )}
    </div>
  )
}

/** Wider than the panel ever gets (max-w-72), so it is never pushed off the right edge. */
const PANEL_ROOM = 300

function SystemPanel({ model, hover, characters }: { model: MapModel; hover: MapHover; characters?: string }) {
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
      {characters && <span className="text-[0.8125rem] text-primary">{characters}</span>}
    </div>
  )
}
