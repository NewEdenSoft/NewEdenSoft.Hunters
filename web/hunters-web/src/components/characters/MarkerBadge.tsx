import { MARKER_ICONS, type CharacterMarker } from '../../characters/markers'
import { cn } from '../ui/styles'

interface MarkerBadgeProps {
  marker: CharacterMarker
  /** Filled in the primary colour, for the chosen option in a picker. */
  selected?: boolean
  className?: string
}

/** A character's map marker: its letter, number or icon in a chamfered badge, like the desktop's. */
export function MarkerBadge({ marker, selected, className }: MarkerBadgeProps) {
  const icon = marker.kind === 'icon' ? MARKER_ICONS[marker.value] : undefined
  return (
    <svg viewBox="0 0 24 24" aria-hidden className={cn('size-7 shrink-0', selected ? 'text-on-primary' : 'text-primary', className)}>
      <path
        d="M5 0.5 H23.5 V19 L19 23.5 H0.5 V5 Z"
        className={selected ? 'fill-primary stroke-primary' : 'fill-bg stroke-primary'}
        strokeWidth={1}
      />
      {icon ? (
        // Icons are 2-unit strokes in a 24 x 24 box, drawn at 60% of the badge.
        <path
          d={icon}
          transform="translate(4.8 4.8) scale(0.6)"
          fill="none"
          stroke="currentColor"
          strokeWidth={2.4}
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      ) : (
        <text
          x="12"
          y="12.6"
          textAnchor="middle"
          dominantBaseline="central"
          fill="currentColor"
          className="font-display text-[13px] font-semibold"
        >
          {marker.value}
        </text>
      )}
    </svg>
  )
}
