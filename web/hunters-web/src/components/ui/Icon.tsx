import type { ComponentProps } from 'react'
import { cn } from './styles'

/** Stroke icons in a 24 x 24 box, drawn in the current text colour. */
const paths = {
  map: 'M3 6 L9 3 L15 6 L21 3 L21 18 L15 21 L9 18 L3 21 Z M9 3 L9 18 M15 6 L15 21',
  layers: 'M12 3 L22 8 L12 13 L2 8 Z M2 12.5 L12 17.5 L22 12.5 M2 17 L12 22 L22 17',
  user: 'M12 12 A4.5 4.5 0 1 0 12 3 A4.5 4.5 0 1 0 12 12 Z M4 21 C4 16.5 7.5 14.5 12 14.5 C16.5 14.5 20 16.5 20 21',
  users:
    'M9 11 A3.8 3.8 0 1 0 9 3.4 A3.8 3.8 0 1 0 9 11 Z M2 20 C2 16.2 5 14.4 9 14.4 C13 14.4 16 16.2 16 20 M16 3.6 A3.6 3.6 0 0 1 16 10.8 M18.5 14.6 C20.6 15.3 22 17 22 20',
  settings:
    'M12 15.2 A3.2 3.2 0 1 0 12 8.8 A3.2 3.2 0 1 0 12 15.2 Z M12 2 L13.6 4.8 L16.8 4 L17.2 7.2 L20.2 8.4 L18.8 11.4 L20.8 14 L17.9 15.5 L17.6 18.7 L14.4 18.4 L12 20.6 L9.6 18.4 L6.4 18.7 L6.1 15.5 L3.2 14 L5.2 11.4 L3.8 8.4 L6.8 7.2 L7.2 4 L10.4 4.8 Z',
  logout: 'M15 4 L19 4 L19 20 L15 20 M10 8 L6 12 L10 16 M6 12 L15 12',
  close: 'M6 6 L18 18 M18 6 L6 18',
  collapse: 'M6 15 L12 9 L18 15',
  expand: 'M6 9 L12 15 L18 9',
  reset: 'M4 12 A8 8 0 1 0 6.5 6.2 M4 3.5 L4 8 L8.5 8',
  search: 'M10.5 17 A6.5 6.5 0 1 0 10.5 4 A6.5 6.5 0 1 0 10.5 17 Z M15.2 15.2 L20 20',
  target: 'M12 19 A7 7 0 1 0 12 5 A7 7 0 1 0 12 19 Z M12 14.5 A2.5 2.5 0 1 0 12 9.5 A2.5 2.5 0 1 0 12 14.5 Z M12 2 L12 5 M12 19 L12 22 M2 12 L5 12 M19 12 L22 12',
} as const

export type IconName = keyof typeof paths

interface IconProps extends Omit<ComponentProps<'svg'>, 'children'> {
  name: IconName
}

/** A HUD icon; decorative unless given an aria-label. */
export function Icon({ name, className, ...props }: IconProps) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden={props['aria-label'] ? undefined : true}
      className={cn('size-5 shrink-0', className)}
      {...props}
    >
      <path d={paths[name]} />
    </svg>
  )
}
