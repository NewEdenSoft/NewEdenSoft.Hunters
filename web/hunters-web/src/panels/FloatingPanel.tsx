import { useLayoutEffect, useRef, useState, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react'
import { Icon, type IconName } from '../components/ui/Icon'
import { DEFAULT_MIN_HEIGHT, DEFAULT_MIN_WIDTH, type PanelDefinition } from './definitions'
import { clampRect, snapMove, snapResize, type Rect } from './geometry'
import { usePanelLayer } from './panelLayerContext'
import { bringToFront, updatePanel, usePanel } from './panelStore'

/** Arrow keys move a focused title bar this far; with Shift, one pixel. */
const KEY_STEP = 16

interface Drag {
  mode: 'move' | 'resize'
  pointerX: number
  pointerY: number
  start: Rect
}

/**
 * A panel floating over the map: drag it by its title bar, resize it from its corner (unless its
 * definition turns that off), collapse it to its title bar or close it. It snaps to the map's edges
 * and to other panels, stays in view, comes to the front when clicked, and remembers its layout in
 * this browser (panelStore.ts). With the title bar focused, the arrow keys move it and Escape
 * collapses or expands it.
 */
export function FloatingPanel({ definition, children }: { definition: PanelDefinition; children: ReactNode }) {
  const { id, title, icon } = definition
  const resizable = definition.resizable ?? true
  const min = { width: definition.minWidth ?? DEFAULT_MIN_WIDTH, height: definition.minHeight ?? DEFAULT_MIN_HEIGHT }
  const state = usePanel(id)
  const { area, rects } = usePanelLayer()
  const ref = useRef<HTMLElement>(null)
  const drag = useRef<Drag | null>(null)
  const [measuredHeight, setMeasuredHeight] = useState(0)

  // Until the user resizes it, a panel fits its content; measure it to keep it in view and to snap to it.
  useLayoutEffect(() => {
    const element = ref.current
    if (!element) return
    const observer = new ResizeObserver(() => setMeasuredHeight(element.offsetHeight))
    observer.observe(element)
    return () => observer.disconnect()
  }, [state.open])

  // A negative x places the panel from the right edge, until it is first moved.
  const x = state.x < 0 ? area.width + state.x - state.width : state.x
  const rect = clampRect({ x, y: state.y, width: state.width, height: measuredHeight }, area)

  useLayoutEffect(() => {
    if (!state.open) return
    rects.set(id, rect)
    return () => {
      rects.delete(id)
    }
  })

  if (!state.open) return null

  const others = () => [...rects].filter(([other]) => other !== id).map(([, r]) => r)

  const startDrag = (mode: Drag['mode'], e: PointerEvent<HTMLElement>) => {
    if (e.button !== 0) return
    e.currentTarget.setPointerCapture(e.pointerId)
    drag.current = { mode, pointerX: e.clientX, pointerY: e.clientY, start: { ...rect, height: ref.current?.offsetHeight ?? rect.height } }
    e.preventDefault()
  }

  const onDrag = (e: PointerEvent<HTMLElement>) => {
    const d = drag.current
    if (!d) return
    const dx = e.clientX - d.pointerX
    const dy = e.clientY - d.pointerY
    if (d.mode === 'move') {
      const next = clampRect(snapMove({ ...d.start, x: d.start.x + dx, y: d.start.y + dy }, others(), area), area)
      updatePanel(id, { x: next.x, y: next.y }, { persist: false })
    } else {
      const next = snapResize({ ...d.start, width: d.start.width + dx, height: d.start.height + dy }, others(), area, min)
      updatePanel(id, { width: next.width, height: next.height }, { persist: false })
    }
  }

  const endDrag = () => {
    if (!drag.current) return
    drag.current = null
    updatePanel(id, {})
  }

  const onTitleKey = (e: KeyboardEvent<HTMLElement>) => {
    const step = e.shiftKey ? 1 : KEY_STEP
    const moves: Record<string, [number, number]> = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }
    if (moves[e.key]) {
      const [dx, dy] = moves[e.key]
      const next = clampRect({ ...rect, x: rect.x + dx, y: rect.y + dy }, area)
      updatePanel(id, { x: next.x, y: next.y })
      e.preventDefault()
    } else if (e.key === 'Escape') {
      updatePanel(id, { collapsed: !state.collapsed })
      e.preventDefault()
    }
  }

  return (
    <section
      ref={ref}
      aria-label={title}
      onPointerDownCapture={() => bringToFront(id)}
      onFocusCapture={() => bringToFront(id)}
      style={{ left: rect.x, top: rect.y, width: rect.width, height: state.collapsed ? undefined : state.height, zIndex: state.z }}
      className="pointer-events-auto absolute flex max-h-full flex-col border border-line-strong bg-surface/92 shadow-[0_8px_28px_rgb(0_0_0/0.45)] backdrop-blur-sm"
    >
      <header
        tabIndex={0}
        aria-label={`${title} panel. Arrow keys move it, Escape collapses it.`}
        onPointerDown={(e) => {
          if (!(e.target as Element).closest('button')) startDrag('move', e)
        }}
        onPointerMove={onDrag}
        onPointerUp={endDrag}
        onPointerCancel={endDrag}
        onKeyDown={onTitleKey}
        className={
          'flex shrink-0 cursor-move touch-none items-center gap-2 bg-primary-soft py-1 pr-1 pl-2.5 select-none ' +
          'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-fg ' +
          (state.collapsed ? '' : 'border-b border-line')
        }
      >
        <Icon name={icon} className="size-4 text-primary" />
        <h2 className="min-w-0 flex-1 truncate font-display text-[0.6875rem] font-semibold tracking-[0.16em] text-primary uppercase">
          {title}
        </h2>
        <PanelButton
          icon={state.collapsed ? 'expand' : 'collapse'}
          label={state.collapsed ? `Expand ${title}` : `Collapse ${title}`}
          onClick={() => updatePanel(id, { collapsed: !state.collapsed })}
        />
        <PanelButton icon="close" label={`Close ${title}`} onClick={() => updatePanel(id, { open: false })} />
      </header>

      {!state.collapsed && <div className="min-h-0 flex-1 overflow-auto p-3">{children}</div>}

      {resizable && !state.collapsed && (
        <div
          aria-hidden
          onPointerDown={(e) => startDrag('resize', e)}
          onPointerMove={onDrag}
          onPointerUp={endDrag}
          onPointerCancel={endDrag}
          className="absolute right-0 bottom-0 size-4 cursor-nwse-resize touch-none text-line-strong hover:text-primary"
        >
          <svg viewBox="0 0 16 16" className="size-full" stroke="currentColor" strokeWidth={1.5}>
            <path d="M15 6 L6 15 M15 11 L11 15" />
          </svg>
        </div>
      )}
    </section>
  )
}

function PanelButton({ icon, label, onClick }: { icon: IconName; label: string; onClick: () => void }) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onClick={onClick}
      className="grid size-6 cursor-pointer place-items-center text-fg-muted hover:bg-primary-soft hover:text-fg focus-visible:outline-2 focus-visible:outline-fg"
    >
      <Icon name={icon} className="size-4" />
    </button>
  )
}
