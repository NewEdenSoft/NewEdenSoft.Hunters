import { useSuspenseQuery } from '@tanstack/react-query'
import { accountQuery } from '../../api/account'
import { getMarker, useCharacterPrefs } from '../../characters/characterPrefs'
import { clearFocus, toggleFollow, useMapFocus } from '../../map/mapFocus'
import { Icon } from '../ui/Icon'
import { cn } from '../ui/styles'
import { MarkerBadge } from './MarkerBadge'

/**
 * The focused character, at the top of the map: its marker and name, Follow (keep the map on it as
 * it moves; dragging the map stops following) and dismiss.
 */
export function FocusChip() {
  const { focused, followed } = useMapFocus()
  const { data: account } = useSuspenseQuery(accountQuery)
  useCharacterPrefs()
  const character = account.characters.find((c) => c.id === focused)
  if (!character) return null
  const following = followed === character.id

  return (
    <div className="pointer-events-auto absolute top-3 left-1/2 z-[1000] flex -translate-x-1/2 items-center gap-2 border border-line-strong bg-surface/92 py-1 pr-1 pl-2 shadow-[0_8px_28px_rgb(0_0_0/0.45)] backdrop-blur-sm">
      <MarkerBadge marker={getMarker(character.id, character.name)} className="size-5" />
      <span className="max-w-48 truncate font-display text-[0.6875rem] font-semibold tracking-[0.16em] text-fg uppercase">{character.name}</span>
      <button
        type="button"
        aria-pressed={following}
        title="Keep the map on this character as it moves. Dragging the map stops following."
        onClick={() => toggleFollow(character.id)}
        className={cn(
          'cursor-pointer px-2.5 py-1 font-display text-[0.6875rem] font-semibold tracking-[0.14em] uppercase focus-visible:outline-2 focus-visible:outline-fg',
          following ? 'bg-primary text-on-primary' : 'text-primary hover:bg-primary-soft',
        )}
      >
        {following ? 'Following' : 'Follow'}
      </button>
      <button
        type="button"
        aria-label="Stop focusing on this character"
        title="Stop focusing on this character"
        onClick={clearFocus}
        className="grid size-6 cursor-pointer place-items-center text-fg-muted hover:bg-primary-soft hover:text-fg focus-visible:outline-2 focus-visible:outline-fg"
      >
        <Icon name="close" className="size-4" />
      </button>
    </div>
  )
}
