import { cn } from './styles'

/** Warning triangle, the same shape as the desktop's Hud.Icon.Warning. */
export function WarningIcon({ className, title }: { className?: string; title?: string }) {
  return (
    <svg
      viewBox="0 0 24 24"
      className={cn('size-4 shrink-0 fill-warning', className)}
      role={title ? 'img' : undefined}
      aria-hidden={title ? undefined : true}
    >
      {title && <title>{title}</title>}
      <path fillRule="evenodd" d="M12 2 L23 21 L1 21 Z M11 9 L11 15 L13 15 L13 9 Z M11 17 L11 19 L13 19 L13 17 Z" />
    </svg>
  )
}
