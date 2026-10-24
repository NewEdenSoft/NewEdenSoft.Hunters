import type { MapModel } from '../../map/model'
import { formatSecurity } from '../../map/security'
import { securityColor } from '../../theme/themes'
import { Icon } from '../ui/Icon'

/**
 * The selected system (the one last clicked or searched for), under the system search: its name,
 * security and region, Focus (make it the staging point) and dismiss. Escape dismisses it too.
 */
export function SelectionCard({
  model,
  index,
  focused,
  onFocus,
  onDismiss,
}: {
  model: MapModel
  index: number
  /** It is the focused system already: no Focus button. */
  focused: boolean
  onFocus: () => void
  onDismiss: () => void
}) {
  const security = model.security[index]
  return (
    <div className="pointer-events-auto flex max-w-full items-center gap-2 border border-line-strong bg-surface/92 py-1 pr-1 pl-2.5 shadow-[0_8px_28px_rgb(0_0_0/0.45)] backdrop-blur-sm">
      <span className="font-display text-[0.6875rem] font-semibold tracking-[0.16em] text-fg-muted uppercase">Selected</span>
      <span className="min-w-0 truncate font-display text-sm font-semibold text-fg">{model.names[index]}</span>
      <span className="font-mono text-[0.75rem] font-semibold" style={{ color: securityColor(security) }}>
        {formatSecurity(security)}
      </span>
      <span className="min-w-0 truncate text-[0.75rem] text-fg-muted">{model.regionNames.get(model.regionIds[index])}</span>
      {!focused && (
        <button
          type="button"
          title="Make this system the focus: the staging point for nearby intel and jump range"
          onClick={onFocus}
          className="cursor-pointer px-2.5 py-1 font-display text-[0.6875rem] font-semibold tracking-[0.14em] text-primary uppercase hover:bg-primary-soft focus-visible:outline-2 focus-visible:outline-fg"
        >
          Focus
        </button>
      )}
      <button
        type="button"
        aria-label="Clear the selection"
        title="Clear the selection (Esc)"
        onClick={onDismiss}
        className="grid size-6 cursor-pointer place-items-center text-fg-muted hover:bg-primary-soft hover:text-fg focus-visible:outline-2 focus-visible:outline-fg"
      >
        <Icon name="close" className="size-3.5" />
      </button>
    </div>
  )
}
