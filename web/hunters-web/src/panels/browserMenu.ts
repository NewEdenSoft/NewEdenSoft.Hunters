import type { MouseEvent } from 'react'

/** Panels have no browser menu (like the map), except in a text field, to paste into it. */
export function blockBrowserMenu(e: MouseEvent) {
  if (!(e.target as Element).closest('input, textarea, [contenteditable="true"]')) e.preventDefault()
}
