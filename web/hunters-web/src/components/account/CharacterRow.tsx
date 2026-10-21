import { useState } from 'react'
import { needsReauth, type CharacterInfo } from '../../api/account'
import { characterPortraitUrl } from '../../api/eveImages'
import { Button } from '../ui/Button'
import { cn } from '../ui/styles'
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
  const attention = needsReauth(character)
  const status = statusText[character.status]

  return (
    <li
      className={cn(
        'flex flex-wrap items-center gap-x-3 gap-y-2 border bg-surface-2 px-3 py-2.5',
        attention ? 'border-warning' : 'border-line',
      )}
    >
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
    </li>
  )
}
