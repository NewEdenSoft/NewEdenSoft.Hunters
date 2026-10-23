import { useEffect, useId, useMemo, useRef, useState } from 'react'
import type { MapModel } from '../../map/model'
import { formatSecurity } from '../../map/security'
import { searchSystems } from '../../map/systemSearch'
import { securityColor } from '../../theme/themes'
import { Icon } from '../ui/Icon'
import { cn } from '../ui/styles'

/**
 * Finds a system by name and focuses it. Showing the focused system (with a button to clear it) when
 * not in use; "/" anywhere on the page starts a search. Up and Down choose, Enter focuses, Escape closes.
 */
export function SystemSearch({
  model,
  selected,
  onSelect,
  onClear,
}: {
  model: MapModel
  /** The focused system's index, or -1. */
  selected: number
  onSelect: (index: number) => void
  onClear: () => void
}) {
  const inputRef = useRef<HTMLInputElement>(null)
  const listId = useId()
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const [open, setOpen] = useState(false)
  const results = useMemo(() => searchSystems(model, query), [model, query])

  // "/" starts a search, unless the user is typing somewhere else.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== '/' || e.ctrlKey || e.metaKey || e.altKey) return
      const target = e.target as HTMLElement | null
      if (target && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))) return
      e.preventDefault()
      inputRef.current?.focus()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const choose = (index: number) => {
    onSelect(index)
    setQuery('')
    setOpen(false)
    inputRef.current?.blur()
  }

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault()
      if (results.length === 0) return
      setOpen(true)
      setActive((a) => (a + (e.key === 'ArrowDown' ? 1 : results.length - 1)) % results.length)
    } else if (e.key === 'Enter') {
      if (results[active] !== undefined) choose(results[active])
    } else if (e.key === 'Escape') {
      if (query) setQuery('')
      else inputRef.current?.blur()
      setOpen(false)
    }
  }

  const showChip = selected >= 0 && !open && query === ''
  const expanded = open && results.length > 0

  return (
    <div className="pointer-events-auto relative w-80 max-w-full">
      <div className="flex h-9 items-center gap-2 border border-line-strong bg-surface/92 px-2.5 shadow-[0_8px_28px_rgb(0_0_0/0.45)] backdrop-blur-sm focus-within:border-primary">
        <Icon name={showChip ? 'target' : 'search'} className={cn('size-4', showChip ? 'text-primary' : 'text-fg-muted')} />
        <input
          ref={inputRef}
          type="text"
          role="combobox"
          aria-label="Find a system"
          aria-expanded={expanded}
          aria-controls={listId}
          aria-autocomplete="list"
          aria-activedescendant={expanded ? `${listId}-${active}` : undefined}
          placeholder={showChip ? '' : 'Find a system  ( / )'}
          spellCheck={false}
          autoComplete="off"
          value={query}
          onChange={(e) => {
            setQuery(e.target.value)
            setActive(0)
            setOpen(true)
          }}
          onFocus={() => setOpen(true)}
          onBlur={() => setOpen(false)}
          onKeyDown={onKeyDown}
          className={cn('min-w-0 flex-1 bg-transparent text-sm text-fg outline-none placeholder:text-fg-faint', showChip && 'absolute inset-0 opacity-0')}
        />
        {showChip && (
          <>
            <span className="min-w-0 flex-1 truncate">
              <span className="font-display text-sm font-semibold text-fg">{model.names[selected]}</span>
              <span className="ml-2 font-mono text-[0.75rem] font-semibold" style={{ color: securityColor(model.security[selected]) }}>
                {formatSecurity(model.security[selected])}
              </span>
              <span className="ml-2 text-[0.75rem] text-fg-muted">{model.regionNames.get(model.regionIds[selected])}</span>
            </span>
            <button
              type="button"
              aria-label="Clear the focused system"
              title="Clear the focused system"
              onClick={onClear}
              className="relative grid size-6 cursor-pointer place-items-center text-fg-muted hover:bg-primary-soft hover:text-fg focus-visible:outline-2 focus-visible:outline-fg"
            >
              <Icon name="close" className="size-4" />
            </button>
          </>
        )}
      </div>

      {expanded && (
        <ul
          id={listId}
          role="listbox"
          aria-label="Systems"
          className="absolute inset-x-0 top-full z-10 mt-1 grid border border-line-strong bg-surface/95 py-1 shadow-[0_8px_28px_rgb(0_0_0/0.45)] backdrop-blur-sm"
        >
          {results.map((index, i) => (
            <li
              key={index}
              id={`${listId}-${i}`}
              role="option"
              aria-selected={i === active}
              // Keep the input focused, so the blur doesn't close the list before the click.
              onMouseDown={(e) => e.preventDefault()}
              onMouseEnter={() => setActive(i)}
              onClick={() => choose(index)}
              className={cn('flex cursor-pointer items-baseline gap-2 px-3 py-1.5', i === active && 'bg-primary-soft')}
            >
              <span className="font-display text-sm font-semibold text-fg">{model.names[index]}</span>
              <span className="font-mono text-[0.75rem] font-semibold" style={{ color: securityColor(model.security[index]) }}>
                {formatSecurity(model.security[index])}
              </span>
              <span className="ml-auto truncate text-[0.75rem] text-fg-muted">{model.regionNames.get(model.regionIds[index])}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
