import { lyBetween, type MapModel } from './model'
import { displaySecurity } from './security'

/** A ship class's jump range with Jump Drive Calibration V, in light years. */
export interface JumpRangePreset {
  id: string
  label: string
  ly: number
}

export const jumpRangePresets: JumpRangePreset[] = [
  { id: 'blops', label: 'Black Ops', ly: 8 },
  { id: 'capital', label: 'Carrier · Dread · FAX', ly: 7 },
  { id: 'super', label: 'Super · Titan', ly: 6 },
  { id: 'jf', label: 'Jump freighter · Rorqual', ly: 10 },
]

/** The custom range is kept within this (light years). */
export const MIN_JUMP_RANGE = 0.5
export const MAX_JUMP_RANGE = 20

/** Pochven: Triglavian space, no cynosural fields. */
const POCHVEN = 10000070
/** Zarzakh (in Yasna Zakh): no cynosural fields. */
const ZARZAKH = 30100000

/**
 * A jump drive can land here: a cynosural field can be lit. Not high-sec (shown as 0.5 or more), not
 * Pochven, not Zarzakh. The same rule as the server's (`CynoTargets` in Hunters.Domain).
 */
export function isCynoTarget(model: MapModel, index: number): boolean {
  return model.regionIds[index] !== POCHVEN && model.ids[index] !== ZARZAKH && displaySecurity(model.security[index]) < 0.5
}

/**
 * The systems a jump drive can reach from `origin` (by index) within `ly` light years: 1 for each
 * cyno target in range, 0 for the rest and the origin itself. Distances are in 3D (`lyBetween`).
 */
export function jumpRangeOf(model: MapModel, origin: number, ly: number): Uint8Array {
  const inRange = new Uint8Array(model.count)
  for (let i = 0; i < model.count; i++) {
    if (i !== origin && isCynoTarget(model, i) && lyBetween(model, origin, i) <= ly) inRange[i] = 1
  }
  return inRange
}

/** A range as text: "8 ly", "7.5 ly". */
export function formatLy(ly: number): string {
  return `${Number.isInteger(ly) ? ly : ly.toFixed(ly < 10 ? 2 : 1)} ly`
}
