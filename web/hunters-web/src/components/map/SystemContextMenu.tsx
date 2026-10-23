import { useEffect, useLayoutEffect, useRef } from 'react'
import type { MapModel } from '../../map/model'
import { formatSecurity } from '../../map/security'
import { securityColor } from '../../theme/themes'
import { Icon, type IconName } from '../ui/Icon'

export interface SystemMenuItem {
  label: string
  icon: IconName
  onSelect: () => void
}

/**
 * A system's menu, at the pointer (right click) or the finger (long press), kept inside the map. It
 * closes on Escape, a click or tap outside, or after an item is chosen; the map closes it when moved.
 */
export function SystemContextMenu({
  model,
  index,
  x,
  y,
  items,
  onClose,
}: {
  model: MapModel
  index: number
  x: number
  y: number
  items: SystemMenuItem[]
  onClose: () => void
}) {
  const menuRef = useRef<HTMLDivElement>(null)

  // Shift left or up when the menu would leave the map (before it is painted).
  useLayoutEffect(() => {
    const menu = menuRef.current
    const area = menu?.offsetParent as HTMLElement | null
    if (!menu || !area) return
    menu.style.left = `${Math.max(8, Math.min(x, area.clientWidth - menu.offsetWidth - 8))}px`
    menu.style.top = `${Math.max(8, Math.min(y, area.clientHeight - menu.offsetHeight - 8))}px`
  }, [x, y])

  useEffect(() => {
    menuRef.current?.querySelector<HTMLButtonElement>('[role="menuitem"]')?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    const onPointer = (e: PointerEvent) => {
      if (!menuRef.current?.contains(e.target as Node)) onClose()
    }
    window.addEventListener('keydown', onKey)
    window.addEventListener('pointerdown', onPointer, true)
    return () => {
      window.removeEventListener('keydown', onKey)
      window.removeEventListener('pointerdown', onPointer, true)
    }
  }, [onClose])

  const security = model.security[index]
  return (
    <div
      ref={menuRef}
      role="menu"
      aria-label={model.names[index]}
      className="absolute z-[1100] grid min-w-48 border border-line-strong bg-surface/95 py-1 shadow-[0_8px_28px_rgb(0_0_0/0.45)] backdrop-blur-sm"
      style={{ left: x, top: y }}
      onContextMenu={(e) => e.preventDefault()}
    >
      <div className="flex items-baseline gap-2 border-b border-line px-3 pt-1 pb-2">
        <span className="font-display text-sm font-semibold text-fg">{model.names[index]}</span>
        <span className="font-mono text-[0.75rem] font-semibold" style={{ color: securityColor(security) }}>
          {formatSecurity(security)}
        </span>
      </div>
      {items.map((item) => (
        <button
          key={item.label}
          type="button"
          role="menuitem"
          onClick={() => {
            item.onSelect()
            onClose()
          }}
          className="flex cursor-pointer items-center gap-2 px-3 py-1.5 text-left text-[0.8125rem] text-fg hover:bg-primary-soft focus-visible:bg-primary-soft focus-visible:outline-none"
        >
          <Icon name={item.icon} className="size-4 text-primary" />
          {item.label}
        </button>
      ))}
    </div>
  )
}
