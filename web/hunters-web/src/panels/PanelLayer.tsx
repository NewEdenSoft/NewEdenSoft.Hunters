import { useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { blockBrowserMenu } from './browserMenu'
import type { PanelId } from './definitions'
import type { Rect, Size } from './geometry'
import { PanelLayerContext } from './panelLayerContext'

/**
 * The layer over the map that panels float in. It passes clicks between panels through to the map,
 * and re-measures when the window changes size so panels can be kept in view.
 */
export function PanelLayer({ children }: { children: ReactNode }) {
  const ref = useRef<HTMLDivElement>(null)
  const [area, setArea] = useState<Size>({ width: 0, height: 0 })
  const [rects] = useState(() => new Map<PanelId, Rect>())

  useLayoutEffect(() => {
    const element = ref.current!
    const measure = () => setArea({ width: element.clientWidth, height: element.clientHeight })
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  return (
    <div ref={ref} className="pointer-events-none absolute inset-0 overflow-hidden" onContextMenu={blockBrowserMenu}>
      {area.width > 0 && <PanelLayerContext value={{ area, rects }}>{children}</PanelLayerContext>}
    </div>
  )
}
