import { useEffect, useRef, useState } from 'react'
import { securityColor } from '../theme/themes'
import { MapRenderer, type MapHover } from './MapRenderer'
import type { MapLayout, MapModel } from './model'
import { formatSecurity } from './security'

interface MapViewProps {
  model: MapModel
  layout: MapLayout
}

/** Hosts the canvas renderer and shows a panel with the hovered system's name. */
export function MapView({ model, layout }: MapViewProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const rendererRef = useRef<MapRenderer | null>(null)
  const [hover, setHover] = useState<MapHover | null>(null)

  // The renderer lives as long as the model; the layout is applied below without recreating it.
  const initialLayout = useRef(layout)
  useEffect(() => {
    const renderer = new MapRenderer(canvasRef.current!, model, initialLayout.current)
    renderer.onHover = setHover
    rendererRef.current = renderer
    return () => {
      renderer.destroy()
      rendererRef.current = null
    }
  }, [model])

  useEffect(() => {
    rendererRef.current?.setLayout(layout)
  }, [layout])

  return (
    <div className="relative size-full overflow-hidden">
      <canvas ref={canvasRef} className="block size-full touch-none" aria-label="Map of known space" />
      {hover && <SystemPanel model={model} hover={hover} />}
    </div>
  )
}

function SystemPanel({ model, hover }: { model: MapModel; hover: MapHover }) {
  const security = model.security[hover.index]
  return (
    <div
      className="pointer-events-none absolute grid gap-0.5 border border-line-strong bg-surface/95 px-3 py-2 shadow-lg backdrop-blur-sm"
      style={{ left: hover.x + 14, top: hover.y + 14 }}
    >
      <span className="font-display font-semibold text-fg">{model.names[hover.index]}</span>
      <span className="flex items-center gap-2 text-[0.8125rem] text-fg-muted">
        <span className="font-mono font-semibold" style={{ color: securityColor(security) }}>
          {formatSecurity(security)}
        </span>
        {model.regionNames.get(model.regionIds[hover.index])}
      </span>
    </div>
  )
}
