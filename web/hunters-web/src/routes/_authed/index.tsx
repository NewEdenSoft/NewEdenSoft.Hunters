import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { discoveryQuery } from '../../api/discovery'
import { Label } from '../../components/ui/Label'
import { PageHeader } from '../../components/ui/PageHeader'
import { Panel } from '../../components/ui/Panel'
import { Status } from '../../components/ui/Status'

export const Route = createFileRoute('/_authed/')({
  component: HomePage,
})

function HomePage() {
  const discovery = useQuery(discoveryQuery)

  return (
    <>
      <PageHeader label="NewEdenSoft · Hunters">
        A tactical HUD for <span className="text-primary text-shadow-glow">hunting in New Eden</span>
      </PageHeader>

      <Panel>
        <Label>Server</Label>
        {discovery.isPending && <p className="text-fg-muted">Connecting…</p>}
        {discovery.isError && (
          <Status tone="danger" role="alert">
            Could not reach the server: {discovery.error.message}
          </Status>
        )}
        {discovery.isSuccess && (
          <Status tone="ok">
            Connected to <strong>{discovery.data.name}</strong>
            <span className="font-mono text-fg-muted">API {discovery.data.apiVersion}</span>
          </Status>
        )}
      </Panel>
    </>
  )
}
