import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { formatSecurity } from '../../map/security'
import { securityColor } from '../../theme/themes'
import { Icon, type IconName } from '../ui/Icon'
import { cn } from '../ui/styles'
import type { Destination } from './useDestination'

export interface SystemMenuItem {
  label: string
  icon?: IconName
  /** A character: a green dot when online, grey when not (instead of an icon). */
  online?: boolean
  title?: string
  disabled?: boolean
  onSelect?: () => void
  /** A submenu, opened beside the item (hover, click or →) instead of choosing it. */
  items?: SystemMenuItem[]
}

/** The area a menu is kept inside, in viewport pixels. */
type Area = { left: number; top: number; right: number; bottom: number }

/** "Navigate to": the destination for every tracked character (All), or for one of the account's characters. */
export function navigateItem({ tracked, usable, busy, setFor }: Destination): SystemMenuItem {
  return {
    label: 'Navigate to',
    icon: 'flag',
    items: [
      {
        label: tracked.length > 0 ? `All (${tracked.length})` : 'All',
        icon: 'flag',
        disabled: tracked.length === 0 || busy,
        title: tracked.length === 0 ? 'No tracked characters' : `Every tracked character: ${tracked.map((c) => c.name).join(', ')}`,
        onSelect: () => void setFor(tracked),
      },
      ...usable.map((c) => ({
        label: c.name,
        online: c.online,
        disabled: busy,
        title: c.online ? 'Online' : 'Not online: the destination shows when it logs in, if at all',
        onSelect: () => void setFor([c]),
      })),
    ],
  }
}

/**
 * A system's menu, at the pointer (right click) or the finger (long press), kept inside the map, or (fixed)
 * inside the window. It closes on Escape, a click or tap outside, or after an item is chosen; the map closes
 * it when moved, and a fixed one closes on scrolling.
 */
export function SystemContextMenu({
  name,
  security,
  x,
  y,
  items,
  onClose,
  fixed = false,
}: {
  name: string
  security: number
  /** Relative to the map, or to the window when fixed. */
  x: number
  y: number
  items: SystemMenuItem[]
  onClose: () => void
  /** Drawn over the whole page (from a panel), not inside the map. */
  fixed?: boolean
}) {
  const menuRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState<string | null>(null)
  // The latest, so a new function from the opener doesn't set the menu up again (and move the keyboard's focus).
  const close = useRef(onClose)
  useEffect(() => {
    close.current = onClose
  })

  const area = (): Area => {
    const parent = fixed ? null : (menuRef.current?.offsetParent as HTMLElement | null)
    return parent?.getBoundingClientRect() ?? { left: 0, top: 0, right: window.innerWidth, bottom: window.innerHeight }
  }

  // Shift left or up when the menu would leave its area (before it is painted).
  useLayoutEffect(() => {
    const menu = menuRef.current
    if (!menu) return
    const { left, top, right, bottom } = area()
    menu.style.left = `${Math.max(8, Math.min(x, right - left - menu.offsetWidth - 8))}px`
    menu.style.top = `${Math.max(8, Math.min(y, bottom - top - menu.offsetHeight - 8))}px`
  }, [x, y, fixed])

  useEffect(() => {
    menuRef.current?.querySelector<HTMLButtonElement>('[role="menuitem"]')?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') close.current()
    }
    const onPointer = (e: PointerEvent) => {
      if (!menuRef.current?.contains(e.target as Node)) close.current()
    }
    const onScroll = (e: Event) => {
      if (!menuRef.current?.contains(e.target as Node)) close.current()
    }
    window.addEventListener('keydown', onKey)
    window.addEventListener('pointerdown', onPointer, true)
    if (fixed) window.addEventListener('scroll', onScroll, true)
    return () => {
      window.removeEventListener('keydown', onKey)
      window.removeEventListener('pointerdown', onPointer, true)
      window.removeEventListener('scroll', onScroll, true)
    }
  }, [fixed])

  const menu = (
    <div
      ref={menuRef}
      role="menu"
      aria-label={name}
      className={cn(
        fixed ? 'fixed' : 'absolute',
        'z-[1100] grid min-w-48 border border-line-strong bg-surface/95 py-1 shadow-[0_8px_28px_rgb(0_0_0/0.45)] backdrop-blur-sm',
      )}
      style={{ left: x, top: y }}
      onContextMenu={(e) => {
        e.preventDefault()
        e.stopPropagation()
      }}
      // From a portal, React events still reach the panel that opened it: a click must not select its card,
      // nor the focus moving through the menu count as being on the card.
      onClick={(e) => e.stopPropagation()}
      onFocus={(e) => e.stopPropagation()}
      onBlur={(e) => e.stopPropagation()}
    >
      <div className="flex items-baseline gap-2 border-b border-line px-3 pt-1 pb-2">
        <span className="font-display text-sm font-semibold text-fg">{name}</span>
        <span className="font-mono text-[0.75rem] font-semibold" style={{ color: securityColor(security) }}>
          {formatSecurity(security)}
        </span>
      </div>
      <MenuItems items={items} open={open} setOpen={setOpen} area={area} onClose={onClose} />
    </div>
  )
  // Fixed: out of the panels, whose blur would otherwise hold it inside them.
  return fixed ? createPortal(menu, document.body) : menu
}

