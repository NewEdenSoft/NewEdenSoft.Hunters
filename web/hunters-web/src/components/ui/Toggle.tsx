import type { ReactNode } from 'react'
import { Button } from './Button'

interface ToggleProps {
  active: boolean
  onClick: () => void
  children: ReactNode
  /** A tooltip, for what the short label leaves out. */
  title?: string
}

/** One option of a small set of choices; the active one is highlighted. */
export function Toggle({ active, onClick, children, title }: ToggleProps) {
  return (
    <Button variant={active ? 'primary' : 'ghost'} className="px-3 py-1.5 text-[0.6875rem]" aria-pressed={active} onClick={onClick} title={title}>
      {children}
    </Button>
  )
}
