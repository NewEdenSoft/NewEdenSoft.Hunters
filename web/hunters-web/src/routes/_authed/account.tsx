import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { accountQuery, deleteAccount, needsReauth, removeCharacter, setMainCharacter } from '../../api/account'
import { CharacterRow } from '../../components/account/CharacterRow'
import { EveLoginButton } from '../../components/account/EveLoginButton'
import { Button } from '../../components/ui/Button'
import { Label } from '../../components/ui/Label'
import { PageHeader } from '../../components/ui/PageHeader'
import { Panel } from '../../components/ui/Panel'
import { Status } from '../../components/ui/Status'
import { WarningIcon } from '../../components/ui/WarningIcon'

interface AccountSearch {
  /** A failed add-character or re-auth login, forwarded from /login. */
  error?: string
  message?: string
}

export const Route = createFileRoute('/_authed/account')({
  validateSearch: (search: Record<string, unknown>): AccountSearch => ({
    error: typeof search.error === 'string' ? search.error : undefined,
    message: typeof search.message === 'string' ? search.message : undefined,
  }),
  component: AccountPage,
})

function AccountPage() {
  const { data: account } = useSuspenseQuery(accountQuery)
  const { error: loginError, message: loginMessage } = Route.useSearch()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  const refresh = () => queryClient.invalidateQueries({ queryKey: accountQuery.queryKey })
  const makeMain = useMutation({ mutationFn: setMainCharacter, onSettled: refresh })
  const remove = useMutation({ mutationFn: removeCharacter, onSettled: refresh })
  const destroy = useMutation({
    mutationFn: deleteAccount,
    onSuccess: async () => {
      // Every session of the account is gone, including this one.
      queryClient.clear()
      await navigate({ to: '/login' })
    },
  })

  const busy = makeMain.isPending || remove.isPending || destroy.isPending
  const failure = makeMain.error ?? remove.error ?? destroy.error
  const characters = [...account.characters].sort(
    (a, b) => Number(b.isMain) - Number(a.isMain) || a.name.localeCompare(b.name),
  )
  const attention = characters.some(needsReauth)

  return (
    <>
      <PageHeader label="System">Account</PageHeader>

      {loginError && (
        <Status tone="danger" role="alert">
          {loginMessage ?? 'The login failed. Try again.'}
        </Status>
      )}
      {failure && (
        <Status tone="danger" role="alert">
          {failure.message}
        </Status>
      )}

      <Panel>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <Label>Characters</Label>
          <EveLoginButton returnUrl="/account" disabled={busy}>
            Add character
          </EveLoginButton>
        </div>

        {attention && (
          <p className="flex items-center gap-2 text-[0.8125rem] text-warning">
            <WarningIcon />
            Some characters need a new login before Hunters can use them.
          </p>
        )}

        {characters.length === 0 ? (
          <p className="text-fg-muted">No characters on this account. Add one to use Hunters.</p>
        ) : (
          <ul className="grid gap-1.5">
            {characters.map((character) => (
              <CharacterRow
                key={character.id}
                character={character}
                busy={busy}
                onMakeMain={() => makeMain.mutate(character.id)}
                onRemove={() => remove.mutate(character.id)}
              />
            ))}
          </ul>
        )}
      </Panel>

      <Panel>
        <Label tone="danger">Delete account</Label>
        <p className="text-[0.8125rem] text-fg-muted">
          Deletes your account on this server with all its characters and sessions, and revokes the server's access to your
          characters at EVE. This cannot be undone.
        </p>
        {confirmingDelete ? (
          <div className="flex flex-wrap items-center gap-2">
            <span>Delete everything?</span>
            <Button variant="danger" disabled={busy} onClick={() => destroy.mutate()}>
              Delete everything
            </Button>
            <Button variant="ghost" onClick={() => setConfirmingDelete(false)}>
              Cancel
            </Button>
          </div>
        ) : (
          <div>
            <Button variant="danger" disabled={busy} onClick={() => setConfirmingDelete(true)}>
              Delete account
            </Button>
          </div>
        )}
      </Panel>
    </>
  )
}
