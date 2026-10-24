import { useEffect, useState } from 'react'
import { Icon } from '../components/ui/Icon'
import { cn } from '../components/ui/styles'
import { onFocusRequested } from '../map/mapFocus'
import type { PanelId } from './definitions'
import type { PanelItem } from './Panels'

/**
 * The panels on a phone: a sheet along the bottom of the map with a tab per panel. Tapping a tab
 * opens the sheet on that panel (up to half the screen, scrolling inside); tapping the open tab
 * closes it again, so the map gets the whole screen. Picking a character to show on the map closes
 * it too, so the map can be seen moving there.
 */
export function BottomSheet({ items }: { items: PanelItem[] }) {
  const [active, setActive] = useState<PanelId>(items[0].definition.id)
  const [open, setOpen] = useState(false)
  const current = items.find((i) => i.definition.id === active) ?? items[0]

  useEffect(() => onFocusRequested(() => setOpen(false)), [])

  const select = (id: PanelId) => {
    if (id === active) setOpen(!open)
    else {
      setActive(id)
      setOpen(true)
    }
  }

  return (
    <section
      aria-label="Panels"
      className="pointer-events-auto absolute inset-x-0 bottom-0 z-[900] flex flex-col border-t border-line-strong bg-surface/95 shadow-[0_-8px_28px_rgb(0_0_0/0.45)] backdrop-blur-sm"
    >
      <div role="tablist" aria-label="Panels" className="flex shrink-0">
        {items.map(({ definition: d }) => {
          const selected = d.id === current.definition.id
          return (
            <button
              key={d.id}
              type="button"
              role="tab"
              id={`sheet-tab-${d.id}`}
              aria-selected={selected}
              aria-expanded={selected && open}
              aria-controls="sheet-panel"
              onClick={() => select(d.id)}
              title={d.title}
              className={cn(
                'flex cursor-pointer items-center justify-center gap-2 px-3 py-2.5 font-display text-[0.6875rem] font-semibold tracking-[0.16em] whitespace-nowrap uppercase',
                'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-fg',
                selected ? 'flex-1' : 'flex-none',
                selected && open ? 'bg-primary-soft text-primary' : 'text-fg-muted',
              )}
            >
              <Icon name={d.icon} className="size-4" />
              {/* Only the selected tab has room for its name; the others show their icon. */}
              <span className={selected ? undefined : 'sr-only'}>{d.title}</span>
              {selected && <Icon name={open ? 'expand' : 'collapse'} className="size-3.5" />}
            </button>
          )
        })}
      </div>
      {open && (
        <div
          id="sheet-panel"
          role="tabpanel"
          aria-labelledby={`sheet-tab-${current.definition.id}`}
          className="max-h-[50dvh] overflow-y-auto border-t border-line p-3 pb-[max(0.75rem,env(safe-area-inset-bottom))]"
        >
          {current.content}
        </div>
      )}
    </section>
  )
}
