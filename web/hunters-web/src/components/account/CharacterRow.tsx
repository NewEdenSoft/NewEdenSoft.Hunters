import { useState } from 'react'
import { needsReauth, type CharacterInfo } from '../../api/account'
import { characterPortraitUrl } from '../../api/eveImages'
import { getMarker, setMarker, setTracked, useCharacterPrefs } from '../../characters/characterPrefs'
import { ICONS, LETTERS, NUMBERS, sameMarker, type CharacterMarker } from '../../characters/markers'
import { useCharacterStatus } from '../../characters/useCharacterStatus'
import { MarkerBadge } from '../characters/MarkerBadge'
import { Button } from '../ui/Button'
import { cn } from '../ui/styles'
import { Switch } from '../ui/Switch'
import { WarningIcon } from '../ui/WarningIcon'
import { EveLoginButton } from './EveLoginButton'

interface CharacterRowProps {
  character: CharacterInfo
  /** Another request is running; actions wait. */
  busy: boolean
  onMakeMain: () => void
  onRemove: () => void
}

const statusText: Record<CharacterInfo['status'], string | null> = {
  Active: null,
  NeedsReauth: 'Access expired or was revoked. Log in with this character again.',
  MissingScopes: 'The server needs more permissions. Log in with this character again.',
}

export function CharacterRow({ character, busy, onMakeMain, onRemove }: CharacterRowProps) {
  const [confirmingRemove, setConfirmingRemove] = useState(false)
  const [pickingMarker, setPickingMarker] = useState(false)
  const attention = needsReauth(character)
  const status = statusText[character.status]
  useCharacterPrefs()
  const marker = getMarker(character.id, character.name)
  const tracking = useCharacterStatus(character)

  return (
    <li
      className={cn(
        'flex flex-wrap items-center gap-x-3 gap-y-2 border bg-surface-2 px-3 py-2.5',
        attention ? 'border-warning' : 'border-line',
      )}
    >
      {/* Map marker; opens the picker below */}
      <button
        type="button"
        aria-label="Change map marker"
        aria-expanded={pickingMarker}
        title="Map marker"
        onClick={() => setPickingMarker(!pickingMarker)}
        className="cursor-pointer hover:brightness-125 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-fg"
      >
        <MarkerBadge marker={marker} selected={pickingMarker} />
      </button>

      {/* Portrait, with the warning icon on its corner when the character needs a new login */}
      <div className="relative size-11 shrink-0">
        <img
          src={characterPortraitUrl(character.id, 64)}
          alt=""
          width={44}
          height={44}
          className={cn('size-11 border bg-bg object-cover', attention ? 'border-warning' : 'border-line-strong')}
        />
        {attention && (
          <span className="absolute -right-1.5 -bottom-1.5 border border-warning bg-bg p-0.5">
            <WarningIcon className="size-3" title={status ?? undefined} />
          </span>
        )}
      </div>

      <div className="grid min-w-0 flex-1 gap-0.5">
        <div className="flex flex-wrap items-center gap-2">
          <h3 className="text-base">{character.name}</h3>
          {character.isMain && (
            <span className="border border-line-strong px-1.5 font-mono text-[0.625rem] tracking-wide text-primary">MAIN</span>
          )}
        </div>
        {status && <p className="text-[0.8125rem] text-warning">{status}</p>}
        {!attention && (
          <p title={tracking.details} className="flex min-w-0 items-center gap-1.5 text-[0.8125rem]">
            <span className={cn('size-1.75 shrink-0 rounded-full', tracking.online ? 'bg-ok' : 'bg-fg-faint')} />
            <span className={cn('truncate', tracking.problem ? 'text-warning' : 'text-fg-muted')}>{tracking.line}</span>
          </p>
        )}
      </div>

      {confirmingRemove ? (
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="text-[0.8125rem] text-fg-muted">Remove from account?</span>
          <Button variant="danger" disabled={busy} onClick={onRemove}>
            Remove
          </Button>
          <Button variant="ghost" onClick={() => setConfirmingRemove(false)}>
            Keep
          </Button>
        </div>
      ) : (
        <div className="flex flex-wrap items-center gap-1.5">
          {!attention && (
            <label className="mr-2 flex items-center gap-2 font-display text-[0.6875rem] font-semibold tracking-[0.16em] text-fg-muted uppercase">
              Track
              <Switch
                checked={tracking.tracked}
                onChange={(on) => setTracked(character.id, on)}
                label="Poll this character's location, online status and ship while Hunters is open"
              />
            </label>
          )}
          {attention && (
            <EveLoginButton returnUrl="/account" variant="primary" disabled={busy}>
              Log in again
            </EveLoginButton>
          )}
          {!character.isMain && (
            <Button variant="ghost" disabled={busy} onClick={onMakeMain}>
              Make main
            </Button>
          )}
          <Button variant="ghost" disabled={busy} onClick={() => setConfirmingRemove(true)}>
            Remove
          </Button>
        </div>
      )}

      {pickingMarker && (
        <MarkerPicker
          current={marker}
          onPick={(m) => {
            setMarker(character.id, m)
            setPickingMarker(false)
          }}
        />
      )}
    </li>
  )
}

/** Letters, numbers and icons to mark the character with on the map. */
function MarkerPicker({ current, onPick }: { current: CharacterMarker; onPick: (marker: CharacterMarker) => void }) {
  const groups: [string, CharacterMarker[]][] = [
    ['Letters', LETTERS],
    ['Numbers', NUMBERS],
    ['Icons', ICONS],
  ]
  return (
    <div className="grid w-full gap-2.5 border-t border-line pt-2.5">
      <p className="text-[0.8125rem] text-fg-muted">Shown on the map, joined to this character's system.</p>
      {groups.map(([label, markers]) => (
        <div key={label} className="flex flex-wrap items-start gap-3">
          <span className="w-18 pt-1.5 font-display text-[0.6875rem] font-semibold tracking-[0.16em] text-fg-muted uppercase">{label}</span>
          <div className="flex flex-1 flex-wrap gap-1">
            {markers.map((m) => (
              <button
                key={m.value}
                type="button"
                aria-label={`${label.slice(0, -1)} ${m.value}`}
                aria-pressed={sameMarker(m, current)}
                onClick={() => onPick(m)}
                className="cursor-pointer hover:brightness-125 focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-fg"
              >
                <MarkerBadge marker={m} selected={sameMarker(m, current)} />
              </button>
            ))}
          </div>
        </div>
      ))}
    </div>
  )
}
