import { useCallback, useState } from 'react'
import { applyTheme, getTheme, savedThemeId, type ThemeDefinition } from './themes'

/** The active theme. The theme itself is applied before first render in main.tsx. */
export function useTheme(): [ThemeDefinition, (id: string) => void] {
  const [theme, setTheme] = useState(() => getTheme(savedThemeId()))
  const select = useCallback((id: string) => setTheme(applyTheme(id)), [])
  return [theme, select]
}
