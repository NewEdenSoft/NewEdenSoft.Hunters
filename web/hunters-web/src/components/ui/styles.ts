/** Joins class names, skipping empty ones. */
export function cn(...classes: (string | false | null | undefined)[]): string {
  return classes.filter(Boolean).join(' ')
}

/** Centred page width with the side gutter. */
export const wrap = 'mx-auto w-full max-w-295 px-4'

export type ButtonVariant = 'default' | 'primary' | 'danger' | 'ghost'

const buttonBase =
  'chamfer inline-block cursor-pointer px-4.5 py-2.25 font-display text-[0.8125rem] font-semibold uppercase ' +
  'tracking-[0.14em] no-underline transition-colors focus-visible:outline-none focus-visible:inset-ring-2 ' +
  'focus-visible:inset-ring-fg disabled:cursor-not-allowed disabled:opacity-50'

const buttonVariants: Record<ButtonVariant, string> = {
  default: 'bg-primary/10 text-primary inset-ring inset-ring-primary/60 hover:bg-primary/20',
  primary: 'bg-primary text-on-primary hover:bg-[color-mix(in_oklab,var(--primary)_85%,white)]',
  danger: 'bg-danger/10 text-danger inset-ring inset-ring-danger/60 hover:bg-danger/20',
  ghost: 'bg-transparent text-primary hover:bg-primary-soft',
}

/** Classes for a HUD button; also for links styled as buttons. */
export function buttonClass(variant: ButtonVariant = 'default', className?: string): string {
  return cn(buttonBase, buttonVariants[variant], className)
}