const itemClass =
  'flex w-full cursor-pointer items-center gap-2 px-3 py-1.5 text-left text-[0.8125rem] text-fg hover:bg-primary-soft focus-visible:bg-primary-soft ' +
  'focus-visible:outline-none disabled:cursor-default disabled:opacity-50 disabled:hover:bg-transparent'

function MenuItems({
  items,
  open,
  setOpen,
  area,
  onClose,
}: {
  items: SystemMenuItem[]
  /** The label of the item whose submenu is open, or null. */
  open: string | null
  setOpen: (label: string | null) => void
  area: () => Area
  onClose: () => void
}) {
  return items.map((item) => {
    const mark =
      item.online !== undefined ? (
        <span className="grid size-4 place-items-center">
          <span className={cn('size-1.5 rounded-full', item.online ? 'bg-ok' : 'bg-fg-faint')} />
        </span>
      ) : item.icon ? (
        <Icon name={item.icon} className="size-4 text-primary" />
      ) : (
        <span className="size-4" />
      )

    if (!item.items) {
      return (
        <button
          key={item.label}
          type="button"
          role="menuitem"
          disabled={item.disabled}
          title={item.title}
          onPointerEnter={(e) => e.pointerType === 'mouse' && setOpen(null)}
          onClick={() => {
            item.onSelect?.()
            onClose()
          }}
          className={cn(itemClass, item.online === false && 'text-fg-muted')}
        >
          {mark}
          {item.label}
        </button>
      )
    }

    const isOpen = open === item.label
    return (
      <div key={item.label} className="relative">
        <button
          type="button"
          role="menuitem"
          aria-haspopup="menu"
          aria-expanded={isOpen}
          disabled={item.disabled}
          title={item.title}
          onPointerEnter={(e) => e.pointerType === 'mouse' && setOpen(item.label)}
          onClick={() => setOpen(isOpen ? null : item.label)}
          onKeyDown={(e) => {
            if (e.key !== 'ArrowRight') return
            e.preventDefault()
            setOpen(item.label)
          }}
          className={cn(itemClass, isOpen && 'bg-primary-soft')}
        >
          {mark}
          {item.label}
          <Icon name="chevron-right" className="ml-auto size-3.5 text-fg-muted" />
        </button>
        {isOpen && <Submenu label={item.label} items={item.items} area={area} onBack={() => setOpen(null)} onClose={onClose} />}
      </div>
    )
  })
}

/** A submenu beside its item: on the right, else on the left, else wherever it fits in the area. */
function Submenu({
  label,
  items,
  area,
  onBack,
  onClose,
}: {
  label: string
  items: SystemMenuItem[]
  area: () => Area
  onBack: () => void
  onClose: () => void
}) {
  const ref = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState<string | null>(null)

  useLayoutEffect(() => {
    const sub = ref.current
    const anchor = sub?.parentElement
    if (!sub || !anchor) return
    const bounds = area()
    const a = anchor.getBoundingClientRect()
    const width = sub.offsetWidth
    const height = sub.offsetHeight
    // Line its first item up with the anchor (past the border and padding).
    const left =
      a.right + width <= bounds.right - 8
        ? a.width
        : a.left - width >= bounds.left + 8
          ? -width
          : Math.max(bounds.left + 8, bounds.right - 8 - width) - a.left
    const top = Math.max(bounds.top + 8, Math.min(a.top - 5, bounds.bottom - 8 - height)) - a.top
    sub.style.left = `${left}px`
    sub.style.top = `${top}px`
    // Opened from the keyboard: into it. A pointer keeps the focus where it is.
    if (anchor.contains(document.activeElement)) sub.querySelector<HTMLButtonElement>('[role="menuitem"]:not(:disabled)')?.focus()
  }, [])

  return (
    <div
      ref={ref}
      role="menu"
      aria-label={label}
      className="absolute top-0 left-full grid max-h-80 min-w-44 overflow-y-auto border border-line-strong bg-surface py-1 shadow-[0_8px_28px_rgb(0_0_0/0.45)]"
      onKeyDown={(e) => {
        if (e.key !== 'ArrowLeft') return
        e.preventDefault()
        e.stopPropagation()
        ;(ref.current?.parentElement?.firstElementChild as HTMLElement | null)?.focus()
        onBack()
      }}
    >
      <MenuItems items={items} open={open} setOpen={setOpen} area={area} onClose={onClose} />
    </div>
  )
}
