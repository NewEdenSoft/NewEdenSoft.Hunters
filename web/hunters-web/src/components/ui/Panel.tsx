import type { ComponentProps } from 'react'
import { cn } from './styles'

/** A surface with HUD corner brackets (top left, bottom right). */
export function Panel({ className, ...props }: ComponentProps<'section'>) {
  return (
    <section
      className={cn(
        'relative grid min-w-0 gap-3 border border-line bg-surface p-4.5',
        'before:pointer-events-none before:absolute before:-top-px before:-left-px before:size-3 before:border-t-2 before:border-l-2 before:border-primary',
        'after:pointer-events-none after:absolute after:-right-px after:-bottom-px after:size-3 after:border-r-2 after:border-b-2 after:border-primary',
        className,
      )}
      {...props}
    />
  )
}
