import { cn } from './styles'

interface SwitchProps {
  checked: boolean
  onChange: (checked: boolean) => void
  label: string
  disabled?: boolean
}

/** An on/off switch. The label is for screen readers and the tooltip; show visible text beside it. */
export function Switch({ checked, onChange, label, disabled }: SwitchProps) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      title={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={cn(
        'relative h-5 w-9 shrink-0 cursor-pointer border transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-fg disabled:cursor-not-allowed disabled:opacity-50',
        checked ? 'border-primary bg-primary/30' : 'border-line-strong bg-bg',
      )}
    >
      <span className={cn('absolute top-0.5 size-3.5 transition-[left]', checked ? 'left-4.5 bg-primary' : 'left-0.5 bg-fg-faint')} />
    </button>
  )
}
