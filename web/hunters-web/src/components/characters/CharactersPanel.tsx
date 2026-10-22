import { useSuspenseQuery } from '@tanstack/react-query'
import { accountQuery, type CharacterInfo } from '../../api/account'
import { characterPortraitUrl } from '../../api/eveImages'
import { getMarker, useCharacterPrefs } from '../../characters/characterPrefs'
import { useCharacterStatus } from '../../characters/useCharacterStatus'
import { focusCharacter, toggleFollow, useMapFocus } from '../../map/mapFocus'
import { MARKER_ICONS } from '../../characters/markers'
import { cn } from '../ui/styles'
import { WarningIcon } from '../ui/WarningIcon'
import { MarkerBadge } from './MarkerBadge'

/**
 * The account's characters at a glance: marker, portrait, online, system and ship, live from the
 * tracker. Main first, then by name, so rows do not move as characters log in and out. Clicking a
 * row shows the character on the map; the target button follows it. The same as the desktop's
 * Characters widget.
 */
export function CharactersPanel() {
  const { data: account } = useSuspenseQuery(accountQuery)
  const characters = [...account.characters].sort((a, b) => Number(b.isMain) - Number(a.isMain) || a.name.localeCompare(b.name))

  return (
    <ul className="-m-3 grid">
      {characters.map((c) => (
        <CharacterRow key={c.id} character={c} />
      ))}
    </ul>
  )
}

const FOLLOW_ICON = { kind: 'icon', value: 'target' } as const satisfies { kind: 'icon'; value: keyof typeof MARKER_ICONS }

function CharacterRow({ character }: { character: CharacterInfo }) {
  useCharacterPrefs()
  const status = useCharacterStatus(character)
  const following = useMapFocus().followed === character.id

  return (
    <li title={status.details} className="relative flex items-center gap-2.5 border-b border-line px-3 py-2 last:border-b-0 hover:bg-primary-soft">
      {/* The whole row shows the character on the map; the follow button sits above it. */}
      <button
        type="button"
        aria-label={`Show ${character.name} on the map`}
        onClick={() => focusCharacter(character.id)}
        className="absolute inset-0 cursor-pointer focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-fg"
      />
      <MarkerBadge marker={getMarker(character.id, character.name)} className="size-5.5" />

      <div className="relative size-9 shrink-0">
        <img src={characterPortraitUrl(character.id, 64)} alt="" width={36} height={36} className="size-9 border border-line-strong bg-bg" />
        {status.needsLogin && (
          <span className="absolute -right-1 -bottom-1 border border-warning bg-bg p-px">
            <WarningIcon className="size-2.5" />
          </span>
        )}
      </div>

      <div className="grid min-w-0 flex-1">
        <div className="flex min-w-0 items-center gap-1.5">
          <span aria-label={status.online ? 'Online' : 'Offline'} className={cn('size-1.75 shrink-0 rounded-full', status.online ? 'bg-ok' : 'bg-fg-faint')} />
          <span className="truncate font-display text-sm font-semibold">{character.name}</span>
        </div>
        <span className={cn('truncate text-[0.78rem] leading-snug', status.problem ? 'text-warning' : 'text-fg-muted')}>{status.place}</span>
        {status.ship && <span className="truncate text-[0.78rem] leading-snug text-fg-muted">{status.ship}</span>}
      </div>

      <button
        type="button"
        aria-pressed={following}
        aria-label={`Follow ${character.name} on the map`}
        title="Follow on the map"
        onClick={() => toggleFollow(character.id)}
        className="relative cursor-pointer hover:brightness-125 focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-fg"
      >
        <MarkerBadge marker={FOLLOW_ICON} selected={following} className="size-6" />
      </button>
    </li>
  )
}
