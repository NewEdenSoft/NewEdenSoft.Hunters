import type { ComponentProps } from 'react'
import { cn } from './styles'

interface LabelProps extends ComponentProps<'span'> {
  accent?: boolean
}

/** Small uppercase HUD caption. */
export function Label({ accent, className, ...props }: LabelProps) {
  return (
    <span
      className={cn(
        'font-display text-[0.6875rem] font-semibold tracking-[0.18em] uppercase',
        accent ? 'text-primary' : 'text-fg-muted',
        className,
      )}
      {...props}
    />
  )
}
