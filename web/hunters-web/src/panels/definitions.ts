import type { IconName } from '../components/ui/Icon'

export type PanelId = 'map' | 'hunting' | 'jump-range' | 'characters'

/** A panel over the map: its title, launcher icon, where it starts, and how it can be sized. */
export interface PanelDefinition {
  id: PanelId
  title: string
  icon: IconName
  /**
   * Where the panel starts, in CSS pixels from the top left of the map. A negative x is measured
   * from the right edge instead, for panels that start on the right.
   */
  initial: { x: number; y: number; width: number }
  /** Whether the user can resize the panel. Defaults to true. */
  resizable?: boolean
  minWidth?: number
  minHeight?: number
  /** Open the first time the map is shown. Defaults to true. */
  openByDefault?: boolean
}

/** The panels over the map, in launcher order. */
export const panelDefinitions: PanelDefinition[] = [
  // The legend shares out the width; below 330 px its labels would touch.
  { id: 'map', title: 'Map', icon: 'layers', initial: { x: 12, y: 12, width: 340 }, minWidth: 330 },
  // Under the Map panel (8 px apart with the Security colour).
  { id: 'hunting', title: 'Hunting', icon: 'radar', initial: { x: 12, y: 342, width: 340 }, minWidth: 220 },
  // The range is off until chosen, so its panel waits on the rail.
  { id: 'jump-range', title: 'Jump range', icon: 'range', initial: { x: -12, y: 330, width: 290 }, minWidth: 220, openByDefault: false },
  { id: 'characters', title: 'Characters', icon: 'users', initial: { x: -12, y: 12, width: 290 }, minWidth: 220 },
]

export const DEFAULT_MIN_WIDTH = 180
export const DEFAULT_MIN_HEIGHT = 80
