import { useEffect, useState } from 'react'
import { getDiscoveryDocument, type DiscoveryDocument } from './api/discovery'
import { ThemeBar } from './components/ThemeBar'
import { securityColor } from './theme/themes'
import { useTheme } from './theme/useTheme'

type ServerState =
  | { status: 'loading' }
  | { status: 'ready'; server: DiscoveryDocument }
  | { status: 'error'; message: string }

const SECURITY_STEPS = [1, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3, 0.2, 0.1, 0, -1]

function App() {
  const [theme, selectTheme] = useTheme()
  const [state, setState] = useState<ServerState>({ status: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    getDiscoveryDocument(controller.signal)
      .then((server) => setState({ status: 'ready', server }))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setState({ status: 'error', message: error instanceof Error ? error.message : String(error) })
        }
      })
    return () => controller.abort()
  }, [])

  return (
    <>
      <ThemeBar current={theme} onSelect={selectTheme} />
      <main className="wrap page">
        <section className="hero">
          <span className="label accent">NewEdenSoft · Hunters · Web</span>
          <h1>
            A tactical HUD for <span className="accent glow">hunting in New Eden</span>
          </h1>
        </section>

        <section className="panel">
          <span className="label">Server</span>
          {state.status === 'loading' && <p className="muted">Connecting…</p>}
          {state.status === 'error' && (
            <p role="alert" className="status status-danger">
              Could not reach the server: {state.message}
            </p>
          )}
          {state.status === 'ready' && (
            <p className="status status-ok">
              Connected to <strong>{state.server.name}</strong> <span className="mono muted">API {state.server.apiVersion}</span>
            </p>
          )}
        </section>

        <section className="panel">
          <span className="label">Controls</span>
          <div className="row">
            <button type="button" className="btn primary">Add character</button>
            <button type="button" className="btn">Set as main</button>
            <button type="button" className="btn ghost">Cancel</button>
            <button type="button" className="btn danger">Remove</button>
          </div>
        </section>

        <section className="panel">
          <span className="label">Security status · fixed</span>
          <div className="scale">
            {SECURITY_STEPS.map((s) => (
              <div className="scale-step" key={s}>
                <div className="scale-bar" style={{ background: securityColor(s) }} />
                <span className="mono muted">{s < 0 ? `−${Math.abs(s).toFixed(1)}` : s.toFixed(1)}</span>
              </div>
            ))}
          </div>
        </section>
      </main>
    </>
  )
}

export default App
