import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { discoveryQuery } from '../../api/discovery'

export const Route = createFileRoute('/_authed/')({
  component: HomePage,
})

function HomePage() {
  const discovery = useQuery(discoveryQuery)

  return (
    <>
      <section className="hero">
        <span className="label accent">NewEdenSoft · Hunters</span>
        <h1>
          A tactical HUD for <span className="accent glow">hunting in New Eden</span>
        </h1>
      </section>

      <section className="panel">
        <span className="label">Server</span>
        {discovery.isPending && <p className="muted">Connecting…</p>}
        {discovery.isError && (
          <p role="alert" className="status status-danger">
            Could not reach the server: {discovery.error.message}
          </p>
        )}
        {discovery.isSuccess && (
          <p className="status status-ok">
            Connected to <strong>{discovery.data.name}</strong>{' '}
            <span className="mono muted">API {discovery.data.apiVersion}</span>
          </p>
        )}
      </section>
    </>
  )
}
