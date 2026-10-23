/**
 * Icons a hunting profile's badge can show instead of its code (`icon` in design/themes.json, `profiles`):
 * stroke paths in a 24 x 24 box, drawn in the profile's colour.
 */
export const PROFILE_ICONS: Record<string, string> = {
  // Smartbomb camps: pods popped on a gate.
  skull:
    'M12 3 C7.3 3 4 6.4 4 10.6 C4 13.1 5.2 15 7 16.2 L7 19.5 L17 19.5 L17 16.2 C18.8 15 20 13.1 20 10.6 C20 6.4 16.7 3 12 3 Z ' +
    'M9.2 9.6 A1.7 1.7 0 1 0 9.2 13 A1.7 1.7 0 1 0 9.2 9.6 Z M14.8 9.6 A1.7 1.7 0 1 0 14.8 13 A1.7 1.7 0 1 0 14.8 9.6 Z ' +
    'M12 14 L11 16 L13 16 Z M10 19.5 L10 17.5 M14 19.5 L14 17.5',
}

/** The path of a named profile icon, or undefined (the badge shows its code). */
export function profileIcon(name: string | undefined): string | undefined {
  return name ? PROFILE_ICONS[name] : undefined
}
