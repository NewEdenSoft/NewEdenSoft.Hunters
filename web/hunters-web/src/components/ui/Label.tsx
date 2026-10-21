import type { ComponentProps } from 'react'
import { cn } from './styles'

interface LabelProps extends ComponentProps<'span'> {
  tone?: 'muted' | 'accent' | 'danger'
}

const tones = {
  muted: 'text-fg-muted',
  accent: 'text-primary',
  danger: 'text-danger',
}

/** Small uppercase HUD caption. */
export function Label({ tone = 'muted', className, ...props }: LabelProps) {
  return (
    <span
      className={cn(
        'font-display text-[0.6875rem] font-semibold tracking-[0.18em] uppercase',
        tones[tone],
        className,
      )}
      {...props}
    />
  )
}
