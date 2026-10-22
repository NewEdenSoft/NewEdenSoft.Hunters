// Mirror NewEdenSoft.Hunters.Desktop.Characters.CharacterMarker: the same choices and icons, so a
// character can be marked the same way in both clients.

export type MarkerKind = 'letter' | 'number' | 'icon'

/**
 * How a character is marked on the map: a letter, a number or an icon, shown in a small badge
 * joined to the character's system. The default is the first letter of the character's name.
 */
export interface CharacterMarker {
  kind: MarkerKind
  /** "A"–"Z", "1"–"9", or an icon name from {@link MARKER_ICONS}. */
  value: string
}

/**
 * Marker icons as SVG path data in a 24 x 24 box, drawn as 2-unit strokes with round joins. The
 * same paths as the desktop's MarkerIcons.
 */
export const MARKER_ICONS: Record<string, string> = {
  target: 'M3 12 A9 9 0 1 0 21 12 A9 9 0 1 0 3 12 Z M12 1 L12 7 M12 17 L12 23 M1 12 L7 12 M17 12 L23 12',
  star: 'M12 2 L15 8.5 L22 9.3 L16.8 14.1 L18.3 21.1 L12 17.6 L5.7 21.1 L7.2 14.1 L2 9.3 L9 8.5 Z',
  diamond: 'M12 2 L21 12 L12 22 L3 12 Z',
  triangle: 'M12 3 L22 21 L2 21 Z',
  bolt: 'M13 2 L4 14 L11 14 L10 22 L19 10 L12 10 Z',
  shield: 'M12 2 L20 5 L20 11 C20 16 16.5 20 12 22 C7.5 20 4 16 4 11 L4 5 Z',
  eye: 'M2 12 C2 12 6 5 12 5 C18 5 22 12 22 12 C22 12 18 19 12 19 C6 19 2 12 2 12 Z M9 12 A3 3 0 1 0 15 12 A3 3 0 1 0 9 12 Z',
  heart: 'M12 21 C12 21 4 15.5 4 10 A4.5 4.5 0 0 1 12 7.2 A4.5 4.5 0 0 1 20 10 C20 15.5 12 21 12 21 Z',
}

export const LETTERS: CharacterMarker[] = Array.from({ length: 26 }, (_, i) => ({ kind: 'letter', value: String.fromCharCode(65 + i) }))
export const NUMBERS: CharacterMarker[] = Array.from({ length: 9 }, (_, i) => ({ kind: 'number', value: String(i + 1) }))
export const ICONS: CharacterMarker[] = Object.keys(MARKER_ICONS).map((value) => ({ kind: 'icon', value }))

/** The first letter of the name, or A when it has none. */
export function defaultMarker(characterName: string): CharacterMarker {
  const letter = [...characterName].find((c) => /[a-z]/i.test(c))
  return { kind: 'letter', value: letter ? letter.toUpperCase() : 'A' }
}

/** One of the markers on offer; anything else (edited or outdated storage) is not. */
export function isValidMarker(marker: unknown): marker is CharacterMarker {
  if (typeof marker !== 'object' || marker === null) return false
  const { kind, value } = marker as Partial<CharacterMarker>
  const choices = kind === 'letter' ? LETTERS : kind === 'number' ? NUMBERS : kind === 'icon' ? ICONS : []
  return choices.some((m) => m.value === value)
}

export function sameMarker(a: CharacterMarker, b: CharacterMarker): boolean {
  return a.kind === b.kind && a.value === b.value
}
