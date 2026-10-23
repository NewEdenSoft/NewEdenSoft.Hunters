import { profileIcon } from '../../map/profileIcons'
import type { ProfileStyle } from '../../theme/themes'
import { cn } from '../ui/styles'

/** A hunting profile's mark, as on its map badge: its icon if it has one, otherwise its code, in its colour. */
export function ProfileMark({ style, className }: { style: ProfileStyle; className?: string }) {
  const icon = profileIcon(style.icon)
  if (icon) {
    return (
      <svg
        viewBox="0 0 24 24"
        fill="none"
        stroke={style.colour}
        strokeWidth={2}
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden
        className={cn('inline-block size-[1.1em] shrink-0 align-[-0.15em]', className)}
      >
        <path d={icon} />
      </svg>
    )
  }
  return (
    <span className={cn('font-display font-bold', className)} style={{ color: style.colour }}>
      {style.code}
    </span>
  )
}
