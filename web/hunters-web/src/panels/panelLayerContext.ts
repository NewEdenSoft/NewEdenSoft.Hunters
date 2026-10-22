import { createContext, useContext } from 'react'
import type { PanelId } from './definitions'
import type { Rect, Size } from './geometry'

export interface PanelLayerContextValue {
  /** The layer's size: panels stay inside it. */
  area: Size
  /** Where each panel is on screen right now, for snapping to one another. */
  rects: Map<PanelId, Rect>
}

export const PanelLayerContext = createContext<PanelLayerContextValue | null>(null)

export function usePanelLayer(): PanelLayerContextValue {
  const value = useContext(PanelLayerContext)
  if (!value) throw new Error('A FloatingPanel must be inside a PanelLayer.')
  return value
}
