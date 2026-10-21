import { createFileRoute } from '@tanstack/react-router'
import { Label } from '../../components/ui/Label'
import { PageHeader } from '../../components/ui/PageHeader'
import { Panel } from '../../components/ui/Panel'

export const Route = createFileRoute('/_authed/account')({
  component: AccountPage,
})

// Character management follows in the next PR.
function AccountPage() {
  return (
    <>
      <PageHeader label="System">Account</PageHeader>
      <Panel>
        <Label>Characters</Label>
        <p className="text-fg-muted">Character management is coming next.</p>
      </Panel>
    </>
  )
}
