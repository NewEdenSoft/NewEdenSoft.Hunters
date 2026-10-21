import type { ComponentProps } from 'react'
import { cn } from './styles'

interface StatusProps extends ComponentProps<'p'> {
  tone: 'ok' | 'danger'
}

/** A line of text led by a glowing status dot. */
export function Status({ tone, className, ...props }: StatusProps) {
  return (
    <p
      className={cn(
        'flex flex-wrap items-center gap-2 before:size-1.75 before:bg-(--c) before:shadow-[0_0_8px_var(--c)]',
        tone === 'ok' ? '[--c:var(--ok)]' : 'text-danger [--c:var(--danger)]',
        className,
      )}
      {...props}
    />
  )
}
