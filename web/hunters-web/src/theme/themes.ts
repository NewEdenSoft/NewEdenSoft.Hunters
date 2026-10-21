import themesJson from '@design/themes.json'
import { NULL_BUCKET, securityBucket } from '../map/security'

// design/themes.json is shared with the desktop app. See design/README.md.

export interface ThemeDefinition {
  id: string
  name: string
  group: string
  primary: string
  secondary: string
  tint: string
}

interface ThemeCatalog {
  defaultTheme: string
  themes: ThemeDefinition[]
  /** Each rule is [a, percent, b]: color-mix(in oklab, a percent%, b). */
  derived: Record<string, { mix: (string | number)[] }>
  fixed: Record<string, string>
  security: { steps: string[]; null: string }
}

const catalog: ThemeCatalog = themesJson

const STORAGE_KEY = 'hunters.theme'
const THEME_INPUTS = ['primary', 'secondary', 'tint'] as const

export const themes = catalog.themes
export const defaultThemeId = catalog.defaultTheme

export const themeGroups: { name: string; themes: ThemeDefinition[] }[] = [...new Set(themes.map((t) => t.group))].map(
  (name) => ({ name, themes: themes.filter((t) => t.group === name) }),
)

export function getTheme(id: string | null | undefined): ThemeDefinition {
  return themes.find((t) => t.id === id) ?? themes.find((t) => t.id === defaultThemeId)!
}

/** Security status colour, for the security as EVE displays it. */
export function securityColor(security: number): string {
  return securityBucketColor(securityBucket(security))
}

/** Colour of a {@link securityBucket}. */
export function securityBucketColor(bucket: number): string {
  return bucket === NULL_BUCKET ? catalog.security.null : catalog.security.steps[bucket]
}

function operand(value: string): string {
  if (value === 'transparent' || value.startsWith('#')) return value
  if ((THEME_INPUTS as readonly string[]).includes(value)) return `var(--${value})`
  throw new Error(`Unknown mix operand '${value}' in themes.json`)
}

/**
 * Writes the derived and fixed tokens as CSS custom properties. Derived tokens are CSS
 * color-mix() expressions over --primary, --secondary and --tint, so switching theme only
 * changes those three.
 */
export function installTokens(): void {
  const lines: string[] = []
  for (const [key, rule] of Object.entries(catalog.derived)) {
    const [a, percent, b] = rule.mix
    lines.push(`--${key}: color-mix(in oklab, ${operand(String(a))} ${Number(percent)}%, ${operand(String(b))});`)
  }
  for (const [key, color] of Object.entries(catalog.fixed)) {
    lines.push(`--${key}: ${color};`)
  }
  catalog.security.steps.forEach((color, i) => lines.push(`--sec-${i}: ${color};`))
  lines.push(`--sec-null: ${catalog.security.null};`)

  let style = document.getElementById('hunters-tokens')
  if (!style) {
    style = document.createElement('style')
    style.id = 'hunters-tokens'
    document.head.prepend(style)
  }
  style.textContent = `:root {\n  ${lines.join('\n  ')}\n}`
}

export function applyTheme(id: string, remember = true): ThemeDefinition {
  const theme = getTheme(id)
  const root = document.documentElement
  for (const input of THEME_INPUTS) root.style.setProperty(`--${input}`, theme[input])
  root.dataset.huntersTheme = theme.id
  if (remember) {
    try {
      localStorage.setItem(STORAGE_KEY, theme.id)
    } catch {
      // Storage unavailable (private mode); the theme still applies for this session.
    }
  }
  return theme
}

export function savedThemeId(): string {
  try {
    return localStorage.getItem(STORAGE_KEY) ?? defaultThemeId
  } catch {
    return defaultThemeId
  }
}
